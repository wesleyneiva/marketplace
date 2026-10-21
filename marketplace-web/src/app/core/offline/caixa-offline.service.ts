import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Produto } from '../api/produtos.api';
import { Cupom, Gerente, NovaVenda, ResumoCaixa } from '../api/pdv.api';
import { Conexao } from './conexao';

// ===================== CAIXA OFFLINE =====================
// Quando a internet cai, o caixa continua vendendo: o catálogo de produtos fica guardado NESTE computador
// (IndexedDB, renovado a cada abertura do caixa e a cada 10 minutos) e as vendas feitas sem internet vão para uma
// FILA, também neste computador. Quando a internet volta, a fila é enviada sozinha (POST /api/vendas/offline), em ordem.
// Cada venda leva um idLocal: se o envio se repetir (a rede caiu no meio da resposta), o servidor não grava duas vezes.
// A fila é por usuário: se outra pessoa entrar neste computador, as vendas do anterior esperam ele voltar.

export interface ProdutoCatalogo {
  id: number; nome: string; codigoBarras: string | null; unidade: string; precoVenda: number; estoqueAtual: number;
  modeloId?: number | null; tamanho?: string | null; cor?: string | null; servico?: boolean; // grade e serviço
}

export interface ItemFila {
  idLocal: string;
  tipo: 'venda' | 'cancelamento';
  usuarioId: string;
  empresaId: number;
  criadoEm: string;
  corpo: unknown;      // o que vai para a API
  cupom?: Cupom;       // venda: o cupom que o cliente recebeu (para reimprimir e mostrar)
  erro?: string;       // o servidor recusou (fica na fila, com o motivo, até alguém resolver)
}

export interface PixFixo { payload: string; svg: string; recebedor: string; chave: string; }

const BANCO = 'marcus-caixa-offline';

@Injectable({ providedIn: 'root' })
export class CaixaOffline {
  private readonly http = inject(HttpClient);
  private readonly conexao = inject(Conexao);

  private usuarioId = '';
  private empresaId = 0;
  private catalogo: (ProdutoCatalogo & { busca: string })[] = [];
  private renovar?: ReturnType<typeof setInterval>;
  private envio?: ReturnType<typeof setInterval>;

  readonly fila = signal<ItemFila[]>([]);                 // os itens DESTE usuário ainda não enviados
  readonly vendasPendentes = computed(() => this.fila().filter((i) => i.tipo === 'venda'));
  readonly comErro = computed(() => this.fila().filter((i) => i.erro));
  readonly enviando = signal(false);
  readonly catalogoEm = signal<string | null>(null);      // quando o catálogo foi guardado
  readonly outrosUsuarios = signal(0);                     // vendas de outro operador esperando neste computador
  readonly pixFixo = signal<PixFixo | null>(null);
  readonly gerentes = signal<Gerente[]>([]);

  constructor() {
    this.conexao.quandoVoltar(() => void this.sincronizar());
  }

  // O caixa abriu a tela: carrega o que está guardado, renova o catálogo (com internet) e envia o que ficou pendente.
  async iniciar(usuarioId: string, empresaId: number): Promise<void> {
    this.usuarioId = usuarioId;
    this.empresaId = empresaId;
    const guardado = await ler<{ em: string; itens: ProdutoCatalogo[] }>(`catalogo:${empresaId}`);
    if (guardado) this.usarCatalogo(guardado.itens, guardado.em);
    this.pixFixo.set((await ler<PixFixo>(`pixFixo:${empresaId}`)) ?? null);
    this.gerentes.set((await ler<Gerente[]>(`gerentes:${empresaId}`)) ?? []);
    await this.recarregarFila();
    void this.atualizarCatalogo();
    void this.sincronizar();
    clearInterval(this.renovar);
    this.renovar = setInterval(() => void this.atualizarCatalogo(), 10 * 60_000);
    clearInterval(this.envio);
    this.envio = setInterval(() => { if (this.fila().some((i) => !i.erro)) void this.sincronizar(); }, 20_000);
  }

  parar(): void {
    clearInterval(this.renovar);
    clearInterval(this.envio);
  }

  temCatalogo(): boolean {
    return this.catalogo.length > 0;
  }

  // Baixa o catálogo, o QR fixo do Pix e os gerentes (para a lista de autorização). Sem internet, fica o que já tinha.
  async atualizarCatalogo(): Promise<void> {
    if (!this.empresaId) return;
    try {
      const itens = await firstValueFrom(this.http.get<ProdutoCatalogo[]>('/api/caixa/catalogo'));
      const em = new Date().toISOString();
      await gravar(`catalogo:${this.empresaId}`, { em, itens });
      this.usarCatalogo(itens, em);
      this.conexao.voltou();
      const pix = await firstValueFrom(this.http.get<PixFixo | null>('/api/empresa/configuracao/pix/qrcode-fixo')).catch(() => null);
      this.pixFixo.set(pix);
      await gravar(`pixFixo:${this.empresaId}`, pix);
      const gerentes = await firstValueFrom(this.http.get<Gerente[]>('/api/caixa/gerentes')).catch(() => null);
      if (gerentes) { this.gerentes.set(gerentes); await gravar(`gerentes:${this.empresaId}`, gerentes); }
    } catch (e) {
      this.conexao.falhou(e);
    }
  }

  private usarCatalogo(itens: ProdutoCatalogo[], em: string): void {
    this.catalogo = itens.map((p) => ({ ...p, busca: normalizar(p.nome) }));
    this.catalogoEm.set(em);
  }

  // Busca no catálogo guardado: código de barras exato ou palavras do nome em qualquer ordem (sem acento).
  buscar(termo: string, maximo = 8): Produto[] {
    const t = termo.trim();
    if (!t) return [];
    const exato = this.catalogo.filter((p) => p.codigoBarras === t);
    if (exato.length || /^\d+$/.test(t)) return exato.map(comoProduto);
    const palavras = normalizar(t).split(/\s+/).filter(Boolean);
    return this.catalogo
      .filter((p) => palavras.every((w) => p.busca.includes(w)))
      .sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR'))
      .filter((p, i, lista) => !p.modeloId || lista.findIndex((x) => x.modeloId === p.modeloId) === i) // grade: 1 por modelo
      .slice(0, maximo)
      .map(comoProduto);
  }

  // As variações de um modelo da grade guardadas neste computador (o caixa escolhe o tamanho sem internet).
  variacoes(modeloId: number): Produto[] {
    return this.catalogo.filter((p) => p.modeloId === modeloId).map(comoProduto);
  }

  // ----- Resumo do caixa (para a tela abrir sem internet) -----
  async guardarResumo(r: ResumoCaixa | null): Promise<void> {
    if (this.usuarioId) await gravar(`resumo:${this.usuarioId}`, r);
  }

  lerResumo(usuarioId: string): Promise<ResumoCaixa | null> {
    return ler<ResumoCaixa>(`resumo:${usuarioId}`).then((r) => r ?? null);
  }

  // ----- Fila -----
  async guardarVenda(venda: NovaVenda, cupom: Cupom, idLocal: string, dataHora: string): Promise<void> {
    await this.colocar({
      idLocal, tipo: 'venda', usuarioId: this.usuarioId, empresaId: this.empresaId, criadoEm: dataHora, cupom,
      corpo: {
        idLocal, dataHora, desconto: venda.desconto, pagamentos: venda.pagamentos,
        itens: cupom.itens.map((i) => ({ produtoId: i.produtoId, quantidade: i.quantidade, precoUnitario: i.precoUnitario })),
      },
    });
  }

  async guardarCancelamento(tipo: 'Item' | 'Compra', itens: { produtoId: number; quantidade: number }[], motivo: string, gerenteId: string | null): Promise<void> {
    const idLocal = novoIdLocal(), dataHora = new Date().toISOString();
    await this.colocar({
      idLocal, tipo: 'cancelamento', usuarioId: this.usuarioId, empresaId: this.empresaId, criadoEm: dataHora,
      corpo: { idLocal, dataHora, tipo, itens, motivo, gerenteId },
    });
  }

  private async colocar(item: ItemFila): Promise<void> {
    await gravar(`fila:${item.idLocal}`, item);
    await this.recarregarFila();
    void this.sincronizar();
  }

  private async recarregarFila(): Promise<void> {
    const todos = (await lerTodos<ItemFila>('fila:')).sort((a, b) => a.criadoEm.localeCompare(b.criadoEm));
    this.fila.set(todos.filter((i) => i.usuarioId === this.usuarioId && i.empresaId === this.empresaId));
    this.outrosUsuarios.set(todos.filter((i) => i.usuarioId !== this.usuarioId && i.tipo === 'venda').length);
  }

  // Envia a fila, em ordem. Sem internet: para e tenta de novo depois. Recusado pelo servidor: fica com o motivo.
  async sincronizar(forcar = false): Promise<void> {
    if (this.enviando() || !this.usuarioId) return;
    const pendentes = this.fila().filter((i) => forcar || !i.erro);
    if (!pendentes.length) return;
    this.enviando.set(true);
    try {
      for (const item of pendentes) {
        try {
          const url = item.tipo === 'venda' ? '/api/vendas/offline' : '/api/caixa/cancelamentos/offline';
          await firstValueFrom(this.http.post(url, item.corpo));
          await apagar(`fila:${item.idLocal}`);
          this.conexao.voltou();
        } catch (e) {
          if (this.conexao.falhou(e)) break; // ainda sem internet: tenta depois
          const mensagem = (e instanceof HttpErrorResponse && e.error?.mensagem) || 'O servidor recusou.';
          await gravar(`fila:${item.idLocal}`, { ...item, erro: mensagem });
        }
      }
    } finally {
      await this.recarregarFila();
      this.enviando.set(false);
    }
  }
}

// O Produto completo que a tela do caixa usa (o catálogo guardado tem só o necessário para vender).
function comoProduto(p: ProdutoCatalogo): Produto {
  return {
    id: p.id, nome: p.nome, codigoBarras: p.codigoBarras, unidade: p.unidade, precoVenda: p.precoVenda, estoqueAtual: p.estoqueAtual,
    categoriaId: 0, categoria: '', precoCusto: 0, margemPercentual: 0, estoqueMinimo: 0, estoqueBaixo: false, controlaValidade: false,
    ativo: true, fornecedorId: null, fornecedor: null, codigoBalanca: null, ncm: null, cest: null, cfop: '', origem: 0, situacaoTributaria: null,
    modeloId: p.modeloId ?? null, tamanho: p.tamanho ?? null, cor: p.cor ?? null, servico: p.servico ?? false,
  };
}

function normalizar(t: string): string {
  return t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

// Identificador único da venda feita sem internet (crypto.randomUUID só existe em https; aqui vale em http também).
export function novoIdLocal(): string {
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

// ----- IndexedDB (um "guarda-volumes" chave → valor, que aguenta milhares de produtos) -----
let conexaoBanco: Promise<IDBDatabase> | null = null;

function banco(): Promise<IDBDatabase> {
  conexaoBanco ??= new Promise((ok, erro) => {
    const pedido = indexedDB.open(BANCO, 1);
    pedido.onupgradeneeded = () => pedido.result.createObjectStore('dados');
    pedido.onsuccess = () => ok(pedido.result);
    pedido.onerror = () => { conexaoBanco = null; erro(pedido.error); };
  });
  return conexaoBanco;
}

async function operar<T>(modo: IDBTransactionMode, acao: (s: IDBObjectStore) => IDBRequest): Promise<T> {
  const db = await banco();
  return new Promise((ok, erro) => {
    const t = db.transaction('dados', modo);
    const pedido = acao(t.objectStore('dados'));
    t.oncomplete = () => ok(pedido.result as T);
    t.onerror = () => erro(t.error);
  });
}

function ler<T>(chave: string): Promise<T | undefined> {
  return operar<T | undefined>('readonly', (s) => s.get(chave)).catch(() => undefined);
}

function gravar(chave: string, valor: unknown): Promise<void> {
  return operar<void>('readwrite', (s) => s.put(valor, chave));
}

function apagar(chave: string): Promise<void> {
  return operar<void>('readwrite', (s) => s.delete(chave));
}

function lerTodos<T>(prefixo: string): Promise<T[]> {
  return operar<T[]>('readonly', (s) => s.getAll(IDBKeyRange.bound(prefixo, prefixo + '￿'))).catch(() => []);
}

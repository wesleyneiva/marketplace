import { Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ItemSugestao, Pedido, SugestaoFornecedor } from '../../core/api/compras.api';
import { ContaFinanceira } from '../../core/api/financeiro.api';
import { DiaPipe } from '../../shared/dia.pipe';
import { QuantidadePipe } from '../../shared/quantidade.pipe';
import { Icone } from '../../shared/icone';
import { CampoData } from '../../shared/campo-data';
import { ProdutoBusca } from '../../shared/produto-busca';
import { Produto } from '../../core/api/produtos.api';
import { EnviarPedido } from './enviar-pedido';
import { ComprasResumo } from '../../core/compras-resumo';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

interface Escolha { marcado: boolean; quantidade: number; custo: number }
interface ItemNovo { produtoId: number; produto: string; unidade: string; estoqueAtual: number; estoqueMinimo: number; fornecedorId: number | null; fornecedor: string | null; quantidade: number; custo: number }
interface Fornecedor { id: number; nome: string; prazoEntregaDias: number; ativo: boolean }
interface Conferencia { itemId: number; produto: string; unidade: string; perecivel: boolean; pedido: number; recebido: number; validade: string; custo: number }

@Component({
  selector: 'app-compras',
  imports: [Icone, FormsModule, RouterLink, CurrencyPipe, DatePipe, DecimalPipe, DiaPipe, QuantidadePipe, CampoData, ProdutoBusca, EnviarPedido, FecharAoClicarFora],
  templateUrl: './compras.html',
  styleUrl: './compras.scss',
})
export class Compras {
  private readonly http = inject(HttpClient);
  private readonly resumo = inject(ComprasResumo); // número no menu
  private readonly envio = viewChild.required(EnviarPedido); // janela "Enviar pedido ao fornecedor" (WhatsApp, e-mail, copiar)

  // ?aba=pedidos (vindo do aviso do painel) abre direto nos pedidos.
  readonly abaInicial = input<string | undefined>(undefined, { alias: 'aba' });
  protected readonly aba = signal<'sugestao' | 'pedidos'>('sugestao');

  ngOnInit(): void {
    if (this.abaInicial() === 'pedidos') this.aba.set('pedidos');
  }

  protected readonly aviso = signal<string | null>(null);
  protected readonly ocupado = signal(false);

  // ---------------------------------------------------------------- sugestão
  protected readonly sugestao = httpResource<SugestaoFornecedor[]>(() => (this.aba() === 'sugestao' ? '/api/compras/sugestao' : undefined));

  // Escolhas do usuário por produto (marcado? quanto? por quanto?). Começa com o sugerido.
  private readonly escolhas = new Map<number, Escolha>();
  protected escolha(i: ItemSugestao): Escolha {
    let e = this.escolhas.get(i.produtoId);
    if (!e) {
      e = { marcado: true, quantidade: i.sugerido, custo: i.custoUnitario };
      this.escolhas.set(i.produtoId, e);
    }
    return e;
  }

  protected totalSelecionado(s: SugestaoFornecedor): number {
    return s.itens.reduce((t, i) => {
      const e = this.escolha(i);
      return e.marcado ? t + (e.quantidade || 0) * (e.custo || 0) : t;
    }, 0);
  }

  protected selecionados(s: SugestaoFornecedor): number {
    return s.itens.filter((i) => this.escolha(i).marcado && this.escolha(i).quantidade > 0).length;
  }

  // Os pedidos saem SÓ daqui (ou do "Novo pedido"): o pedido nasce na hora de abrir a janela de envio (para ter o
  // número na mensagem) e só aparece em Pedidos depois de mandado. Fechou sem mandar? Ele é apagado (desistiuDoEnvio).
  async enviarSugestao(s: SugestaoFornecedor): Promise<void> {
    const itens = s.itens
      .filter((i) => this.escolha(i).marcado && this.escolha(i).quantidade > 0)
      .map((i) => ({ produtoId: i.produtoId, quantidade: this.escolha(i).quantidade, custoUnitario: this.escolha(i).custo }));
    if (!itens.length) return alert('Marque pelo menos um item.');

    await this.executar(async () => {
      const p = await firstValueFrom(this.http.post<Pedido>('/api/compras/pedidos', { fornecedorId: s.fornecedorId, itens, enviar: false }));
      void this.envio().abrir(p);
    });
  }

  // ---------------------------------------------------------------- pedidos
  protected readonly filtro = signal<'abertos' | 'Recebido' | 'Cancelado' | 'todos'>('abertos');
  protected readonly pedidos = httpResource<Pedido[]>(() => {
    if (this.aba() !== 'pedidos') return undefined;
    const filtro = this.filtro();
    const params: Record<string, string> = filtro === 'Recebido' || filtro === 'Cancelado' ? { status: filtro } : {};
    return { url: '/api/compras/pedidos', params };
  });
  protected readonly pedidosVisiveis = computed(() => {
    const lista = this.pedidos.value() ?? [];
    return this.filtro() === 'abertos' ? lista.filter((p) => p.status === 'Enviado') : lista;
  });
  protected readonly aberto = signal<number | null>(null);

  // O fornecedor respondeu com a data dele: muda a previsão (é ela que marca o pedido como atrasado).
  protected novaPrevisao: Record<number, string> = {};
  protected readonly avisoPrevisao = signal<{ id: number; texto: string } | null>(null);

  async salvarPrevisao(p: Pedido): Promise<void> {
    const previsao = this.novaPrevisao[p.id];
    if (!previsao) return;
    await this.executar(async () => {
      await firstValueFrom(this.http.put(`/api/compras/pedidos/${p.id}/previsao`, { previsao }));
      delete this.novaPrevisao[p.id];
      this.avisoPrevisao.set({ id: p.id, texto: `Entrega prevista para ${this.dia(previsao)}.` });
      setTimeout(() => this.avisoPrevisao.set(null), 5000);
      this.pedidos.reload();
    });
  }

  // Reenviar (já enviado): abre a janela com a mensagem pronta e os canais do fornecedor.
  reenviar(p: Pedido): void {
    void this.envio().abrir(p);
  }

  aoEnviar(r: Pedido): void {
    r.itens.forEach((i) => this.escolhas.delete(i.produtoId));
    this.mostrarAviso(`Pedido #${r.id} enviado` + (r.previsaoEntrega ? `: entrega prevista em ${this.dia(r.previsaoEntrega)}` : '') +
      '. Ele está em Pedidos, esperando a entrega para você conferir.');
    this.sugestao.reload();
    this.pedidos.reload();
    this.resumo.recarregar();
  }

  // Fechou a janela sem mandar: o pedido montado some (nada fica parado como se tivesse sido pedido).
  async desistiuDoEnvio(p: Pedido): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/compras/pedidos/${p.id}`)); } catch { /* sobra de outro dia é apagada no próximo pedido */ }
  }

  async cancelar(p: Pedido): Promise<void> {
    if (!confirm(`Cancelar o pedido #${p.id} (${p.fornecedor})?`)) return;
    await this.executar(async () => {
      await firstValueFrom(this.http.post(`/api/compras/pedidos/${p.id}/cancelar`, {}));
      this.mostrarAviso(`Pedido #${p.id} cancelado.`);
      this.pedidos.reload();
    });
  }

  // ----- Pedido manual (sem passar pela sugestão) -----
  private readonly dlgNovo = viewChild<ElementRef<HTMLDialogElement>>('dlgNovo');
  private readonly carregarFornecedores = signal(false);
  protected readonly fornecedores = httpResource<Fornecedor[]>(() => (this.carregarFornecedores() ? '/api/fornecedores' : undefined));
  protected readonly fornecedoresAtivos = computed(() => (this.fornecedores.value() ?? []).filter((f) => f.ativo));
  protected novoFornecedorId: number | null = null;
  protected novaObservacao = '';
  protected readonly novosItens = signal<ItemNovo[]>([]);
  protected readonly erroNovo = signal<string | null>(null);

  abrirNovoPedido(): void {
    this.carregarFornecedores.set(true);
    this.novoFornecedorId = null;
    this.novaObservacao = '';
    this.novosItens.set([]);
    this.erroNovo.set(null);
    this.dlgNovo()?.nativeElement.showModal();
  }

  adicionarItem(p: Produto): void {
    this.erroNovo.set(null);
    if (this.novosItens().some((i) => i.produtoId === p.id)) return;
    if (this.novoFornecedorId === null && p.fornecedorId) this.novoFornecedorId = p.fornecedorId;
    // Sugere repor até o dobro do mínimo; senão, 1.
    const repor = p.estoqueMinimo * 2 - p.estoqueAtual;
    const quantidade = repor > 0 ? (p.unidade === 'KG' || p.unidade === 'L' ? Math.ceil(repor * 2) / 2 : Math.ceil(repor)) : 1;
    this.novosItens.update((l) => [...l, {
      produtoId: p.id, produto: p.nome, unidade: p.unidade, estoqueAtual: p.estoqueAtual, estoqueMinimo: p.estoqueMinimo,
      fornecedorId: p.fornecedorId, fornecedor: p.fornecedor, quantidade, custo: p.precoCusto,
    }]);
  }

  removerItem(i: ItemNovo): void {
    this.novosItens.update((l) => l.filter((x) => x !== i));
  }

  protected totalNovo(): number {
    return this.novosItens().reduce((t, i) => t + (Number(i.quantidade) || 0) * (Number(i.custo) || 0), 0);
  }

  async enviarNovo(): Promise<void> {
    if (this.novoFornecedorId === null) return this.erroNovo.set('Escolha o fornecedor.');
    const itens = this.novosItens().filter((i) => Number(i.quantidade) > 0)
      .map((i) => ({ produtoId: i.produtoId, quantidade: Number(i.quantidade), custoUnitario: Number(i.custo) || null }));
    if (!itens.length) return this.erroNovo.set('Adicione pelo menos um produto com quantidade.');

    this.ocupado.set(true);
    try {
      const p = await firstValueFrom(this.http.post<Pedido>('/api/compras/pedidos',
        { fornecedorId: this.novoFornecedorId, itens, enviar: false, observacao: this.novaObservacao }));
      this.dlgNovo()?.nativeElement.close();
      void this.envio().abrir(p);
    } catch (e) {
      this.erroNovo.set(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  // ----- Conferência na entrega -----
  private readonly dlgReceber = viewChild<ElementRef<HTMLDialogElement>>('dlgReceber');
  protected readonly recebendo = signal<Pedido | null>(null);
  protected conferencia: Conferencia[] = [];
  protected readonly erroReceber = signal<string | null>(null);

  // Pagamento da mercadoria (gera a conta a pagar — é o que faz o dinheiro sair no Financeiro).
  protected readonly formasNaEntrega = ['Dinheiro', 'PIX', 'Transferência', 'Cartão de débito', 'Cartão de crédito', 'Cheque'];
  private readonly carregarContas = signal(false);
  protected readonly contasFinanceiras = httpResource<ContaFinanceira[]>(() => (this.carregarContas() ? '/api/financeiro/contas' : undefined));
  protected readonly contasAtivas = computed(() => (this.contasFinanceiras.value() ?? []).filter((c) => c.ativa && c.tipo !== 'Maquininha'));
  protected pagamento = { modo: 'Boleto' as 'Boleto' | 'NaEntrega' | 'Nota', vencimento: '', documento: '', forma: 'Dinheiro', contaId: null as number | null, valor: null as number | null };

  // Total do que chegou (quantidade conferida × custo): é o valor da conta, se ninguém mudar.
  protected totalRecebido(): number {
    return Math.round(this.conferencia.reduce((t, c) => t + (Number(c.recebido) || 0) * (Number(c.custo) || 0), 0) * 100) / 100;
  }

  protected escolherForma(forma: string): void {
    this.pagamento.forma = forma;
    const tipo = forma === 'Dinheiro' ? 'Dinheiro' : 'Banco';
    this.pagamento.contaId = this.contasAtivas().find((c) => c.tipo === tipo)?.id ?? this.pagamento.contaId;
  }

  abrirRecebimento(p: Pedido): void {
    this.carregarContas.set(true);
    const venc = new Date(Date.now() + 7 * 86_400_000).toLocaleDateString('sv-SE'); // boleto: 7 dias (o mais comum); dá para mudar
    this.pagamento = { modo: 'Boleto', vencimento: venc, documento: '', forma: 'Dinheiro', contaId: null, valor: null };
    this.recebendo.set(p);
    this.conferencia = p.itens.map((i) => ({
      itemId: i.id, produto: i.produto, unidade: i.unidade, perecivel: i.perecivel,
      pedido: i.quantidade, recebido: i.quantidade, validade: '', custo: i.custoUnitario,
    }));
    this.erroReceber.set(null);
    this.dlgReceber()?.nativeElement.showModal();
  }

  async confirmarRecebimento(): Promise<void> {
    const p = this.recebendo();
    if (!p) return;
    const semValidade = this.conferencia.find((c) => c.perecivel && c.recebido > 0 && !c.validade);
    if (semValidade) return this.erroReceber.set(`Informe a validade de "${semValidade.produto}".`);
    const pg = this.pagamento;
    if (pg.modo === 'Boleto' && !pg.vencimento) return this.erroReceber.set('Informe o vencimento do boleto.');
    if (pg.modo === 'NaEntrega' && !pg.contaId) {
      this.escolherForma(pg.forma);
      if (!pg.contaId) return this.erroReceber.set('Escolha de qual conta saiu o dinheiro.');
    }

    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post(`/api/compras/pedidos/${p.id}/receber`, {
        itens: this.conferencia.map((c) => ({ itemId: c.itemId, quantidadeRecebida: c.recebido, validade: c.validade || null, custoUnitario: c.custo })),
        pagamento: {
          modo: pg.modo, vencimento: pg.modo === 'Boleto' ? pg.vencimento : null, valor: pg.valor || null,
          documento: pg.documento.trim() || null, contaFinanceiraId: pg.modo === 'NaEntrega' ? pg.contaId : null,
          formaPagamento: pg.modo === 'NaEntrega' ? pg.forma : 'Boleto',
        },
      }));
      this.dlgReceber()?.nativeElement.close();
      this.mostrarAviso(`Pedido #${p.id} recebido: a mercadoria entrou no estoque` + ({
        Boleto: ` e o boleto de ${this.reais(pg.valor || this.totalRecebido())} entrou em Contas a pagar (vence ${this.dia(pg.vencimento)}).`,
        NaEntrega: ` e o pagamento de ${this.reais(pg.valor || this.totalRecebido())} (${pg.forma}) saiu do Financeiro.`,
        Nota: '. A conta entra quando você lançar a NF-e deste pedido (Compras → Entrada por NF-e).',
      })[pg.modo]);
      this.pedidos.reload();
    } catch (e) {
      this.erroReceber.set(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  private reais(v: number): string {
    return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }

  protected faltou(c: Conferencia): boolean {
    return c.recebido < c.pedido;
  }

  // ---------------------------------------------------------------- utilidades
  protected dia(iso: string | null): string {
    return iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : '—';
  }

  private async executar(acao: () => Promise<void>): Promise<void> {
    this.ocupado.set(true);
    try {
      await acao();
    } catch (e) {
      alert(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  private mostrarAviso(texto: string): void {
    this.aviso.set(texto);
    setTimeout(() => this.aviso.set(null), 6000);
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      if (e.error?.errors) return Object.values<string[]>(e.error.errors).flat().join(' ');
    }
    return 'Não foi possível concluir.';
  }
}

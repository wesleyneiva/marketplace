import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { Categoria, UNIDADES } from '../../core/api/produtos.api';
import { GRADES_PRONTAS, ModeloDetalhe, ModelosApi, Variacao } from '../../core/api/modelos.api';
import { Icone } from '../../shared/icone';
import { Confirmacao } from '../../shared/confirmacao';

// Um modelo com grade: /produtos/modelos/novo (cadastra a matriz tamanho × cor, já com o estoque inicial)
// e /produtos/modelos/:id (matriz do estoque, entrada em grade, acrescentar tamanho/cor, editar).
@Component({
  selector: 'app-modelo',
  imports: [Icone, FormsModule, RouterLink, CurrencyPipe, DecimalPipe],
  templateUrl: './modelo.html',
  styleUrl: './modelo.scss',
})
export class ModeloPagina {
  private readonly confirmacao = inject(Confirmacao);
  private readonly api = inject(ModelosApi);
  private readonly router = inject(Router);

  readonly id = input<string>(); // da rota: "novo" ou o número
  protected readonly novo = computed(() => !this.id() || this.id() === 'novo');

  protected readonly categorias = httpResource<Categoria[]>(() => '/api/categorias');
  protected readonly fornecedores = httpResource<{ id: number; nome: string; ativo: boolean }[]>(() => '/api/fornecedores');
  protected readonly unidades = UNIDADES.filter((u) => ['UN', 'PAR', 'CX', 'PCT'].includes(u.sigla));
  protected readonly gradesProntas = GRADES_PRONTAS;

  protected readonly modelo = signal<ModeloDetalhe | null>(null);
  protected readonly carregando = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);

  // ----- Cadastro (novo) e edição -----
  protected form = this.formVazio();
  protected readonly tamanhos = signal<string[]>([]);
  protected readonly cores = signal<string[]>([]);
  protected novoTamanho = '';
  protected novaCor = '';
  // Matriz do cadastro: "tamanho|cor" → quantidade inicial (vazio = 0) e as combinações que a loja NÃO trabalha.
  protected inicial: Record<string, number | null> = {};
  protected readonly desligadas = signal<Set<string>>(new Set());

  // ----- Detalhe -----
  protected readonly editando = signal(false);
  protected aplicarPrecos = false;
  protected readonly modoEntrada = signal(false);
  protected entrada: Record<number, number | null> = {};
  protected custoEntrada: number | null = null;
  protected obsEntrada = '';
  protected acrescentarTamanho = '';
  protected acrescentarCor = '';

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => (id && id !== 'novo' ? void this.carregar(+id) : this.limpar()));
    });
  }

  private formVazio() {
    return {
      nome: '', marca: '', referencia: '', categoriaId: null as number | null, fornecedorId: null as number | null,
      unidade: 'UN', precoCusto: null as number | null, precoVenda: null as number | null, estoqueMinimo: 0, ncm: '',
    };
  }

  private limpar(): void {
    this.modelo.set(null);
    this.form = this.formVazio();
    this.tamanhos.set([]);
    this.cores.set([]);
    this.inicial = {};
    this.desligadas.set(new Set());
  }

  private async carregar(id: number): Promise<void> {
    this.carregando.set(true);
    this.erro.set(null);
    try {
      this.usar(await this.api.obter(id));
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.carregando.set(false);
    }
  }

  private usar(m: ModeloDetalhe): void {
    this.modelo.set(m);
    const v = m.variacoes[0];
    this.form = {
      nome: m.nome, marca: m.marca ?? '', referencia: m.referencia ?? '', categoriaId: m.categoriaId, fornecedorId: m.fornecedorId,
      unidade: m.unidade, precoCusto: v?.precoCusto ?? 0, precoVenda: v?.precoVenda ?? null, estoqueMinimo: v?.estoqueMinimo ?? 0, ncm: m.ncm ?? '',
    };
  }

  // ----- Grade (cadastro) -----
  protected usarGradePronta(indice: string): void {
    const g = this.gradesProntas[+indice];
    if (g) this.tamanhos.set([...g.tamanhos]);
  }

  protected adicionarTamanho(): void {
    for (const t of separar(this.novoTamanho)) if (!this.tamanhos().includes(t)) this.tamanhos.update((l) => [...l, t]);
    this.novoTamanho = '';
  }

  protected adicionarCor(): void {
    for (const c of separar(this.novaCor)) if (!this.cores().includes(c)) this.cores.update((l) => [...l, c]);
    this.novaCor = '';
  }

  protected tirarTamanho(t: string): void { this.tamanhos.update((l) => l.filter((x) => x !== t)); }
  protected tirarCor(c: string): void { this.cores.update((l) => l.filter((x) => x !== c)); }

  protected chave(t: string, c: string): string { return `${t}|${c}`; }

  protected alternarCombinacao(t: string, c: string): void {
    const k = this.chave(t, c);
    this.desligadas.update((s) => {
      const n = new Set(s);
      if (n.has(k)) n.delete(k); else n.add(k);
      return n;
    });
  }

  // Sem cor (só tamanho) = "Única"; sem tamanho (só cor) = "Único".
  private coresEfetivas(): string[] { return this.cores().length ? this.cores() : ['Única']; }
  private tamanhosEfetivos(): string[] { return this.tamanhos().length ? this.tamanhos() : ['Único']; }
  protected readonly colunas = computed(() => (this.cores().length ? this.cores() : ['Única']));
  protected readonly linhas = computed(() => (this.tamanhos().length ? this.tamanhos() : ['Único']));
  protected readonly totalVariacoes = computed(() => this.linhas().length * this.colunas().length - this.desligadas().size);

  protected async criar(): Promise<void> {
    this.erro.set(null);
    if (!this.form.nome.trim() || !this.form.categoriaId || !this.form.precoVenda) {
      this.erro.set('Preencha nome, categoria e preço de venda.');
      return;
    }
    if (this.tamanhos().length === 0 && this.cores().length === 0) {
      this.erro.set('Informe os tamanhos e/ou as cores da grade.');
      return;
    }
    const combinacoes = this.tamanhosEfetivos().flatMap((t) => this.coresEfetivas().map((c) => ({ tamanho: t, cor: c })))
      .filter((x) => !this.desligadas().has(this.chave(x.tamanho, x.cor)));
    if (combinacoes.length === 0) {
      this.erro.set('Todas as combinações estão desligadas.');
      return;
    }
    this.salvando.set(true);
    try {
      let m = await this.api.criar({
        nome: this.form.nome.trim(), marca: vazio(this.form.marca), referencia: vazio(this.form.referencia),
        categoriaId: this.form.categoriaId, fornecedorId: this.form.fornecedorId, unidade: this.form.unidade,
        precoCusto: this.form.precoCusto ?? 0, precoVenda: this.form.precoVenda, estoqueMinimo: this.form.estoqueMinimo ?? 0,
        ncm: vazio(this.form.ncm), tamanhos: this.tamanhosEfetivos(), cores: this.coresEfetivas(), combinacoes,
      });
      const itens = m.variacoes
        .map((v) => ({ produtoId: v.produtoId, quantidade: this.inicial[this.chave(v.tamanho, v.cor)] ?? 0 }))
        .filter((i) => i.quantidade > 0);
      if (itens.length) m = await this.api.entrada(m.id, itens, this.form.precoCusto || null, 'Estoque inicial (cadastro do modelo)');
      await this.router.navigate(['/produtos/modelos', m.id]);
      this.aviso.set(`Modelo cadastrado com ${m.variacoes.length} variação(ões).`);
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  // ----- Detalhe: matriz -----
  protected celula(t: string, c: string): Variacao | undefined {
    return this.modelo()?.variacoes.find((v) => v.tamanho === t && v.cor === c);
  }

  protected readonly precoUnico = computed(() => {
    const vs = this.modelo()?.variacoes.filter((v) => v.ativo) ?? [];
    return vs.length > 0 && vs.every((v) => v.precoVenda === vs[0].precoVenda) ? vs[0].precoVenda : null;
  });

  protected readonly estoqueTotal = computed(() =>
    (this.modelo()?.variacoes ?? []).filter((v) => v.ativo).reduce((s, v) => s + v.estoqueAtual, 0));

  protected totalLinha(t: string): number {
    return (this.modelo()?.variacoes ?? []).filter((v) => v.ativo && v.tamanho === t).reduce((s, v) => s + v.estoqueAtual, 0);
  }

  protected readonly idsEtiquetas = computed(() => (this.modelo()?.variacoes ?? []).filter((v) => v.ativo).map((v) => v.produtoId).join(','));

  protected async salvarEdicao(): Promise<void> {
    const m = this.modelo();
    if (!m || !this.form.categoriaId) return;
    this.salvando.set(true);
    this.erro.set(null);
    try {
      this.usar(await this.api.editar(m.id, {
        nome: this.form.nome.trim(), marca: vazio(this.form.marca), referencia: vazio(this.form.referencia),
        categoriaId: this.form.categoriaId, fornecedorId: this.form.fornecedorId, ncm: vazio(this.form.ncm),
        aplicarPrecos: this.aplicarPrecos, precoCusto: this.form.precoCusto ?? 0, precoVenda: this.form.precoVenda ?? 0.01,
        estoqueMinimo: this.form.estoqueMinimo ?? 0,
      }));
      this.editando.set(false);
      this.aplicarPrecos = false;
      this.aviso.set('Modelo atualizado (vale para todas as variações).');
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  protected abrirEntrada(): void {
    this.entrada = {};
    this.custoEntrada = null;
    this.obsEntrada = '';
    this.modoEntrada.set(true);
  }

  protected async confirmarEntrada(): Promise<void> {
    const m = this.modelo();
    if (!m) return;
    const itens = Object.entries(this.entrada).filter(([, q]) => (q ?? 0) > 0).map(([id, q]) => ({ produtoId: +id, quantidade: q! }));
    if (!itens.length) {
      this.erro.set('Digite a quantidade que chegou em pelo menos um tamanho/cor.');
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);
    try {
      this.usar(await this.api.entrada(m.id, itens, this.custoEntrada || null, vazio(this.obsEntrada)));
      this.modoEntrada.set(false);
      this.aviso.set(`Entrada registrada: ${itens.reduce((s, i) => s + i.quantidade, 0)} peça(s).`);
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  // Acrescenta um tamanho (linha inteira, em todas as cores) ou uma cor (coluna inteira).
  protected async acrescentar(tipo: 'tamanho' | 'cor'): Promise<void> {
    const m = this.modelo();
    if (!m) return;
    const valores = separar(tipo === 'tamanho' ? this.acrescentarTamanho : this.acrescentarCor);
    if (!valores.length) return;
    const combinacoes = tipo === 'tamanho'
      ? valores.flatMap((t) => m.cores.map((c) => ({ tamanho: t, cor: c })))
      : valores.flatMap((c) => m.tamanhos.map((t) => ({ tamanho: t, cor: c })));
    this.salvando.set(true);
    this.erro.set(null);
    try {
      this.usar(await this.api.acrescentar(m.id, combinacoes));
      this.acrescentarTamanho = '';
      this.acrescentarCor = '';
      this.aviso.set(`${tipo === 'tamanho' ? 'Tamanho' : 'Cor'} acrescentado(a).`);
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  // Combinação que a loja não tinha (buraco na matriz): cria só ela.
  protected async criarCelula(t: string, c: string): Promise<void> {
    const m = this.modelo();
    if (!m) return;
    this.salvando.set(true);
    try {
      this.usar(await this.api.acrescentar(m.id, [{ tamanho: t, cor: c }]));
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  protected async alternarAtivo(): Promise<void> {
    const m = this.modelo();
    if (!m) return;
    if (m.ativo && !(await this.confirmacao.pedir(`Desativar "${m.nome}" e todas as variações?\nElas deixam de aparecer nas vendas.`, { botao: 'Desativar', perigo: true }))) return;
    try {
      await (m.ativo ? this.api.desativar(m.id) : this.api.reativar(m.id));
      await this.carregar(m.id);
    } catch (e) {
      this.erro.set(mensagem(e));
    }
  }
}

// "38, 39 40" → ["38", "39", "40"] (vírgula ou ponto e vírgula; espaço também separa tamanhos curtos).
function separar(texto: string): string[] {
  return texto.split(/[,;]+/).map((t) => t.trim()).filter(Boolean)
    .flatMap((t) => (/^[\w.]+(\s+[\w.]+)+$/.test(t) && t.split(/\s+/).every((p) => p.length <= 3) ? t.split(/\s+/) : [t]));
}

function vazio(t: string | null | undefined): string | null {
  return t && t.trim() ? t.trim() : null;
}

function mensagem(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.error?.mensagem) return e.error.mensagem;
    if (e.error?.errors) return Object.values(e.error.errors as Record<string, string[]>).flat().join(' ');
    if (e.status === 0) return 'Sem conexão com o servidor.';
  }
  return 'Não foi possível concluir. Tente de novo.';
}

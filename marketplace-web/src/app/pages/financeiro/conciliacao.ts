import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import { Icone } from '../../shared/icone';
import { CategoriaFinanceira, URL_FINANCEIRO, mensagemDeErro } from '../../core/api/financeiro.api';
import { Conciliacao, ImportacaoExtrato, LinhaExtrato, MovimentoResumo, URL_CONCILIACAO, comSinal } from '../../core/api/conciliacao.api';

// O que está aberto embaixo de uma linha pendente: lançar no sistema ou escolher o lançamento na mão.
type Painel = { id: number; modo: 'lancar' | 'escolher' };

// Aba "Conciliação": importa o extrato do banco (OFX) e confere linha a linha com o que está no sistema.
@Component({
  selector: 'app-conciliacao',
  imports: [Icone, CurrencyPipe, FormsModule, DiaPipe],
  templateUrl: './conciliacao.html',
  styleUrls: ['./financeiro.scss', './lancamentos.scss', './conciliacao.scss'],
})
export class ConciliacaoAba {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly contaInicial = input<number | null>(null);
  protected readonly contaId = signal<number | null>(null);
  protected readonly comSinal = comSinal;

  protected readonly dados = httpResource<Conciliacao>(() => {
    const params: Record<string, string> = {};
    if (this.contaId()) params['contaId'] = String(this.contaId());
    return { url: URL_CONCILIACAO, params };
  });
  protected readonly categorias = httpResource<CategoriaFinanceira[]>(() => `${URL_FINANCEIRO}/categorias`);

  protected readonly conta = computed(() => {
    const d = this.dados.value();
    return d?.contas.find((c) => c.id === d.contaId) ?? null;
  });
  protected readonly sugeridas = computed(() => new Set((this.dados.value()?.pendentes ?? []).filter((l) => l.sugestao).map((l) => l.id)));
  // Linhas agrupadas (vários PIX = 1 lançamento) aparecem uma vez só na contagem de sugestões.
  protected readonly qtdSugestoes = computed(() => {
    const vistas = new Set<string>();
    for (const l of this.dados.value()?.pendentes ?? []) if (l.sugestao) vistas.add(l.sugestao.movimentoIds.join(','));
    return vistas.size;
  });
  protected readonly diferenca = computed(() => {
    const d = this.dados.value();
    return d?.saldoBanco != null && d.saldoSistema != null ? Math.round((d.saldoBanco - d.saldoSistema) * 100) / 100 : null;
  });

  // ----- importar -----
  protected readonly enviando = signal(false);
  protected readonly arrastando = signal(false);
  protected readonly importou = signal<ImportacaoExtrato | null>(null);
  protected readonly erroImportar = signal<string | null>(null);
  protected readonly pedirConta = signal(false);
  private arquivoPendente: File | null = null;

  // ----- ações nas linhas -----
  protected readonly painel = signal<Painel | null>(null);
  protected readonly categoriaId = signal<number | null>(null);
  protected readonly descricao = signal('');
  protected readonly candidatos = signal<MovimentoResumo[] | null>(null);
  protected readonly escolhidos = signal<Set<number>>(new Set());
  protected readonly ocupado = signal<number | null>(null);
  protected readonly erroLinha = signal<{ id: number; texto: string } | null>(null);
  protected readonly retorno = signal<string | null>(null);
  protected readonly retornoErro = signal<string | null>(null);
  protected readonly aceitando = signal(false);
  protected readonly verIgnoradas = signal(false);
  protected readonly verTodosConferidos = signal(false);

  constructor() {
    effect(() => {
      const c = this.contaInicial();
      untracked(() => this.contaId.set(c ?? null));
    });
  }

  protected mudouConta(id: number | null): void {
    this.contaId.set(id);
    this.limparRetornos();
    this.router.navigate([], { queryParams: { aba: 'conciliacao', conta: id ?? null }, replaceUrl: true });
  }

  // ================================================================ importar o .ofx

  protected escolherArquivo(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    const arquivo = campo.files?.[0];
    campo.value = '';
    if (arquivo) this.importar(arquivo);
  }

  protected arrastarPorCima(e: DragEvent): void { e.preventDefault(); this.arrastando.set(true); }

  protected soltarArquivo(e: DragEvent): void {
    e.preventDefault();
    this.arrastando.set(false);
    const arquivo = e.dataTransfer?.files?.[0];
    if (arquivo) this.importar(arquivo);
  }

  // Quando o arquivo não diz a conta (ou há várias), o cliente escolhe e o mesmo arquivo vai de novo.
  protected importarNaConta(): void {
    if (this.arquivoPendente && this.contaId()) this.importar(this.arquivoPendente, this.contaId());
  }

  private async importar(arquivo: File, conta: number | null = this.contaId()): Promise<void> {
    this.limparRetornos();
    this.enviando.set(true);
    const dados = new FormData();
    dados.append('arquivo', arquivo);
    if (conta) dados.append('contaId', String(conta));
    try {
      const r = await firstValueFrom(this.http.post<ImportacaoExtrato>(`${URL_CONCILIACAO}/importar`, dados));
      this.importou.set(r);
      this.pedirConta.set(false);
      this.arquivoPendente = null;
      if (r.contaId !== this.contaId()) this.mudouConta(r.contaId);
      this.retorno.set(null);
      this.dados.reload();
    } catch (e) {
      const corpo = e instanceof HttpErrorResponse ? e.error : null;
      if (corpo?.escolherConta) { this.pedirConta.set(true); this.arquivoPendente = arquivo; }
      this.erroImportar.set(mensagemDeErro(e));
    } finally {
      this.enviando.set(false);
    }
  }

  // ================================================================ conferir

  protected async aceitarSugestoes(): Promise<void> {
    const conta = this.dados.value()?.contaId;
    if (!conta) return;
    this.limparRetornos();
    this.aceitando.set(true);
    try {
      const r = await firstValueFrom(this.http.post<{ conferidas: number }>(`${URL_CONCILIACAO}/aceitar-sugestoes`, { contaId: conta }));
      this.retorno.set(r.conferidas === 1 ? '1 lançamento conferido.' : `${r.conferidas} lançamentos conferidos.`);
      this.dados.reload();
    } catch (e) {
      this.retornoErro.set(mensagemDeErro(e));
    } finally {
      this.aceitando.set(false);
    }
  }

  protected async confirmar(l: LinhaExtrato): Promise<void> {
    if (!l.sugestao) return;
    await this.acao(l.id, () => firstValueFrom(this.http.post(`${URL_CONCILIACAO}/conciliar`,
      { linhaIds: l.sugestao!.linhaIds, movimentoIds: l.sugestao!.movimentoIds })));
  }

  protected abrir(l: LinhaExtrato, modo: Painel['modo']): void {
    this.erroLinha.set(null);
    if (this.painel()?.id === l.id && this.painel()?.modo === modo) { this.painel.set(null); return; }
    this.painel.set({ id: l.id, modo });
    if (modo === 'lancar') {
      this.categoriaId.set(l.categoriaSugeridaId);
      this.descricao.set(l.descricao);
    } else {
      this.candidatos.set(null);
      this.escolhidos.set(new Set());
      firstValueFrom(this.http.get<MovimentoResumo[]>(`${URL_CONCILIACAO}/linhas/${l.id}/candidatos`))
        .then((c) => this.candidatos.set(c))
        .catch((e) => this.erroLinha.set({ id: l.id, texto: mensagemDeErro(e) }));
    }
  }

  // Categorias da natureza certa: o que entrou no banco é receita; o que saiu, despesa. Vendas do caixa ficam de fora.
  protected categoriasDa(l: LinhaExtrato): CategoriaFinanceira[] {
    const natureza = l.valor > 0 ? 'Entrada' : 'Saida';
    return (this.categorias.value() ?? []).filter((c) => c.ativa && c.natureza === natureza && c.grupo !== 'ReceitaVendas');
  }

  protected async lancar(l: LinhaExtrato): Promise<void> {
    if (!this.categoriaId()) { this.erroLinha.set({ id: l.id, texto: 'Escolha a categoria.' }); return; }
    await this.acao(l.id, () => firstValueFrom(this.http.post(`${URL_CONCILIACAO}/linhas/${l.id}/lancar`,
      { categoriaId: this.categoriaId(), descricao: this.descricao() })), 'Lançado no sistema e conferido.');
  }

  protected alternarEscolhido(id: number): void {
    const s = new Set(this.escolhidos());
    if (s.has(id)) s.delete(id); else s.add(id);
    this.escolhidos.set(s);
  }

  protected somaEscolhidos(): number {
    return (this.candidatos() ?? []).filter((m) => this.escolhidos().has(m.id)).reduce((s, m) => s + comSinal(m), 0);
  }

  protected soma(linhas: LinhaExtrato[]): number {
    return linhas.reduce((s, l) => s + l.valor, 0);
  }

  protected bate(l: LinhaExtrato): boolean {
    return this.escolhidos().size > 0 && Math.abs(this.somaEscolhidos() - l.valor) < 0.005;
  }

  protected async conciliarEscolhidos(l: LinhaExtrato): Promise<void> {
    await this.acao(l.id, () => firstValueFrom(this.http.post(`${URL_CONCILIACAO}/conciliar`,
      { linhaIds: [l.id], movimentoIds: [...this.escolhidos()] })));
  }

  protected async ignorar(l: LinhaExtrato, valor = true): Promise<void> {
    await this.acao(l.id, () => firstValueFrom(this.http.post(`${URL_CONCILIACAO}/linhas/${l.id}/ignorar`, null, { params: { valor } })),
      valor ? `"${l.descricao}" ignorada.` : `"${l.descricao}" voltou para as pendentes.`);
  }

  protected async desfazer(id: string): Promise<void> {
    this.limparRetornos();
    try {
      await firstValueFrom(this.http.post(`${URL_CONCILIACAO}/${id}/desfazer`, null));
      this.retorno.set('Conferência desfeita: a linha voltou para as pendentes.');
      this.dados.reload();
    } catch (e) {
      this.retornoErro.set(mensagemDeErro(e));
    }
  }

  private async acao(id: number, fazer: () => Promise<unknown>, texto?: string): Promise<void> {
    this.erroLinha.set(null);
    this.ocupado.set(id);
    try {
      await fazer();
      this.painel.set(null);
      this.retorno.set(texto ?? 'Conferido.');
      this.retornoErro.set(null);
      this.dados.reload();
    } catch (e) {
      this.erroLinha.set({ id, texto: mensagemDeErro(e) });
    } finally {
      this.ocupado.set(null);
    }
  }

  private limparRetornos(): void {
    this.importou.set(null);
    this.erroImportar.set(null);
    this.retorno.set(null);
    this.retornoErro.set(null);
    this.erroLinha.set(null);
  }
}

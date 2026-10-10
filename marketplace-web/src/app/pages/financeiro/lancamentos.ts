import { Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import {
  CategoriaFinanceira, ContaFinanceira, Extrato, ICONE_CONTA, Movimento, NOME_ORIGEM, OrigemMovimento, TipoMovimento,
  URL_FINANCEIRO, fimDoMes, hojeIso, inicioDoMes, mensagemDeErro,
} from '../../core/api/financeiro.api';
import { DialogoLancamento } from './dialogo-lancamento';
import { DialogoTransferencia } from './dialogo-transferencia';

type Periodo = 'mes' | 'mesPassado' | '30dias' | 'outro';
// Uma linha da tabela: um movimento ou a linha do saldo de implantação.
type Linha = { tipo: 'mov'; mov: Movimento } | { tipo: 'implantacao'; data: string; valor: number };

// Aba "Lançamentos": o livro (extrato). Com uma conta escolhida, mostra o saldo linha a linha, como o extrato do banco.
@Component({
  selector: 'app-lancamentos',
  imports: [CurrencyPipe, FormsModule, DiaPipe, RouterLink, DialogoLancamento, DialogoTransferencia],
  templateUrl: './lancamentos.html',
  styleUrls: ['./financeiro.scss', './lancamentos.scss'],
})
export class Lancamentos {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly contaInicial = input<number | null>(null);

  protected readonly icone = ICONE_CONTA;
  protected readonly nomeOrigem = NOME_ORIGEM;
  protected readonly origens = Object.keys(NOME_ORIGEM) as OrigemMovimento[];
  private readonly hoje = hojeIso();

  // ----- Filtros -----
  protected readonly periodo = signal<Periodo>('mes');
  protected readonly de = signal(inicioDoMes(this.hoje));
  protected readonly ate = signal(this.hoje);
  protected readonly contaId = signal<number | null>(null);
  protected readonly categoriaId = signal<number | null>(null);
  protected readonly origem = signal<OrigemMovimento | null>(null);
  protected readonly busca = signal('');        // o que está no campo
  private readonly buscaAplicada = signal('');  // o que vai para a API (0,4 s depois de parar de digitar)
  private espera?: ReturnType<typeof setTimeout>;

  protected readonly contas = httpResource<ContaFinanceira[]>(() => ({ url: `${URL_FINANCEIRO}/contas`, params: { incluirInativas: true } }));
  protected readonly categorias = httpResource<CategoriaFinanceira[]>(() => ({ url: `${URL_FINANCEIRO}/categorias`, params: { incluirInativas: true } }));
  protected readonly extrato = httpResource<Extrato>(() => {
    const params: Record<string, string> = { de: this.de(), ate: this.ate() };
    if (this.contaId()) params['contaId'] = String(this.contaId());
    if (this.categoriaId()) params['categoriaId'] = String(this.categoriaId());
    if (this.origem()) params['origem'] = this.origem()!;
    if (this.buscaAplicada()) params['busca'] = this.buscaAplicada();
    return { url: `${URL_FINANCEIRO}/movimentos`, params };
  });

  protected readonly contaEscolhida = computed(() => (this.contas.value() ?? []).find((c) => c.id === this.contaId()) ?? null);
  // Saldo linha a linha só vem quando há uma conta e nenhum outro filtro (a API decide e manda saldoAnterior).
  protected readonly comSaldo = computed(() => this.extrato.value()?.saldoAnterior != null);
  protected readonly filtrando = computed(() => !!(this.categoriaId() || this.origem() || this.buscaAplicada()));

  // Movimentos + a linha de implantação no lugar certo (pela data).
  protected readonly linhas = computed<Linha[]>(() => {
    const e = this.extrato.value();
    if (!e) return [];
    const linhas: Linha[] = e.movimentos.map((mov) => ({ tipo: 'mov', mov }));
    if (e.implantacao) {
      const i = linhas.findIndex((l) => l.tipo === 'mov' && l.mov.data >= e.implantacao!.data);
      linhas.splice(i < 0 ? linhas.length : i, 0, { tipo: 'implantacao', ...e.implantacao });
    }
    return linhas;
  });

  // Retornos junto da ação.
  protected readonly retorno = signal<string | null>(null);
  protected readonly erroLinha = signal<{ id: number; texto: string } | null>(null);
  protected readonly retornoLinha = signal<{ id: number; texto: string } | null>(null);

  private readonly dlgLancamento = viewChild.required(DialogoLancamento);
  private readonly dlgTransferencia = viewChild.required(DialogoTransferencia);

  constructor() {
    // A conta pode vir do endereço (clicou num cartão da aba Saldos).
    effect(() => {
      const c = this.contaInicial();
      untracked(() => this.contaId.set(c ?? null));
    });
  }

  protected escolherPeriodo(p: Periodo): void {
    this.periodo.set(p);
    if (p === 'mes') { this.de.set(inicioDoMes(this.hoje)); this.ate.set(this.hoje); }
    if (p === 'mesPassado') { this.de.set(inicioDoMes(this.hoje, -1)); this.ate.set(fimDoMes(this.hoje, -1)); }
    if (p === '30dias') {
      const d = new Date(); d.setDate(d.getDate() - 29);
      this.de.set(d.toLocaleDateString('sv-SE')); this.ate.set(this.hoje);
    }
  }

  protected mudouData(campo: 'de' | 'ate', valor: string): void {
    if (!valor) return;
    this.periodo.set('outro');
    (campo === 'de' ? this.de : this.ate).set(valor);
  }

  // A conta escolhida também vai para o endereço (o "voltar" do navegador e o favorito lembram).
  protected mudouConta(id: number | null): void {
    this.contaId.set(id);
    this.router.navigate([], { queryParams: { aba: 'lancamentos', conta: id ?? null }, replaceUrl: true });
  }

  protected digitouBusca(texto: string): void {
    this.busca.set(texto);
    clearTimeout(this.espera);
    this.espera = setTimeout(() => this.buscaAplicada.set(texto.trim()), 400);
  }

  protected limparFiltros(): void {
    this.categoriaId.set(null);
    this.origem.set(null);
    this.busca.set('');
    this.buscaAplicada.set('');
  }

  protected novo(tipo: TipoMovimento): void {
    this.retorno.set(null);
    this.dlgLancamento().abrir(tipo, undefined, this.contaId());
  }

  protected editar(m: Movimento): void {
    this.retorno.set(null);
    this.erroLinha.set(null);
    this.retornoLinha.set(null);
    this.dlgLancamento().abrir(m.tipo, m);
  }

  protected transferir(): void {
    this.retorno.set(null);
    this.dlgTransferencia().abrir(this.contaId() ?? undefined);
  }

  protected salvou(r: { id: number; texto: string; editou: boolean }): void {
    if (r.editou) this.retornoLinha.set({ id: r.id, texto: r.texto });
    else this.retorno.set(r.texto);
    this.recarregar();
  }

  protected transferiu(texto: string): void {
    this.retorno.set(texto);
    this.recarregar();
  }

  protected async excluir(m: Movimento): Promise<void> {
    const valor = m.valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    const pergunta = m.origem === 'Transferencia'
      ? `Excluir a transferência de ${valor}? As duas pontas (saída e entrada) saem do livro.`
      : `Excluir "${m.descricao}" (${valor})?`;
    if (!confirm(pergunta)) return;
    this.erroLinha.set(null);
    try {
      await firstValueFrom(this.http.delete(`${URL_FINANCEIRO}/movimentos/${m.id}`));
      this.retorno.set(m.origem === 'Transferencia' ? 'Transferência excluída.' : `"${m.descricao}" excluído.`);
      this.recarregar();
    } catch (e) {
      this.erroLinha.set({ id: m.id, texto: mensagemDeErro(e) });
    }
  }

  private recarregar(): void {
    this.extrato.reload();
    this.contas.reload();
    this.categorias.reload();
  }
}

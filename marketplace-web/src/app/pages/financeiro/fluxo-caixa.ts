import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import { GraficoSaldo, PontoSaldo } from '../../shared/graficos/grafico-saldo';
import { GraficoEntradasSaidas, PontoEntradaSaida } from '../../shared/graficos/grafico-entradas-saidas';
import {
  FluxoPrevisto, FluxoRealizado, URL_FINANCEIRO, diaCurto, hojeIso, mensagemDeErro,
} from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';
import { MODULOS } from '../../core/modulos';

type Modo = 'previsto' | 'realizado';
type PeriodoRealizado = '30d' | '90d' | '6m' | '12m';

const URL_FLUXO = `${URL_FINANCEIRO}/fluxo`;
const BRL = (v: number) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

// Aba "Fluxo de caixa": "vou ter dinheiro para pagar o fornecedor dia 20?"
// Previsto = saldo de hoje + maquininha + vendas estimadas − contas a pagar; Realizado = o que de fato entrou e saiu.
@Component({
  selector: 'app-fluxo-caixa',
  imports: [Icone, CurrencyPipe, DecimalPipe, FormsModule, DiaPipe, RouterLink, GraficoSaldo, GraficoEntradasSaidas],
  templateUrl: './fluxo-caixa.html',
  styleUrls: ['./financeiro.scss', './fluxo-caixa.scss'],
})
export class FluxoCaixa {
  protected readonly comFiado = MODULOS.fiado;
  private readonly http = inject(HttpClient);
  protected readonly diaCurto = diaCurto;

  protected readonly modo = signal<Modo>('previsto');

  // ----- Previsto -----
  protected readonly dias = signal(30);
  protected readonly comEstimativa = signal(true);
  protected readonly previsto = httpResource<FluxoPrevisto>(() => this.modo() !== 'previsto' ? undefined : ({
    url: `${URL_FLUXO}/previsto`, params: { dias: this.dias(), estimativa: this.comEstimativa() },
  }));
  protected readonly aberto = signal<string | null>(null); // dia com o detalhe aberto na tabela
  protected readonly soDiasComMovimento = signal(false);

  protected readonly pontosSaldo = computed<PontoSaldo[]>(() => (this.previsto.value()?.diasPrevistos ?? []).map((d) => ({
    rotulo: diaCurto(d.data), valor: d.saldoFinal,
    detalhes: [`Entra ${BRL(d.recebiveis + d.estimado)} · sai ${BRL(d.saidas)}`],
  })));
  protected readonly pontosFluxo = computed<PontoEntradaSaida[]>(() => (this.previsto.value()?.diasPrevistos ?? []).map((d) => ({
    rotulo: diaCurto(d.data), entradas: d.recebiveis, entradasEstimadas: d.estimado, saidas: d.saidas,
  })));
  protected readonly linhasPrevisto = computed(() => (this.previsto.value()?.diasPrevistos ?? [])
    .filter((d) => !this.soDiasComMovimento() || d.saidas > 0 || d.recebiveis > 0 || d.data === this.aberto()));

  // ----- Realizado -----
  protected readonly periodo = signal<PeriodoRealizado>('30d');
  protected readonly agrupar = signal<'dia' | 'semana' | 'mes'>('dia');
  protected readonly realizado = httpResource<FluxoRealizado>(() => {
    if (this.modo() !== 'realizado') return undefined;
    const ate = hojeIso();
    const d = new Date();
    if (this.periodo() === '30d') d.setDate(d.getDate() - 29);
    if (this.periodo() === '90d') d.setDate(d.getDate() - 89);
    if (this.periodo() === '6m') d.setMonth(d.getMonth() - 6);
    if (this.periodo() === '12m') d.setMonth(d.getMonth() - 12);
    return { url: `${URL_FLUXO}/realizado`, params: { de: d.toLocaleDateString('sv-SE'), ate, agrupar: this.agrupar() } };
  });
  protected readonly pontosSaldoReal = computed<PontoSaldo[]>(() => (this.realizado.value()?.periodos ?? []).map((p) => ({
    rotulo: this.agrupar() === 'dia' ? diaCurto(p.inicio) : p.rotulo, valor: p.saldoFinal,
    detalhes: [`Entrou ${BRL(p.entradas)} · saiu ${BRL(p.saidas)}`, ...(p.implantacao ? [`Saldo de implantação: ${BRL(p.implantacao)}`] : [])],
  })));
  protected readonly pontosFluxoReal = computed<PontoEntradaSaida[]>(() => (this.realizado.value()?.periodos ?? []).map((p) => ({
    rotulo: this.agrupar() === 'dia' ? diaCurto(p.inicio) : p.rotulo, entradas: p.entradas, saidas: p.saidas,
  })));

  // ----- Saldo mínimo de segurança (editado aqui mesmo) -----
  protected editandoMinimo = signal(false);
  protected novoMinimo: number | null = null;
  protected readonly retornoMinimo = signal<{ texto: string; ruim?: boolean } | null>(null);

  protected escolherPeriodoAgrupado(p: PeriodoRealizado): void {
    this.periodo.set(p);
    // Período longo por dia vira uma "parede" de colunas: agrupa sozinho (a pessoa pode trocar).
    if (p === '6m' && this.agrupar() === 'dia') this.agrupar.set('semana');
    if (p === '12m' && this.agrupar() !== 'mes') this.agrupar.set('mes');
  }

  protected abrirDia(data: string): void {
    this.aberto.set(this.aberto() === data ? null : data);
  }

  // Do alerta para o dia: abre o detalhe e rola até ele.
  protected irParaDia(data: string): void {
    this.soDiasComMovimento.set(false);
    this.aberto.set(data);
    setTimeout(() => document.getElementById('dia-' + data)?.scrollIntoView({ behavior: 'smooth', block: 'center' }));
  }

  protected editarMinimo(atual: number): void {
    this.novoMinimo = atual;
    this.retornoMinimo.set(null);
    this.editandoMinimo.set(true);
  }

  protected async salvarMinimo(): Promise<void> {
    try {
      await firstValueFrom(this.http.put(`${URL_FLUXO}/saldo-minimo`, { valor: this.novoMinimo ?? 0 }));
      this.editandoMinimo.set(false);
      this.retornoMinimo.set({ texto: 'Salvo.' });
      this.previsto.reload();
    } catch (e) {
      this.retornoMinimo.set({ texto: mensagemDeErro(e), ruim: true });
    }
  }
}

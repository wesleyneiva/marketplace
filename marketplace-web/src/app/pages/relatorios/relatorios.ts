import { Component, computed, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RelatorioClima, RelatorioProdutos, RelatorioVendas } from '../../core/api/relatorios.api';
import { DiaPipe } from '../../shared/dia.pipe';
import { GraficoBarras, PontoBarra } from '../../shared/graficos/grafico-barras';
import { GraficoColunas, PontoColuna } from '../../shared/graficos/grafico-colunas';
import { GraficoFaixa, PontoFaixa } from '../../shared/graficos/grafico-faixa';
import { CelulaMapa, MapaCalor } from '../../shared/graficos/mapa-calor';
import { formatar } from '../../shared/graficos/largura';
import { QuantidadePipe } from '../../shared/quantidade.pipe';

const NOMES = ['Domingo', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado'];
const CURTOS = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];
const ORDEM_SEMANA = [1, 2, 3, 4, 5, 6, 0]; // segunda primeiro
const FORMAS: Record<string, string> = { Pix: 'Pix', Debito: 'Débito', Credito: 'Crédito', Dinheiro: 'Dinheiro' };

@Component({
  selector: 'app-relatorios',
  imports: [CurrencyPipe, DecimalPipe, DiaPipe, QuantidadePipe, GraficoColunas, GraficoFaixa, GraficoBarras, MapaCalor],
  templateUrl: './relatorios.html',
  styleUrl: './relatorios.scss',
})
export class Relatorios {
  protected readonly opcoesPeriodo = [7, 30, 60];
  protected readonly dias = signal(30);

  // Período em datas de Brasília ("sv-SE" formata como AAAA-MM-DD).
  private readonly hoje = new Date().toLocaleDateString('sv-SE');
  protected readonly periodo = computed(() => {
    const inicio = new Date();
    inicio.setDate(inicio.getDate() - (this.dias() - 1));
    return { de: inicio.toLocaleDateString('sv-SE'), ate: this.hoje };
  });

  // Os três relatórios: mudou o período → os três recarregam sozinhos.
  protected readonly vendas = httpResource<RelatorioVendas>(() => ({ url: '/api/relatorios/vendas', params: this.periodo() }));
  protected readonly produtos = httpResource<RelatorioProdutos>(() => ({ url: '/api/relatorios/produtos', params: this.periodo() }));
  protected readonly clima = httpResource<RelatorioClima>(() => ({ url: '/api/relatorios/clima', params: this.periodo() }));

  protected readonly carregando = computed(() => this.vendas.isLoading() || this.produtos.isLoading() || this.clima.isLoading());

  // ---------------------------------------------------------------- vendas por dia + clima
  protected readonly colunasDia = computed<PontoColuna[]>(() =>
    (this.vendas.value()?.porDia ?? []).map((d) => {
      const dia = this.dataLocal(d.data);
      return {
        rotulo: d.data.slice(8, 10) + '/' + d.data.slice(5, 7),
        valor: d.faturamento,
        apagado: d.data === this.hoje,
        detalhes: [
          NOMES[dia.getDay()] + (d.data === this.hoje ? ' (hoje, até agora)' : ''),
          `${d.vendas} vendas · lucro ${formatar(d.lucro, 'moeda')}`,
        ],
      };
    }),
  );

  protected readonly faixaTemperatura = computed<PontoFaixa[]>(() =>
    (this.vendas.value()?.porDia ?? []).map((d) => ({
      rotulo: `${d.data.slice(8, 10)}/${d.data.slice(5, 7)} · ${NOMES[this.dataLocal(d.data).getDay()]}`,
      minimo: d.tempMin,
      maximo: d.tempMax,
      detalhes: d.tempMax === null ? ['Sem dados de clima'] : [
        `Máx ${formatar(d.tempMax, 'numero')} °C · Mín ${formatar(d.tempMin ?? 0, 'numero')} °C`,
        d.chuva > 0 ? `Chuva: ${formatar(d.chuva, 'numero')} mm` : 'Sem chuva',
        `Faturamento: ${formatar(d.faturamento, 'moeda')}`,
      ],
    })),
  );

  // ---------------------------------------------------------------- semana e horários
  protected readonly barrasSemana = computed<PontoBarra[]>(() => {
    const lista = this.vendas.value()?.porDiaSemana ?? [];
    const maior = Math.max(...lista.map((d) => d.faturamento), 0);
    return ORDEM_SEMANA.map((n) => lista.find((d) => d.dia === n)).filter((d) => !!d).map((d) => ({
      rotulo: d!.nome, valor: d!.faturamento, extra: `${formatar(d!.vendas, 'numero')} vendas`, destaque: d!.faturamento === maior,
    }));
  });

  protected readonly mapa = computed(() => {
    const celulas = (this.vendas.value()?.mapaDeCalor ?? []).map<CelulaMapa>((c) => ({ linha: c.diaSemana, coluna: c.hora, valor: c.mediaVendas }));
    const horas = [...new Set(celulas.map((c) => c.coluna))].sort((a, b) => a - b);
    return {
      celulas,
      linhas: ORDEM_SEMANA.map((d) => ({ valor: d, texto: CURTOS[d] })),
      colunas: horas.map((h) => ({ valor: h, texto: `${h}h` })),
    };
  });
  protected readonly textoCelula = (c: CelulaMapa) => `${formatar(c.valor, 'numero')} clientes em média`;
  protected readonly tituloCelula = (c: CelulaMapa) => `${NOMES[c.linha]}, ${c.coluna}h às ${c.coluna + 1}h`;

  protected readonly horarioPico = computed(() => {
    const todas = this.mapa().celulas;
    if (!todas.length) return null;
    const pico = todas.reduce((a, b) => (b.valor > a.valor ? b : a));
    return `${NOMES[pico.linha]} às ${pico.coluna}h (${formatar(pico.valor, 'numero')} clientes em média)`;
  });

  protected readonly barrasForma = computed<PontoBarra[]>(() => {
    const formas = this.vendas.value()?.porForma ?? [];
    const total = formas.reduce((s, f) => s + f.valor, 0) || 1;
    return formas.map((f) => ({ rotulo: FORMAS[f.forma] ?? f.forma, valor: f.valor, extra: `${formatar((f.valor / total) * 100, 'numero')}%` }));
  });

  // ---------------------------------------------------------------- produtos
  protected readonly filtroClasse = signal<'A' | 'B' | 'C' | null>('A');
  protected readonly produtosFiltrados = computed(() =>
    (this.produtos.value()?.produtos ?? []).filter((p) => !this.filtroClasse() || p.classe === this.filtroClasse()),
  );

  protected readonly barrasCategoria = computed<PontoBarra[]>(() =>
    (this.produtos.value()?.categorias ?? []).map((c) => ({
      rotulo: c.categoria, valor: c.faturamento, extra: `margem ${formatar(c.margemPercentual, 'numero')}%`,
      detalhes: [`${formatar(c.participacao, 'numero')}% do faturamento`, `Lucro bruto: ${formatar(c.lucro, 'moeda')}`, `${c.produtos} produtos`],
    })),
  );

  // ---------------------------------------------------------------- clima
  protected readonly barrasTemperatura = computed<PontoBarra[]>(() =>
    (this.clima.value()?.porTemperatura ?? []).map((f) => ({
      rotulo: f.faixa, valor: f.clientesPorHora, extra: f.ticketMedio ? `ticket ${formatar(f.ticketMedio, 'moeda')}` : undefined,
      detalhes: [`${f.horas} horas com o mercado aberto`],
    })),
  );

  protected readonly barrasChuva = computed<PontoBarra[]>(() =>
    (this.clima.value()?.porChuva ?? []).map((f) => ({
      rotulo: f.faixa, valor: f.clientesPorHora, extra: f.ticketMedio ? `ticket ${formatar(f.ticketMedio, 'moeda')}` : undefined,
      detalhes: [`${f.horas} horas com o mercado aberto`],
    })),
  );

  protected readonly efeitoChuva = computed(() => {
    const [seco, chuva] = this.clima.value()?.porChuva ?? [];
    if (!seco?.clientesPorHora || !chuva?.horas) return null;
    return Math.round((chuva.clientesPorHora / seco.clientesPorHora - 1) * 100);
  });

  // ---------------------------------------------------------------- utilidades
  protected seta(variacao: number | null): string {
    return variacao === null ? '' : variacao > 0 ? '▲' : variacao < 0 ? '▼' : '■';
  }

  // "2026-10-08" → Date ao meio-dia LOCAL (meio-dia: nunca escorrega para o dia anterior por causa do fuso).
  private dataLocal(iso: string): Date {
    return new Date(`${iso}T12:00:00`);
  }
}

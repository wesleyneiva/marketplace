import { Component, computed, input, signal } from '@angular/core';
import { MARGEM_ESQ } from './grafico-colunas';
import { escalaIntervalo, formatar, larguraDoComponente } from './largura';

export interface PontoSaldo {
  rotulo: string;      // eixo de baixo e título da dica (ex.: "ter 13/10")
  valor: number;       // saldo no fim do dia/período
  detalhes?: string[]; // linhas extras da dica
}

// Linha do SALDO (uma série, sem legenda: o título diz o que é). Mostra o saldo mínimo de segurança como
// linha tracejada com rótulo, e pinta (bem claro) a região abaixo do mínimo e abaixo de zero, também com rótulo.
// Mesma margem esquerda e mesmas "faixas" do gráfico de entradas × saídas logo abaixo: os dias ficam alinhados.
@Component({
  selector: 'app-grafico-saldo',
  template: `
    <div class="grafico" (mouseleave)="ativo.set(null)">
      <svg [attr.width]="largura()" [attr.height]="ALTURA" role="img" [attr.aria-label]="descricao()">
        @let g = geo();
        @if (g.yMinimo !== null && g.yMinimo < g.base) {
          <rect class="regiao-minimo" [attr.x]="g.esq" [attr.y]="g.yMinimo" [attr.width]="g.dir - g.esq" [attr.height]="g.base - g.yMinimo" />
        }
        @if (g.yZero < g.base) {
          <rect class="regiao-negativo" [attr.x]="g.esq" [attr.y]="g.yZero" [attr.width]="g.dir - g.esq" [attr.height]="g.base - g.yZero" />
          <text class="rotulo-ref" [attr.x]="g.esq + 6" [attr.y]="g.base - 6">saldo negativo</text>
        }
        @for (t of g.marcas; track t.valor) {
          <line class="grade" [attr.x1]="g.esq" [attr.x2]="g.dir" [attr.y1]="t.y" [attr.y2]="t.y" />
          <text class="eixo" [attr.x]="g.esq - 8" [attr.y]="t.y" text-anchor="end" dominant-baseline="middle">{{ t.texto }}</text>
        }
        @if (g.yZero > g.topo && g.yZero < g.base) {
          <line class="ref-zero" [attr.x1]="g.esq" [attr.x2]="g.dir" [attr.y1]="g.yZero" [attr.y2]="g.yZero" />
        }
        @if (g.yMinimo !== null) {
          <line class="ref-minimo" [attr.x1]="g.esq" [attr.x2]="g.dir" [attr.y1]="g.yMinimo" [attr.y2]="g.yMinimo" />
          <text class="rotulo-ref" [attr.x]="g.dir" [attr.y]="g.yMinimo - 5" text-anchor="end">mínimo {{ g.textoMinimo }}</text>
        }
        <path class="linha-saldo" [attr.d]="g.caminho" />
        @for (p of g.pontos; track $index) {
          @if (p.mostrarRotulo) {
            <text class="eixo" [attr.x]="p.x" [attr.y]="g.base + 16" text-anchor="middle">{{ p.rotulo }}</text>
          }
          <rect class="alvo" [attr.x]="p.x - g.faixa / 2" [attr.y]="g.topo" [attr.width]="g.faixa" [attr.height]="g.base - g.topo"
                (mouseenter)="ativo.set($index)" />
        }
        <line class="base" [attr.x1]="g.esq" [attr.x2]="g.dir" [attr.y1]="g.base" [attr.y2]="g.base" />
        @if (ativo() !== null) {
          @let a = g.pontos[ativo()!];
          <line class="mira" [attr.x1]="a.x" [attr.x2]="a.x" [attr.y1]="g.topo" [attr.y2]="g.base" />
          <circle class="ponto-saldo" [attr.cx]="a.x" [attr.cy]="a.y" r="5" />
        }
      </svg>
      @if (ativo() !== null) {
        @let a = geo().pontos[ativo()!];
        <div class="dica-grafico" [style.left.px]="a.x" [style.top.px]="a.y">
          <strong>{{ a.rotulo }}</strong>
          <span>Saldo: {{ a.valorTexto }}</span>
          @for (d of a.detalhes; track $index) { <small>{{ d }}</small> }
        </div>
      }
    </div>
  `,
})
export class GraficoSaldo {
  readonly dados = input.required<PontoSaldo[]>();
  readonly minimo = input<number | null>(null);
  readonly descricao = input('Saldo');

  protected readonly ALTURA = 220;
  protected readonly largura = larguraDoComponente();
  protected readonly ativo = signal<number | null>(null);

  protected readonly geo = computed(() => {
    const dados = this.dados();
    const minimo = this.minimo() || null;
    const esq = MARGEM_ESQ, dir = Math.max(this.largura(), 280) - 8, topo = 12, base = this.ALTURA - 26;
    const valores = dados.map((d) => d.valor);
    const lo = Math.min(0, ...valores, minimo ?? 0);
    const hi = Math.max(...valores, minimo ?? 0, 1);
    const ticks = escalaIntervalo(lo, hi);
    const [tMin, tMax] = [ticks[0], ticks[ticks.length - 1]];
    const y = (v: number) => base - ((v - tMin) / (tMax - tMin || 1)) * (base - topo);
    const faixa = (dir - esq) / Math.max(dados.length, 1);
    const cada = Math.max(1, Math.ceil(dados.length / Math.max(2, Math.floor((dir - esq) / 70)))); // rótulo a cada ~70px

    const pontos = dados.map((d, i) => ({
      ...d, x: esq + faixa * (i + 0.5), y: y(d.valor), valorTexto: formatar(d.valor, 'moeda'),
      detalhes: d.detalhes ?? [], mostrarRotulo: i % cada === 0,
    }));
    return {
      esq, dir, topo, base, faixa, pontos,
      caminho: pontos.map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' '),
      marcas: ticks.map((v) => ({ valor: v, y: y(v), texto: formatar(v, 'moedaCompacta') })),
      yZero: y(0),
      yMinimo: minimo === null ? null : y(minimo),
      textoMinimo: minimo === null ? '' : formatar(minimo, 'moeda'),
    };
  });
}

import { Component, computed, input, signal } from '@angular/core';
import { escalaRedonda, larguraDoComponente } from './largura';
import { MARGEM_ESQ } from './grafico-colunas';

export interface PontoFaixa {
  rotulo: string;
  minimo: number | null;
  maximo: number | null;
  detalhes?: string[];
}

// Faixa entre a mínima e a máxima de cada dia (ex.: temperatura), com a máxima em linha.
// Usa a MESMA margem e a mesma divisão em faixas do gráfico de colunas: os dias ficam alinhados
// um embaixo do outro (dois gráficos alinhados em vez de um gráfico com dois eixos).
@Component({
  selector: 'app-grafico-faixa',
  template: `
    <div class="grafico" (mouseleave)="ativo.set(null)">
      <svg [attr.width]="largura()" [attr.height]="altura()" role="img" [attr.aria-label]="descricao()">
        @for (t of geo().marcas; track t.valor) {
          <line class="grade" [attr.x1]="geo().esq" [attr.x2]="largura() - 8" [attr.y1]="t.y" [attr.y2]="t.y" />
          <text class="eixo" [attr.x]="geo().esq - 8" [attr.y]="t.y" text-anchor="end" dominant-baseline="middle">{{ t.texto }}</text>
        }
        <path class="faixa-area" [attr.d]="geo().area" />
        <path class="faixa-linha" [attr.d]="geo().linha" />
        @if (ativo() !== null) {
          @let p = geo().pontos[ativo()!];
          <line class="mira" [attr.x1]="p.x" [attr.x2]="p.x" [attr.y1]="geo().topo" [attr.y2]="geo().base" />
          @if (p.yMax !== null) { <circle class="ponto" [attr.cx]="p.x" [attr.cy]="p.yMax" r="4" /> }
        }
        @for (p of geo().pontos; track $index) {
          <rect class="alvo" [attr.x]="p.faixaX" y="0" [attr.width]="geo().faixa" [attr.height]="geo().base"
                (mouseenter)="ativo.set($index)" />
        }
        <line class="base" [attr.x1]="geo().esq" [attr.x2]="largura() - 8" [attr.y1]="geo().base" [attr.y2]="geo().base" />
      </svg>
      @if (ativo() !== null) {
        @let p = geo().pontos[ativo()!];
        <div class="dica-grafico" [style.left.px]="p.x" [style.top.px]="p.yMax ?? geo().topo">
          <strong>{{ p.rotulo }}</strong>
          @for (d of p.detalhes; track $index) { <small>{{ d }}</small> }
        </div>
      }
    </div>
  `,
})
export class GraficoFaixa {
  readonly dados = input.required<PontoFaixa[]>();
  readonly unidade = input('°C');
  readonly altura = input(130);
  readonly descricao = input('Gráfico de faixa');

  protected readonly largura = larguraDoComponente();
  protected readonly ativo = signal<number | null>(null);

  protected readonly geo = computed(() => {
    const dados = this.dados();
    const esq = MARGEM_ESQ, dir = 8, topo = 8, base = this.altura() - 8;
    const largura = Math.max(this.largura(), 200);
    const faixa = (largura - esq - dir) / Math.max(dados.length, 1);
    const valores = dados.flatMap((d) => [d.minimo, d.maximo]).filter((v): v is number => v !== null);
    // O eixo começa num múltiplo de 5 logo abaixo do menor valor (e não no zero: temperatura não é "quantidade").
    const menor = valores.length ? Math.floor(Math.min(...valores) / 5) * 5 : 0;
    const marcas = escalaRedonda(Math.max(...valores, 1) - menor, 3).map((m) => m + menor);
    const maior = marcas[marcas.length - 1];
    const y = (v: number) => base - ((v - menor) / (maior - menor || 1)) * (base - topo);

    const pontos = dados.map((d, i) => ({
      faixaX: esq + i * faixa, x: esq + i * faixa + faixa / 2, rotulo: d.rotulo, detalhes: d.detalhes ?? [],
      yMax: d.maximo === null ? null : y(d.maximo), yMin: d.minimo === null ? null : y(d.minimo),
    }));
    const comDado = pontos.filter((p) => p.yMax !== null && p.yMin !== null);
    const linha = comDado.map((p, i) => `${i ? 'L' : 'M'}${p.x},${p.yMax}`).join(' ');
    const area = comDado.length
      ? linha + ' ' + [...comDado].reverse().map((p) => `L${p.x},${p.yMin}`).join(' ') + ' Z'
      : '';

    return {
      esq, topo, base, faixa, pontos, linha, area,
      marcas: marcas.map((v) => ({ valor: v, y: y(v), texto: `${v} ${this.unidade()}` })),
    };
  });
}

import { Component, computed, input, signal } from '@angular/core';
import { Formato, formatar } from './largura';

export interface FatiaRosca {
  rotulo: string;
  valor: number;
  cor: number;          // 1 a 5 (--grafico-cat-N): segue a COISA (o plano, o cliente), nunca a posição no ranking
  detalhes?: string[];  // linhas extras do tooltip
}

// Rosca (parte de um todo): poucas fatias, o total no meio e a legenda com valor e % ao lado — a cor nunca fica
// sozinha para dizer quem é quem. Fatias separadas por um fio da cor do fundo.
@Component({
  selector: 'app-grafico-rosca',
  template: `
    <div class="rosca" (mouseleave)="ativo.set(null)">
      <svg [attr.width]="tamanho" [attr.height]="tamanho" [attr.viewBox]="'0 0 ' + tamanho + ' ' + tamanho" role="img" [attr.aria-label]="descricao()">
        @if (geo().fatias.length === 0) {
          <circle [attr.cx]="c" [attr.cy]="c" [attr.r]="raio - espessura / 2" class="vazia" [attr.stroke-width]="espessura" />
        }
        @for (f of geo().fatias; track f.rotulo) {
          <path [attr.d]="f.caminho" class="fatia" fill-rule="evenodd" [style.fill]="'var(--grafico-cat-' + f.cor + ')'"
                [class.apagada]="ativo() !== null && ativo() !== $index" (mouseenter)="ativo.set($index)" />
        }
        <text [attr.x]="c" [attr.y]="c - 6" text-anchor="middle" class="centro-valor">{{ centro().valor }}</text>
        <text [attr.x]="c" [attr.y]="c + 14" text-anchor="middle" class="centro-rotulo">{{ centro().rotulo }}</text>
      </svg>
      <ul class="legenda">
        @for (f of geo().fatias; track f.rotulo) {
          <li [class.ativa]="ativo() === $index" (mouseenter)="ativo.set($index)">
            <i [style.background]="'var(--grafico-cat-' + f.cor + ')'"></i>
            <span class="nome">{{ f.rotulo }}</span>
            <span class="num">{{ f.valorTexto }}</span>
            <span class="pct">{{ f.pct }}</span>
          </li>
          @for (d of f.detalhes; track $index) { <li class="detalhe">{{ d }}</li> }
        } @empty {
          <li class="vazio">{{ vazio() }}</li>
        }
      </ul>
    </div>
  `,
  styles: `
    .rosca { display: flex; flex-wrap: wrap; align-items: center; gap: 18px 24px; }
    svg { flex: none; overflow: visible; font-family: inherit; }
    .fatia { stroke: var(--cor-superficie); stroke-width: 2; transition: opacity 0.12s; }
    .fatia.apagada { opacity: 0.35; }
    .vazia { fill: none; stroke: var(--grafico-grade); }
    .centro-valor { fill: var(--cor-texto); font-size: 17px; font-weight: 700; font-variant-numeric: tabular-nums; }
    .centro-rotulo { fill: var(--cor-texto-suave); font-size: 12px; }
    .legenda { flex: 1 1 200px; min-width: 0; list-style: none; margin: 0; padding: 0; font-size: 14px; }
    .legenda li { display: flex; align-items: center; gap: 8px; padding: 6px 0; border-top: 1px solid var(--cor-borda); }
    .legenda li:first-child { border-top: 0; }
    .legenda li.ativa .nome { text-decoration: underline; }
    .legenda i { flex: none; width: 12px; height: 12px; border-radius: 3px; }
    .nome { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; }
    .num { font-variant-numeric: tabular-nums; white-space: nowrap; }
    .pct { width: 44px; text-align: right; color: var(--cor-texto-suave); font-variant-numeric: tabular-nums; }
    .legenda li.detalhe { border-top: 0; padding: 0 0 6px 20px; color: var(--cor-texto-suave); font-size: 12px; }
    .legenda li.vazio { color: var(--cor-texto-suave); }
  `,
})
export class GraficoRosca {
  readonly dados = input.required<FatiaRosca[]>();
  readonly formato = input<Formato>('moeda');
  readonly rotuloCentro = input('total');
  readonly descricao = input('Gráfico de rosca');
  readonly vazio = input('Nada no período.');

  protected readonly tamanho = 176;
  protected readonly c = this.tamanho / 2;
  protected readonly raio = this.tamanho / 2;
  protected readonly espessura = 26;
  protected readonly ativo = signal<number | null>(null);

  protected readonly geo = computed(() => {
    const dados = this.dados().filter((d) => d.valor > 0);
    const total = dados.reduce((s, d) => s + d.valor, 0);
    let angulo = -Math.PI / 2; // começa no topo, sentido horário
    return {
      total,
      fatias: dados.map((d) => {
        const fracao = total ? d.valor / total : 0;
        const fim = angulo + fracao * 2 * Math.PI;
        const caminho = this.arco(angulo, fim);
        angulo = fim;
        return {
          rotulo: d.rotulo, cor: d.cor, caminho, detalhes: d.detalhes ?? [],
          valorTexto: formatar(d.valor, this.formato()),
          pct: `${(fracao * 100).toLocaleString('pt-BR', { maximumFractionDigits: fracao < 0.1 ? 1 : 0 })}%`,
        };
      }),
    };
  });

  // No meio: o total, ou a fatia sob o mouse.
  protected readonly centro = computed(() => {
    const i = this.ativo();
    const f = i === null ? null : this.geo().fatias[i];
    return f ? { valor: f.valorTexto, rotulo: f.pct } : { valor: formatar(this.geo().total, this.formato()), rotulo: this.rotuloCentro() };
  });

  // Anel entre o raio de fora e o de dentro, de "de" até "ate" (radianos). Uma fatia só = anel inteiro (dois meios arcos).
  private arco(de: number, ate: number): string {
    const [c, r1, r2] = [this.c, this.raio, this.raio - this.espessura];
    if (ate - de >= 2 * Math.PI - 1e-6) {
      return `M ${c} ${c - r1} A ${r1} ${r1} 0 1 1 ${c} ${c + r1} A ${r1} ${r1} 0 1 1 ${c} ${c - r1} Z `
        + `M ${c} ${c - r2} A ${r2} ${r2} 0 1 0 ${c} ${c + r2} A ${r2} ${r2} 0 1 0 ${c} ${c - r2} Z`;
    }
    const p = (r: number, a: number) => `${(c + r * Math.cos(a)).toFixed(2)} ${(c + r * Math.sin(a)).toFixed(2)}`;
    const grande = ate - de > Math.PI ? 1 : 0;
    return `M ${p(r1, de)} A ${r1} ${r1} 0 ${grande} 1 ${p(r1, ate)} L ${p(r2, ate)} A ${r2} ${r2} 0 ${grande} 0 ${p(r2, de)} Z`;
  }
}

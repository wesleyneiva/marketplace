import { Component, computed, input, signal } from '@angular/core';
import { Formato, caminhoColuna, escalaRedonda, formatar, larguraDoComponente } from './largura';

export interface PontoColuna {
  rotulo: string;      // aparece no eixo de baixo (ex.: "08/10")
  valor: number;
  detalhes?: string[]; // linhas extras do tooltip
  apagado?: boolean;   // ex.: o dia de hoje, ainda incompleto
}

// Gráfico de colunas de UMA série (uma cor só: o título já diz o que é, sem legenda).
// Margem esquerda fixa (MARGEM_ESQ) para alinhar com o gráfico de faixa logo abaixo.
export const MARGEM_ESQ = 64;

@Component({
  selector: 'app-grafico-colunas',
  template: `
    <div class="grafico" (mouseleave)="ativo.set(null)">
      <svg [attr.width]="largura()" [attr.height]="altura()" role="img" [attr.aria-label]="descricao()">
        @for (t of geo().marcas; track t.valor) {
          <line class="grade" [attr.x1]="geo().esq" [attr.x2]="largura() - 8" [attr.y1]="t.y" [attr.y2]="t.y" />
          <text class="eixo" [attr.x]="geo().esq - 8" [attr.y]="t.y" text-anchor="end" dominant-baseline="middle">{{ t.texto }}</text>
        }
        @for (c of geo().colunas; track $index) {
          <path [attr.d]="c.caminho" class="coluna" [class.apagada]="c.apagado" [class.ativa]="ativo() === $index" />
          @if (c.mostrarRotulo) {
            <text class="eixo" [attr.x]="c.centro" [attr.y]="geo().base + 16" text-anchor="middle">{{ c.rotulo }}</text>
          }
          <!-- área de "acerto" do mouse: a faixa inteira, maior que a coluna -->
          <rect class="alvo" [attr.x]="c.faixaX" y="0" [attr.width]="geo().faixa" [attr.height]="geo().base"
                (mouseenter)="ativo.set($index)" />
        }
        <line class="base" [attr.x1]="geo().esq" [attr.x2]="largura() - 8" [attr.y1]="geo().base" [attr.y2]="geo().base" />
      </svg>
      @if (ativo() !== null) {
        @let c = geo().colunas[ativo()!];
        <div class="dica-grafico" [style.left.px]="c.centro" [style.top.px]="c.topo">
          <strong>{{ c.rotulo }}</strong>
          <span>{{ c.valorTexto }}</span>
          @for (d of c.detalhes; track $index) { <small>{{ d }}</small> }
        </div>
      }
    </div>
  `,
})
export class GraficoColunas {
  readonly dados = input.required<PontoColuna[]>();
  readonly formato = input<Formato>('moeda');
  readonly altura = input(220);
  readonly descricao = input('Gráfico de colunas');

  protected readonly largura = larguraDoComponente();
  protected readonly ativo = signal<number | null>(null);

  protected readonly geo = computed(() => {
    const dados = this.dados();
    const esq = MARGEM_ESQ, dir = 8, topo = 12, base = this.altura() - 24;
    const largura = Math.max(this.largura(), 200);
    const faixa = (largura - esq - dir) / Math.max(dados.length, 1);
    const espessura = Math.min(24, Math.max(2, faixa * 0.7)); // nunca mais grossa que 24px
    const marcasValor = escalaRedonda(Math.max(...dados.map((d) => d.valor), 0));
    const maximo = marcasValor[marcasValor.length - 1] || 1;
    const y = (v: number) => base - (v / maximo) * (base - topo);
    const pularRotulos = Math.ceil(dados.length / Math.max(1, Math.floor((largura - esq) / 46)));

    return {
      esq, base, faixa,
      marcas: marcasValor.map((v) => ({ valor: v, y: y(v), texto: formatar(v, this.formato() === 'moeda' ? 'moedaCompacta' : 'compacto') })),
      colunas: dados.map((d, i) => {
        const faixaX = esq + i * faixa;
        const x = faixaX + (faixa - espessura) / 2;
        return {
          faixaX, centro: faixaX + faixa / 2, topo: y(d.valor),
          caminho: caminhoColuna(x, y(d.valor), espessura, base),
          rotulo: d.rotulo, mostrarRotulo: i % pularRotulos === 0, apagado: d.apagado,
          valorTexto: formatar(d.valor, this.formato()), detalhes: d.detalhes ?? [],
        };
      }),
    };
  });
}

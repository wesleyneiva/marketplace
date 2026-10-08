import { Component, computed, input, signal } from '@angular/core';
import { Formato, caminhoBarra, formatar, larguraDoComponente } from './largura';

export interface PontoBarra {
  rotulo: string;      // nome à esquerda
  valor: number;
  extra?: string;      // texto discreto depois do valor (ex.: "margem 41,7%")
  detalhes?: string[]; // tooltip
  destaque?: boolean;  // pinta com a cor de destaque (ex.: o maior)
}

// Barras deitadas de UMA série: o nome fica escrito ao lado, então não precisa de cores diferentes.
// O valor vai na ponta da barra (rótulo direto), em cor de texto — nunca na cor da barra.
@Component({
  selector: 'app-grafico-barras',
  template: `
    <div class="grafico" (mouseleave)="ativo.set(null)">
      <svg [attr.width]="largura()" [attr.height]="geo().altura" role="img" [attr.aria-label]="descricao()">
        @for (b of geo().barras; track b.rotulo) {
          <text class="rotulo" [attr.x]="geo().rotuloW - 10" [attr.y]="b.centro" text-anchor="end" dominant-baseline="middle">{{ b.rotulo }}</text>
          <path [attr.d]="b.caminho" class="coluna" [class.destaque]="b.destaque" [class.ativa]="ativo() === $index" />
          <text class="valor" [attr.x]="b.fim + 8" [attr.y]="b.centro" dominant-baseline="middle">
            {{ b.valorTexto }}@if (b.extra) {<tspan class="extra"> · {{ b.extra }}</tspan>}
          </text>
          <rect class="alvo" x="0" [attr.y]="b.centro - geo().passo / 2" [attr.width]="largura()" [attr.height]="geo().passo"
                (mouseenter)="ativo.set($index)" />
        }
        <line class="base" [attr.x1]="geo().rotuloW" [attr.x2]="geo().rotuloW" y1="0" [attr.y2]="geo().altura" />
      </svg>
      @if (ativo() !== null && geo().barras[ativo()!].detalhes.length) {
        @let b = geo().barras[ativo()!];
        <div class="dica-grafico" [style.left.px]="b.fim" [style.top.px]="b.centro - 10">
          <strong>{{ b.rotulo }}</strong>
          @for (d of b.detalhes; track $index) { <small>{{ d }}</small> }
        </div>
      }
    </div>
  `,
})
export class GraficoBarras {
  readonly dados = input.required<PontoBarra[]>();
  readonly formato = input<Formato>('moeda');
  readonly larguraRotulos = input(110);
  readonly descricao = input('Gráfico de barras');

  protected readonly largura = larguraDoComponente();
  protected readonly ativo = signal<number | null>(null);

  protected readonly geo = computed(() => {
    const dados = this.dados();
    const passo = 32, espessura = 18;
    const rotuloW = this.larguraRotulos();
    const espacoTexto = 150; // reserva à direita para "R$ 84.315 · margem 32%"
    const largura = Math.max(this.largura(), 260);
    const maximo = Math.max(...dados.map((d) => d.valor), 1);
    const escala = (largura - rotuloW - espacoTexto) / maximo;

    return {
      passo, rotuloW, altura: dados.length * passo + 4,
      barras: dados.map((d, i) => {
        const centro = i * passo + passo / 2 + 2;
        const comprimento = Math.max(d.valor * escala, d.valor > 0 ? 2 : 0);
        return {
          rotulo: d.rotulo, centro, fim: rotuloW + comprimento, destaque: d.destaque, extra: d.extra,
          caminho: caminhoBarra(rotuloW, centro - espessura / 2, comprimento, espessura),
          valorTexto: formatar(d.valor, this.formato()), detalhes: d.detalhes ?? [],
        };
      }),
    };
  });
}

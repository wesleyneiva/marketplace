import { Component, computed, input, signal } from '@angular/core';
import { larguraDoComponente } from './largura';

export interface CelulaMapa {
  linha: number;   // ex.: dia da semana (0 = domingo)
  coluna: number;  // ex.: hora
  valor: number;
}

// Rampa SEQUENCIAL de um tom só (azul), do claro (pouco) ao escuro (muito) — validada para daltonismo.
const RAMPA = ['#cde2fb', '#9ec5f4', '#6da7ec', '#3987e5', '#256abf', '#184f95', '#0d366b'];

// Mapa de calor: linhas × colunas, a cor mostra a intensidade. Células separadas por 2px de "ar".
@Component({
  selector: 'app-mapa-calor',
  template: `
    <div class="grafico" (mouseleave)="ativo.set(null)">
      <svg [attr.width]="largura()" [attr.height]="geo().altura" role="img" [attr.aria-label]="descricao()">
        @for (c of geo().colunas; track c.valor) {
          <text class="eixo" [attr.x]="c.x" [attr.y]="12" text-anchor="middle">{{ c.texto }}</text>
        }
        @for (l of geo().linhas; track l.valor) {
          <text class="rotulo" [attr.x]="geo().esq - 8" [attr.y]="l.y + geo().lado / 2" text-anchor="end" dominant-baseline="middle">{{ l.texto }}</text>
        }
        @for (c of geo().celulas; track $index) {
          <rect [attr.x]="c.x" [attr.y]="c.y" [attr.width]="geo().ladoX" [attr.height]="geo().lado" rx="3"
                [attr.fill]="c.cor" class="celula" [class.ativa]="ativo() === $index" (mouseenter)="ativo.set($index)" />
        }
      </svg>
      <div class="legenda-rampa">
        <span>menos</span>
        @for (cor of rampa; track cor) { <i [style.background]="cor"></i> }
        <span>mais</span>
      </div>
      @if (ativo() !== null) {
        @let c = geo().celulas[ativo()!];
        <div class="dica-grafico" [style.left.px]="c.x + geo().ladoX / 2" [style.top.px]="c.y">
          <strong>{{ c.titulo }}</strong>
          <small>{{ c.texto }}</small>
        </div>
      }
    </div>
  `,
})
export class MapaCalor {
  readonly celulas = input.required<CelulaMapa[]>();
  readonly linhas = input.required<{ valor: number; texto: string }[]>();
  readonly colunas = input.required<{ valor: number; texto: string }[]>();
  readonly textoValor = input<(c: CelulaMapa) => string>((c) => String(c.valor));
  readonly tituloCelula = input<(c: CelulaMapa) => string>((c) => `${c.linha} × ${c.coluna}`);
  readonly descricao = input('Mapa de calor');

  protected readonly rampa = RAMPA;
  protected readonly largura = larguraDoComponente();
  protected readonly ativo = signal<number | null>(null);

  protected readonly geo = computed(() => {
    const esq = 64, topo = 20, gap = 2, lado = 26;
    const colunas = this.colunas(), linhas = this.linhas();
    const ladoX = Math.max(10, (Math.max(this.largura(), 260) - esq) / colunas.length - gap);
    const maximo = Math.max(...this.celulas().map((c) => c.valor), 0.0001);
    const xDe = (col: number) => esq + colunas.findIndex((c) => c.valor === col) * (ladoX + gap);
    const yDe = (lin: number) => topo + linhas.findIndex((l) => l.valor === lin) * (lado + gap);

    return {
      esq, lado, ladoX, altura: topo + linhas.length * (lado + gap),
      colunas: colunas.map((c) => ({ ...c, x: xDe(c.valor) + ladoX / 2 })),
      linhas: linhas.map((l) => ({ ...l, y: yDe(l.valor) })),
      celulas: this.celulas()
        .filter((c) => colunas.some((k) => k.valor === c.coluna) && linhas.some((l) => l.valor === c.linha))
        .map((c) => ({
          x: xDe(c.coluna), y: yDe(c.linha),
          cor: RAMPA[Math.min(RAMPA.length - 1, Math.floor((c.valor / maximo) * RAMPA.length))],
          titulo: this.tituloCelula()(c), texto: this.textoValor()(c),
        })),
    };
  });
}

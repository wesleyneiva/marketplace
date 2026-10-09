import { Component, computed, input } from '@angular/core';

// Código de barras EAN-13 / EAN-8 desenhado em SVG (sem biblioteca): o leitor do caixa lê direto da etiqueta.
// Cada dígito vira 7 "módulos" (barras finas pretas ou brancas) seguindo as tabelas L, G e R do padrão GS1.
const L = ['0001101', '0011001', '0010011', '0111101', '0100011', '0110001', '0101111', '0111011', '0110111', '0001011'];
const G = ['0100111', '0110011', '0011011', '0100001', '0011101', '0111001', '0000101', '0010001', '0001001', '0010111'];
const R = ['1110010', '1100110', '1101100', '1000010', '1011100', '1001110', '1010000', '1000100', '1001000', '1110100'];
// No EAN-13, o 1º dígito não vira barra: ele decide se cada dígito da esquerda usa a tabela L ou G.
const PARIDADE = ['LLLLLL', 'LLGLGG', 'LLGGLG', 'LLGGGL', 'LGLLGG', 'LGGLLG', 'LGGGLL', 'LGLGLG', 'LGLGGL', 'LGGLGL'];

export function modulosEan(codigo: string): string | null {
  if (/^\d{13}$/.test(codigo)) {
    const p = PARIDADE[+codigo[0]];
    const esquerda = [...codigo.slice(1, 7)].map((d, i) => (p[i] === 'L' ? L : G)[+d]).join('');
    const direita = [...codigo.slice(7)].map((d) => R[+d]).join('');
    return `101${esquerda}01010${direita}101`;
  }
  if (/^\d{8}$/.test(codigo)) {
    return `101${[...codigo.slice(0, 4)].map((d) => L[+d]).join('')}01010${[...codigo.slice(4)].map((d) => R[+d]).join('')}101`;
  }
  return null; // outros formatos: a etiqueta mostra só os números
}

@Component({
  selector: 'app-codigo-barras',
  template: `
    @if (barras(); as b) {
      <svg [attr.viewBox]="'0 0 ' + b.largura + ' 34'" preserveAspectRatio="none" role="img" [attr.aria-label]="'Código de barras ' + codigo()">
        @for (x of b.pretas; track $index) { <rect [attr.x]="x.inicio" y="0" [attr.width]="x.largura" height="34" /> }
      </svg>
    }
    <span class="numeros">{{ codigo() }}</span>
  `,
  styles: `
    :host { display: flex; flex-direction: column; align-items: center; }
    svg { width: 100%; height: 100%; min-height: 0; flex: 1; fill: #000; }
    .numeros { font-size: 7pt; letter-spacing: 0.08em; line-height: 1; margin-top: 1px; }
  `,
})
export class CodigoBarras {
  readonly codigo = input.required<string>();

  // Junta os módulos pretos seguidos num retângulo só (menos elementos no SVG). 9 de margem branca de cada lado.
  protected readonly barras = computed(() => {
    const m = modulosEan(this.codigo());
    if (!m) return null;
    const pretas: { inicio: number; largura: number }[] = [];
    for (let i = 0; i < m.length; i++) {
      if (m[i] !== '1') continue;
      const ultima = pretas.at(-1);
      if (ultima && ultima.inicio + ultima.largura === i + 9) ultima.largura++;
      else pretas.push({ inicio: i + 9, largura: 1 });
    }
    return { largura: m.length + 18, pretas };
  });
}

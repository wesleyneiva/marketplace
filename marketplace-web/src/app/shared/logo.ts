import { Component, input } from '@angular/core';
import { MARCA } from './marca';

// Logo do Marcus: o ícone (coroa de louros + M romano) e, se pedido, o nome e a frase ao lado.
// Uso:  <app-logo />   ·   <app-logo [tamanho]="56" />   ·   <app-logo tema="escuro" />  (fundo escuro)
//       <app-logo [nome]="false" />  (só o ícone)
@Component({
  selector: 'app-logo',
  template: `
    <span class="logo" [class.escuro]="tema() === 'escuro'" [style.gap.px]="tamanho() * 0.28">
      <svg [attr.width]="tamanho()" [attr.height]="tamanho()" viewBox="0 0 64 64" aria-hidden="true">
        <!-- Coroa de louros (Marco Aurélio, o imperador filósofo) em volta do M romano. Igual a public/favicon.svg. -->
        <rect width="64" height="64" rx="15" fill="#15803d"/>
        <g fill="#facc15"><path d="M28.6 52.5A19.3 19.3 0 0 1 17.2 21.1" fill="none" stroke="#facc15" stroke-width="1.3" stroke-linecap="round"/><ellipse cx="28.4" cy="53.7" rx="4.1" ry="1.75" transform="rotate(155 28.4 53.7)"/><ellipse cx="22.1" cy="51.4" rx="4.1" ry="1.75" transform="rotate(174 22.1 51.4)"/><ellipse cx="16.8" cy="47.2" rx="4.1" ry="1.75" transform="rotate(193 16.8 47.2)"/><ellipse cx="13.1" cy="41.5" rx="4.1" ry="1.75" transform="rotate(212 13.1 41.5)"/><ellipse cx="11.5" cy="34.9" rx="4.1" ry="1.75" transform="rotate(231 11.5 34.9)"/><ellipse cx="12.2" cy="28.2" rx="4.1" ry="1.75" transform="rotate(250 12.2 28.2)"/><ellipse cx="15.0" cy="22.0" rx="4.1" ry="1.75" transform="rotate(269 15.0 22.0)"/><path d="M35.4 52.5A19.3 19.3 0 0 0 46.8 21.1" fill="none" stroke="#facc15" stroke-width="1.3" stroke-linecap="round"/><ellipse cx="35.6" cy="53.7" rx="4.1" ry="1.75" transform="rotate(205 35.6 53.7)"/><ellipse cx="41.9" cy="51.4" rx="4.1" ry="1.75" transform="rotate(186 41.9 51.4)"/><ellipse cx="47.2" cy="47.2" rx="4.1" ry="1.75" transform="rotate(167 47.2 47.2)"/><ellipse cx="50.9" cy="41.5" rx="4.1" ry="1.75" transform="rotate(148 50.9 41.5)"/><ellipse cx="52.5" cy="34.9" rx="4.1" ry="1.75" transform="rotate(129 52.5 34.9)"/><ellipse cx="51.8" cy="28.2" rx="4.1" ry="1.75" transform="rotate(110 51.8 28.2)"/><ellipse cx="49.0" cy="22.0" rx="4.1" ry="1.75" transform="rotate(91 49.0 22.0)"/></g>
        <path fill="#fff" d="M18.5 43h7.2v-1.6h-1.9V29.2l7.3 13.6h1.8l7.3-13.6v12.2h-1.9V43h7.2v-1.6h-1.8V25.6h1.8V24h-6.1L32 37.4 24.6 24h-6.1v1.6h1.8v15.8h-1.8z"/>
      </svg>
      @if (nome()) {
        <span class="texto">
          <span class="nome" [style.font-size.px]="tamanho() * 0.5">Marcus</span>
          @if (slogan()) { <span class="slogan" [style.font-size.px]="tamanho() * 0.22">{{ frase }}</span> }
        </span>
      }
    </span>
  `,
  styles: `
    :host { display: inline-flex; }
    .logo { display: inline-flex; align-items: center; color: var(--cor-texto); }
    svg { flex-shrink: 0; display: block; }
    .texto { display: flex; flex-direction: column; line-height: 1.05; }
    .nome { font-weight: 750; letter-spacing: -0.02em; }
    .slogan { color: var(--cor-texto-suave); font-weight: 500; margin-top: 2px; white-space: nowrap; }
    .escuro { color: #fff; }
    .escuro .slogan { color: #a7c4b2; }
  `,
})
export class Logo {
  readonly tamanho = input(40);
  readonly nome = input(true);
  readonly slogan = input(true);
  readonly tema = input<'claro' | 'escuro'>('claro');
  protected readonly frase = MARCA.frase;
}

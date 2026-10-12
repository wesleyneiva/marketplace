import { Component, input } from '@angular/core';
import { MARCA } from './marca';

// Logo do Marcus: o ícone (templo romano) e, se pedido, o nome e a frase ao lado.
// Uso:  <app-logo />   ·   <app-logo [tamanho]="56" />   ·   <app-logo tema="escuro" />  (fundo escuro)
//       <app-logo [nome]="false" />  (só o ícone)
@Component({
  selector: 'app-logo',
  template: `
    <span class="logo" [class.escuro]="tema() === 'escuro'" [style.gap.px]="tamanho() * 0.28">
      <svg [attr.width]="tamanho()" [attr.height]="tamanho()" viewBox="0 0 64 64" aria-hidden="true">
        <!-- Templo romano (Marco Aurélio, o imperador filósofo). Igual a public/favicon.svg. -->
        <rect width="64" height="64" rx="15" fill="#15803d"/>
        <g fill="#fff">
        <path d="M32 11.5 54 24H10z"/>
        <path d="M32 15.6 45.2 22.2H18.8z" fill="#15803d"/>
        <rect x="11" y="25.4" width="42" height="2.4" rx="0.6"/>
        <rect x="13" y="28.6" width="38" height="1.6" rx="0.5"/>
        <rect x="15.8" y="31" width="5.4" height="2" rx="0.4"/>
        <rect x="16.7" y="33" width="3.6" height="12"/>
        <rect x="15.8" y="45" width="5.4" height="1.6" rx="0.4"/>
        <rect x="24.8" y="31" width="5.4" height="2" rx="0.4"/>
        <rect x="25.7" y="33" width="3.6" height="12"/>
        <rect x="24.8" y="45" width="5.4" height="1.6" rx="0.4"/>
        <rect x="33.8" y="31" width="5.4" height="2" rx="0.4"/>
        <rect x="34.7" y="33" width="3.6" height="12"/>
        <rect x="33.8" y="45" width="5.4" height="1.6" rx="0.4"/>
        <rect x="42.8" y="31" width="5.4" height="2" rx="0.4"/>
        <rect x="43.7" y="33" width="3.6" height="12"/>
        <rect x="42.8" y="45" width="5.4" height="1.6" rx="0.4"/>
        <rect x="11" y="47.6" width="42" height="2.4" rx="0.6"/>
        <rect x="8.5" y="51" width="47" height="2.6" rx="0.6"/>
        </g>
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

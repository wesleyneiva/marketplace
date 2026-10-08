import { Component, input } from '@angular/core';

// Logo do Marketplace: o ícone (cesta + folha) e, se pedido, o nome ao lado.
// Uso:  <app-logo />   ·   <app-logo [tamanho]="56" />   ·   <app-logo tema="escuro" />  (fundo escuro)
//       <app-logo [nome]="false" />  (só o ícone)
@Component({
  selector: 'app-logo',
  template: `
    <span class="logo" [class.escuro]="tema() === 'escuro'" [style.gap.px]="tamanho() * 0.28">
      <svg [attr.width]="tamanho()" [attr.height]="tamanho()" viewBox="0 0 64 64" aria-hidden="true">
        <rect width="64" height="64" rx="15" fill="#15803d" />
        <path d="M40 9c7-3 13 1 13 1-2 8-9 10-13 8-1-3-1-6 0-9z" fill="#86efac" />
        <path d="M19 29c0-11 26-11 26 0" fill="none" stroke="#fff" stroke-width="4.5" stroke-linecap="round" />
        <path d="M10 28h44l-5.2 22.5A4 4 0 0 1 44.9 54H19.1a4 4 0 0 1-3.9-3.5z" fill="#fff" />
        <path d="M25 35v12M32 35v12M39 35v12" stroke="#15803d" stroke-width="3.2" stroke-linecap="round" />
      </svg>
      @if (nome()) {
        <span class="texto">
          <span class="nome" [style.font-size.px]="tamanho() * 0.5">Marketplace</span>
          @if (slogan()) { <span class="slogan" [style.font-size.px]="tamanho() * 0.24">mercadinho de bairro</span> }
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
    .slogan { color: var(--cor-texto-suave); font-weight: 500; letter-spacing: 0.02em; margin-top: 2px; }
    .escuro { color: #fff; }
    .escuro .slogan { color: #a7c4b2; }
  `,
})
export class Logo {
  readonly tamanho = input(40);
  readonly nome = input(true);
  readonly slogan = input(true);
  readonly tema = input<'claro' | 'escuro'>('claro');
}

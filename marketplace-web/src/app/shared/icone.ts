import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, input } from '@angular/core';
import { ICONES } from './icones';

// Ícone de traço (Lucide). Herda a cor do texto e acompanha o tamanho da fonte (1em).
// Uso:  <app-icone nome="printer" />   ·   <app-icone [nome]="x.icone" [tamanho]="20" />
//       <app-icone nome="loader-circle" girando />   (carregando)
// Nome que não existe em icones.ts → não desenha nada (e avisa no console em desenvolvimento).
@Component({
  selector: 'app-icone',
  template: '',
  styles: `
    :host {
      display: inline-flex;
      flex-shrink: 0;
      width: 1.15em;
      height: 1.15em;
      vertical-align: -0.2em;
    }
    :host([girando]) { animation: girar 0.9s linear infinite; }
    @keyframes girar { to { transform: rotate(360deg); } }
  `,
  host: { 'aria-hidden': 'true', '[style.width.px]': 'tamanho()', '[style.height.px]': 'tamanho()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Icone {
  readonly nome = input.required<string>();
  readonly tamanho = input<number | undefined>(undefined); // em px; vazio = acompanha a fonte
  readonly traco = input(2);

  private readonly el = inject(ElementRef<HTMLElement>);

  constructor() {
    // Os desenhos são constantes do próprio sistema (icones.ts), nunca texto vindo do usuário.
    effect(() => {
      const corpo = ICONES[this.nome()];
      if (corpo === undefined) console.warn(`Ícone desconhecido: ${this.nome()}`);
      this.el.nativeElement.innerHTML = corpo === undefined ? '' :
        `<svg xmlns="http://www.w3.org/2000/svg" width="100%" height="100%" viewBox="0 0 24 24" fill="none" ` +
        `stroke="currentColor" stroke-width="${this.traco()}" stroke-linecap="round" stroke-linejoin="round">${corpo}</svg>`;
    });
  }
}

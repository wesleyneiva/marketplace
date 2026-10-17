import { Component, DestroyRef, inject, signal } from '@angular/core';

// Relógio da barra do topo: hora de Brasília (não a do computador nem a da loja), 24 h, HH:MM.
const HORA = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });

@Component({
  selector: 'app-relogio-brasilia',
  template: `<strong class="relogio" title="Horário de Brasília" aria-label="Horário de Brasília">{{ hora() }}</strong>`,
  styles: `
    .relogio { font-size: 17px; font-weight: 600; font-variant-numeric: tabular-nums; letter-spacing: 0.01em; }
    @media (max-width: 520px) { .relogio { font-size: 15px; } }
  `,
})
export class RelogioBrasilia {
  protected readonly hora = signal('');

  constructor() {
    const marcar = () => this.hora.set(HORA.format(new Date()));
    marcar();
    const id = setInterval(marcar, 5000); // confere a cada 5 s: o minuto vira no máximo 5 s atrasado
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }
}

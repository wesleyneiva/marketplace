import { Component, DestroyRef, inject, signal } from '@angular/core';

// Relógio da barra do topo: hora de Brasília (não a do computador nem a da loja), atualizado a cada segundo.
const HORA = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit', second: '2-digit' });
const DIA = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', weekday: 'short', day: '2-digit', month: '2-digit' });

@Component({
  selector: 'app-relogio-brasilia',
  template: `
    <div class="relogio" title="Horário de Brasília" aria-label="Horário de Brasília">
      <strong>{{ hora() }}</strong>
      <small>{{ dia() }} · Brasília</small>
    </div>`,
  styles: `
    .relogio { display: flex; flex-direction: column; align-items: flex-end; line-height: 1.15; }
    strong { font-size: 17px; font-weight: 600; font-variant-numeric: tabular-nums; letter-spacing: 0.01em; }
    small { font-size: 12px; color: var(--cor-texto-suave); }
    @media (max-width: 520px) { small { display: none; } strong { font-size: 15px; } }
  `,
})
export class RelogioBrasilia {
  protected readonly hora = signal('');
  protected readonly dia = signal('');

  constructor() {
    const marcar = () => {
      const agora = new Date();
      this.hora.set(HORA.format(agora));
      this.dia.set(DIA.format(agora).replace('.', ''));
    };
    marcar();
    const id = setInterval(marcar, 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }
}

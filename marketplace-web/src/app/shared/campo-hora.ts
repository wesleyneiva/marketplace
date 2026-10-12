import { Component, computed, input, model } from '@angular/core';

// Campo de hora SEMPRE em 24 horas ("08:00", "21:30"), igual em qualquer navegador e idioma.
// (O <input type="time"> segue o idioma do navegador: em inglês aparece AM/PM.)
// Uso:  <app-campo-hora [(valor)]="dia.abre" rotulo="Segunda: abre às" />
@Component({
  selector: 'app-campo-hora',
  template: `
    <span class="campo" role="group" [attr.aria-label]="rotulo()">
      <select (change)="mudar($any($event.target).value, minuto())" [disabled]="desabilitado()" [attr.aria-label]="rotulo() + ' (hora)'">
        @for (h of horas; track h) { <option [value]="h" [selected]="h === hora()">{{ h }}</option> }
      </select>
      <span class="dois-pontos">:</span>
      <select (change)="mudar(hora(), $any($event.target).value)" [disabled]="desabilitado()" [attr.aria-label]="rotulo() + ' (minutos)'">
        @for (m of minutosDaLista(); track m) { <option [value]="m" [selected]="m === minuto()">{{ m }}</option> }
      </select>
    </span>
  `,
  styles: `
    .campo { display: inline-flex; align-items: center; gap: 2px; }
    select { padding: 7px 6px; font-variant-numeric: tabular-nums; }
    .dois-pontos { font-weight: 700; }
  `,
})
export class CampoHora {
  readonly valor = model.required<string>();   // "HH:MM"
  readonly rotulo = input('Hora');
  readonly desabilitado = input(false);

  protected readonly horas = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0'));
  private readonly minutos = ['00', '15', '30', '45'];

  protected readonly hora = computed(() => (this.valor() ?? '00:00').slice(0, 2));
  protected readonly minuto = computed(() => (this.valor() ?? '00:00').slice(3, 5));
  // Se a hora salva tiver outro minuto (ex.: 07:40), ele continua na lista.
  protected readonly minutosDaLista = computed(() =>
    this.minutos.includes(this.minuto()) ? this.minutos : [...this.minutos, this.minuto()].sort());

  protected mudar(h: string, m: string): void {
    this.valor.set(`${h}:${m}`);
  }
}

import { Component, ElementRef, forwardRef, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Icone } from './icone';

// Campo de data SEMPRE em dd/mm/aaaa, igual em qualquer navegador e idioma.
// (O <input type="date"> segue o idioma do navegador: em inglês aparece mm/dd/aaaa.)
// Digita-se só os números (as barras entram sozinhas); o botão do calendário abre o calendário do navegador.
// O valor do modelo continua no formato do <input type="date">: "AAAA-MM-DD" ('' quando vazio ou incompleto).
// Uso:  <app-campo-data name="vencimento" [(ngModel)]="form.vencimento" required [min]="hoje" />
@Component({
  selector: 'app-campo-data',
  imports: [Icone],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CampoData), multi: true }],
  template: `
    <input type="text" inputmode="numeric" placeholder="dd/mm/aaaa" maxlength="10" size="10" autocomplete="off"
      [value]="texto()" [disabled]="desabilitado()" [class.invalida]="invalida()" [attr.aria-label]="rotulo()"
      (input)="digitou($any($event.target))" (blur)="saiu()" />
    <input #nativo type="date" class="nativo" tabindex="-1" aria-hidden="true"
      [value]="iso()" [min]="min() ?? ''" [max]="max() ?? ''" (change)="escolheu($any($event.target).value)" />
    <button type="button" class="abrir" [disabled]="desabilitado()" (click)="abrirCalendario()" aria-label="Abrir calendário">
      <app-icone nome="calendar" />
    </button>
  `,
  styles: `
    :host { position: relative; display: inline-block; vertical-align: middle; }
    input[type='text'] { width: 100%; min-width: 9.5em; padding-right: 36px; box-sizing: border-box; font-variant-numeric: tabular-nums; }
    input.invalida { border-color: var(--cor-erro); outline-color: var(--cor-erro); }
    .nativo { position: absolute; right: 0; bottom: 0; width: 34px; height: 100%; opacity: 0; pointer-events: none; padding: 0; border: 0; }
    .abrir {
      position: absolute; right: 4px; top: 50%; transform: translateY(-50%);
      display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px;
      padding: 0; border: 0; border-radius: 6px; background: transparent; color: var(--cor-texto-suave); cursor: pointer;
    }
    .abrir:hover:not(:disabled) { color: var(--cor-primaria); }
    .abrir:disabled { cursor: default; opacity: 0.5; }
  `,
})
export class CampoData implements ControlValueAccessor {
  readonly min = input<string | null | undefined>();   // "AAAA-MM-DD"
  readonly max = input<string | null | undefined>();
  readonly rotulo = input<string | undefined>();

  protected readonly texto = signal('');
  protected readonly iso = signal('');
  protected readonly invalida = signal(false);
  protected readonly desabilitado = signal(false);
  private readonly nativo = viewChild.required<ElementRef<HTMLInputElement>>('nativo');

  private avisar: (v: string) => void = () => {};
  private tocou: () => void = () => {};

  writeValue(v: string | null | undefined): void {
    const iso = /^\d{4}-\d{2}-\d{2}/.test(v ?? '') ? v!.slice(0, 10) : '';
    this.iso.set(iso);
    this.texto.set(iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}` : '');
    this.invalida.set(false);
  }
  registerOnChange(fn: (v: string) => void): void { this.avisar = fn; }
  registerOnTouched(fn: () => void): void { this.tocou = fn; }
  setDisabledState(d: boolean): void { this.desabilitado.set(d); }

  protected digitou(el: HTMLInputElement): void {
    const n = el.value.replace(/\D/g, '').slice(0, 8);
    const txt = n.length > 4 ? `${n.slice(0, 2)}/${n.slice(2, 4)}/${n.slice(4)}`
      : n.length > 2 ? `${n.slice(0, 2)}/${n.slice(2)}` : n;
    el.value = txt;
    this.texto.set(txt);
    const iso = n.length === 8 ? CampoData.paraIso(n) : '';
    this.invalida.set(n.length === 8 && !iso);
    if (iso !== this.iso()) { this.iso.set(iso); this.avisar(iso); }
  }

  protected saiu(): void {
    // Saiu do campo com a data pela metade: marca em vermelho (o modelo já está vazio).
    const n = this.texto().replace(/\D/g, '');
    this.invalida.set(n.length > 0 && !this.iso());
    this.tocou();
  }

  protected escolheu(iso: string): void {
    this.writeValue(iso);
    this.avisar(this.iso());
    this.tocou();
  }

  protected abrirCalendario(): void {
    const el = this.nativo().nativeElement;
    try { el.showPicker(); } catch { el.focus(); }
  }

  // "ddmmaaaa" → "AAAA-MM-DD", ou '' se a data não existe (31/02, mês 13…).
  private static paraIso(n: string): string {
    const d = +n.slice(0, 2), m = +n.slice(2, 4), a = +n.slice(4, 8);
    const data = new Date(a, m - 1, d);
    if (a < 1900 || data.getFullYear() !== a || data.getMonth() !== m - 1 || data.getDate() !== d) return '';
    return `${n.slice(4, 8)}-${n.slice(2, 4)}-${n.slice(0, 2)}`;
  }
}

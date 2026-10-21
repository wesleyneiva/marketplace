import { Component, input, output } from '@angular/core';
import { Icone } from './icone';

// Anterior / Próxima + o número da página num campo: digita o número e Enter (ou sai do campo) para pular direto.
// Some sozinha quando há só 1 página. O texto extra ("· 120 venda(s)") vai dentro da tag.
// Uso:  <app-paginacao [pagina]="pagina()" [paginas]="paginas()" (irPara)="irPara($event)">· {{ total }} venda(s)</app-paginacao>
@Component({
  selector: 'app-paginacao',
  imports: [Icone],
  template: `
    @if (paginas() > 1) {
      <nav class="paginacao" aria-label="Paginação">
        <button type="button" class="botao pequeno" [disabled]="pagina() <= 1" (click)="ir(pagina() - 1)">
          <app-icone nome="arrow-left" /> Anterior
        </button>
        <span class="texto">
          Página
          <input #campo type="number" inputmode="numeric" min="1" [max]="paginas()" [value]="pagina()"
            aria-label="Número da página" title="Digite a página e aperte Enter"
            (keydown.enter)="ir(+campo.value, campo)" (blur)="ir(+campo.value, campo)" (focus)="campo.select()" />
          de {{ paginas() }} <ng-content />
        </span>
        <button type="button" class="botao pequeno" [disabled]="pagina() >= paginas()" (click)="ir(pagina() + 1)">
          Próxima <app-icone nome="chevron-right" />
        </button>
      </nav>
    }
  `,
  styles: `
    .paginacao {
      display: flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: 12px; margin-top: 14px;
      color: var(--cor-texto-suave); font-size: 14px;
    }
    .texto { display: inline-flex; align-items: center; gap: 6px; flex-wrap: wrap; justify-content: center; }
    input {
      width: 4.5em; padding: 4px 6px; text-align: center; font: inherit; font-variant-numeric: tabular-nums;
      color: var(--cor-texto); -moz-appearance: textfield;
    }
    input::-webkit-outer-spin-button, input::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
  `,
})
export class Paginacao {
  readonly pagina = input.required<number>();
  readonly paginas = input.required<number>();
  readonly irPara = output<number>();

  protected ir(p: number, campo?: HTMLInputElement): void {
    const destino = Number.isFinite(p) && p >= 1 ? Math.min(Math.trunc(p), this.paginas()) : this.pagina();
    if (campo) campo.value = String(destino); // número inválido ou fora da faixa: volta para o válido
    if (destino !== this.pagina()) this.irPara.emit(destino);
  }
}

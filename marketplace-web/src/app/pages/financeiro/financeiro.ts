import { Component, computed, input, numberAttribute } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Saldos } from './saldos';
import { Lancamentos } from './lancamentos';
import { PlanoContas } from './plano-contas';

type Aba = 'saldos' | 'lancamentos' | 'plano';

// Financeiro: onde o dinheiro está (saldos), cada entrada e saída (lançamentos) e o porquê (plano de contas).
// A aba e a conta ficam no ENDEREÇO (?aba=lancamentos&conta=3): dá para favoritar e o "voltar" do navegador funciona.
@Component({
  selector: 'app-financeiro',
  imports: [RouterLink, Saldos, Lancamentos, PlanoContas],
  template: `
    <header class="titulo-pagina">
      <div>
        <h1>🏦 Financeiro</h1>
        <p>Todo dinheiro que entra e sai: de qual conta e por quê.</p>
      </div>
    </header>

    <nav class="abas" role="tablist">
      <a role="tab" routerLink="." [queryParams]="{ aba: 'saldos' }" [class.ativa]="abaAtual() === 'saldos'">💰 Saldos</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'lancamentos' }" [class.ativa]="abaAtual() === 'lancamentos'">📒 Lançamentos</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'plano' }" [class.ativa]="abaAtual() === 'plano'">🗂️ Plano de contas</a>
    </nav>

    @switch (abaAtual()) {
      @case ('lancamentos') { <app-lancamentos [contaInicial]="conta() ?? null" /> }
      @case ('plano') { <app-plano-contas /> }
      @default { <app-saldos /> }
    }
  `,
  styles: `
    .abas {
      display: flex; gap: 4px; border-bottom: 1px solid var(--cor-borda); margin-bottom: 16px; overflow-x: auto;
      a {
        padding: 10px 14px; font-weight: 600; color: var(--cor-texto-suave); text-decoration: none; white-space: nowrap;
        border-bottom: 3px solid transparent;
        &.ativa { color: var(--cor-primaria-escura); border-bottom-color: var(--cor-primaria); }
      }
    }
  `,
})
export class Financeiro {
  // Vêm do endereço (withComponentInputBinding).
  readonly aba = input<string>();
  readonly conta = input(undefined, { transform: (v: unknown) => (v == null || v === '' ? undefined : numberAttribute(v)) });

  protected readonly abaAtual = computed<Aba>(() => {
    const a = this.aba();
    return a === 'lancamentos' || a === 'plano' ? a : 'saldos';
  });
}

import { Component, computed, input, numberAttribute } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Saldos } from './saldos';
import { Lancamentos } from './lancamentos';
import { PlanoContas } from './plano-contas';
import { AReceber } from './a-receber';
import { FluxoCaixa } from './fluxo-caixa';
import { DreAba } from './dre';
import { Icone } from '../../shared/icone';

type Aba = 'saldos' | 'lancamentos' | 'fluxo' | 'dre' | 'receber' | 'plano';

// Financeiro: onde o dinheiro está (saldos), cada entrada e saída (lançamentos) e o porquê (plano de contas).
// A aba e a conta ficam no ENDEREÇO (?aba=lancamentos&conta=3): dá para favoritar e o "voltar" do navegador funciona.
@Component({
  selector: 'app-financeiro',
  imports: [Icone, RouterLink, Saldos, Lancamentos, FluxoCaixa, DreAba, AReceber, PlanoContas],
  template: `
    <header class="titulo-pagina">
      <div>
        <h1><app-icone nome="landmark" /> Financeiro</h1>
        <p>Todo dinheiro que entra e sai: de qual conta e por quê.</p>
      </div>
    </header>

    <nav class="abas nao-imprimir" role="tablist">
      <a role="tab" routerLink="." [queryParams]="{ aba: 'saldos' }" [class.ativa]="abaAtual() === 'saldos'"><app-icone nome="circle-dollar-sign" /> Saldos</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'lancamentos' }" [class.ativa]="abaAtual() === 'lancamentos'"><app-icone nome="notebook-pen" /> Lançamentos</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'fluxo' }" [class.ativa]="abaAtual() === 'fluxo'"><app-icone nome="chart-line" /> Fluxo de caixa</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'dre' }" [class.ativa]="abaAtual() === 'dre'"><app-icone nome="chart-column" /> DRE</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'receber' }" [class.ativa]="abaAtual() === 'receber'"><app-icone nome="credit-card" /> A receber</a>
      <a role="tab" routerLink="." [queryParams]="{ aba: 'plano' }" [class.ativa]="abaAtual() === 'plano'"><app-icone nome="folder" /> Plano de contas</a>
    </nav>

    @switch (abaAtual()) {
      @case ('lancamentos') { <app-lancamentos [contaInicial]="conta() ?? null" /> }
      @case ('fluxo') { <app-fluxo-caixa /> }
      @case ('dre') { <app-dre /> }
      @case ('receber') { <app-a-receber /> }
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
    return a === 'lancamentos' || a === 'fluxo' || a === 'dre' || a === 'receber' || a === 'plano' ? a : 'saldos';
  });
}

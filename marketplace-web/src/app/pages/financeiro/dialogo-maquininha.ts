import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import {
  ConfiguracaoRecebimento, ContaFinanceira, ICONE_FORMA, NOME_FORMA, URL_FINANCEIRO, mensagemDeErro,
} from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

// O contrato da maquininha: taxa, prazo e onde cai o dinheiro de cada forma. Vale para as PRÓXIMAS vendas.
@Component({
  selector: 'app-dialogo-maquininha',
  imports: [FecharAoClicarFora, Icone, FormsModule, DiaPipe],
  styleUrl: './financeiro.scss',
  styles: `
    .janela { width: min(640px, calc(100vw - 32px)); }
    .forma { display: flex; flex-direction: column; gap: 8px; padding: 12px; border: 1px solid var(--cor-borda); border-radius: 10px; }
    .forma h3 { margin: 0; font-size: 15px; }
    .campos { display: grid; grid-template-columns: 110px 110px 1fr; gap: 10px; align-items: end; }
    .campos label { display: flex; flex-direction: column; gap: 4px; font-size: 13px; font-weight: 600; min-width: 0; }
    .uteis { flex-direction: row !important; align-items: center; gap: 6px !important; font-weight: 400 !important; }
    .exemplo { font-size: 12px; color: var(--cor-texto-suave); }
    @media (max-width: 560px) { .campos { grid-template-columns: 1fr 1fr; } .campos .conta { grid-column: 1 / -1; } }
  `,
  template: `
    <dialog #dlg fecharAoClicarFora class="janela" (close)="erro.set(null)">
      <form (ngSubmit)="salvar()" #f="ngForm">
        <header><h2><app-icone nome="settings" /> Maquininha e PIX</h2><button type="button" class="fechar" (click)="dlg.close()"><app-icone nome="x" /></button></header>
        <div class="corpo">
          <p class="dica">Copie do contrato da sua maquininha. Vale para as <b>próximas</b> vendas: as já feitas guardam a taxa do dia.</p>
          @for (c of linhas(); track c.forma) {
            <section class="forma">
              <h3><app-icone [nome]="iconeForma[c.forma]" /> {{ nomeForma[c.forma] }}</h3>
              <div class="campos">
                <label>Taxa (%)<input [name]="'taxa-' + c.forma" type="number" min="0" max="20" step="0.01" [(ngModel)]="c.taxaPercentual" required /></label>
                <label>Prazo (dias)<input [name]="'prazo-' + c.forma" type="number" min="0" max="120" step="1" [(ngModel)]="c.prazoDias" required /></label>
                <label class="conta">Cai em
                  <select [name]="'conta-' + c.forma" [(ngModel)]="c.contaFinanceiraId" required>
                    @for (cf of contas(); track cf.id) { <option [ngValue]="cf.id">{{ cf.nome }}</option> }
                  </select>
                </label>
              </div>
              @if (c.prazoDias > 0) {
                <label class="uteis"><input type="checkbox" [name]="'uteis-' + c.forma" [(ngModel)]="c.diasUteis" /> contar só dias úteis (ex.: "D+1 útil")</label>
              }
              <span class="exemplo">
                @if (c.prazoDias === 0) { Cai na hora, no fechamento do caixa. } @else { Hoje (com o contrato salvo): venda de hoje cai em {{ c.exemploHoje | dia }}. }
              </span>
            </section>
          }
          @if (erro()) { <div class="erro" role="alert">{{ erro() }}</div> }
        </div>
        <footer>
          <button type="button" class="botao" (click)="dlg.close()">Cancelar</button>
          <button type="submit" class="botao primario" [disabled]="f.invalid || salvando()">{{ salvando() ? 'Salvando…' : 'Salvar' }}</button>
        </footer>
      </form>
    </dialog>
  `,
})
export class DialogoMaquininha {
  private readonly http = inject(HttpClient);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  readonly salvo = output<string>();

  protected readonly nomeForma = NOME_FORMA;
  protected readonly iconeForma = ICONE_FORMA;
  protected readonly linhas = signal<ConfiguracaoRecebimento[]>([]);
  protected readonly contas = signal<ContaFinanceira[]>([]);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);

  async abrir(): Promise<void> {
    this.erro.set(null);
    const [config, contas] = await Promise.all([
      firstValueFrom(this.http.get<ConfiguracaoRecebimento[]>(`${URL_FINANCEIRO}/recebimento/configuracao`)),
      firstValueFrom(this.http.get<ContaFinanceira[]>(`${URL_FINANCEIRO}/contas`)),
    ]);
    this.linhas.set(config.map((c) => ({ ...c })));
    this.contas.set(contas.filter((c) => c.tipo !== 'Dinheiro')); // dinheiro da maquininha cai em banco (ou conta da maquininha)
    this.dlg().nativeElement.showModal();
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const pedido = this.linhas().map(({ forma, taxaPercentual, prazoDias, diasUteis, contaFinanceiraId }) =>
        ({ forma, taxaPercentual, prazoDias, diasUteis, contaFinanceiraId }));
      await firstValueFrom(this.http.put(`${URL_FINANCEIRO}/recebimento/configuracao`, pedido));
      this.dlg().nativeElement.close();
      this.salvo.emit('Contrato da maquininha salvo. Vale a partir das próximas vendas.');
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}

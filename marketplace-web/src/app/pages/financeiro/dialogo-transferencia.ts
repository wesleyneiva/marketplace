import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ContaFinanceira, ICONE_CONTA, URL_FINANCEIRO, hojeIso, mensagemDeErro } from '../../core/api/financeiro.api';

// Transferência entre contas (ex.: levar o dinheiro do cofre para o banco). Não é receita nem despesa.
@Component({
  selector: 'app-dialogo-transferencia',
  imports: [FormsModule, CurrencyPipe],
  styleUrl: './financeiro.scss',
  template: `
    <dialog #dlg class="janela" (close)="erro.set(null)">
      <form (ngSubmit)="salvar()" #f="ngForm">
        <header><h2>⇄ Transferência entre contas</h2><button type="button" class="fechar" (click)="dlg.close()">✕</button></header>
        <div class="corpo">
          <p class="dica">O dinheiro só muda de lugar: não é receita nem despesa (não mexe no resultado).</p>
          <div class="duas">
            <label>Sai de
              <select name="de" [(ngModel)]="form.deContaId" required>
                @for (c of contas(); track c.id) {
                  <option [ngValue]="c.id">{{ icone[c.tipo] }} {{ c.nome }} ({{ c.saldo | currency: 'BRL' }})</option>
                }
              </select>
            </label>
            <label>Entra em
              <select name="para" [(ngModel)]="form.paraContaId" required>
                @for (c of contas(); track c.id) {
                  <option [ngValue]="c.id" [disabled]="c.id === form.deContaId">{{ icone[c.tipo] }} {{ c.nome }}</option>
                }
              </select>
            </label>
          </div>
          <div class="duas">
            <label>Data<input name="data" type="date" [max]="hoje" [(ngModel)]="form.data" required /></label>
            <label>Valor (R$)<input #campoValor name="valor" type="number" min="0.01" step="0.01" [(ngModel)]="form.valor" required /></label>
          </div>
          <label>Descrição <small>(opcional)</small>
            <input name="descricao" [(ngModel)]="form.descricao" maxlength="200" placeholder="Ex.: Depósito do dinheiro do cofre" />
          </label>
          @if (erro()) { <div class="erro" role="alert">{{ erro() }}</div> }
        </div>
        <footer>
          <button type="button" class="botao" (click)="dlg.close()">Cancelar</button>
          <button type="submit" class="botao primario" [disabled]="f.invalid || salvando() || form.deContaId === form.paraContaId">
            {{ salvando() ? 'Transferindo…' : 'Transferir' }}
          </button>
        </footer>
      </form>
    </dialog>
  `,
})
export class DialogoTransferencia {
  private readonly http = inject(HttpClient);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campoValor = viewChild.required<ElementRef<HTMLInputElement>>('campoValor');

  readonly salvo = output<string>();

  protected readonly icone = ICONE_CONTA;
  protected readonly hoje = hojeIso();
  protected readonly contas = signal<ContaFinanceira[]>([]);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected form = { data: '', valor: null as number | null, deContaId: 0, paraContaId: 0, descricao: '' };

  // Abre já com as contas (e saldos) atualizadas; "de" opcional (ex.: clicou em transferir no cartão do cofre).
  async abrir(deContaId?: number): Promise<void> {
    this.erro.set(null);
    const contas = await firstValueFrom(this.http.get<ContaFinanceira[]>(`${URL_FINANCEIRO}/contas`));
    this.contas.set(contas);
    const de = contas.find((c) => c.id === deContaId) ?? contas[0];
    const para = contas.find((c) => c.id !== de?.id && c.tipo === 'Banco') ?? contas.find((c) => c.id !== de?.id);
    this.form = { data: this.hoje, valor: null, deContaId: de?.id ?? 0, paraContaId: para?.id ?? 0, descricao: '' };
    this.dlg().nativeElement.showModal();
    setTimeout(() => this.campoValor().nativeElement.focus()); // as contas já vêm sugeridas: falta o valor
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      await firstValueFrom(this.http.post(`${URL_FINANCEIRO}/transferencias`, { ...this.form, descricao: this.form.descricao || null }));
      const de = this.contas().find((c) => c.id === this.form.deContaId)?.nome;
      const para = this.contas().find((c) => c.id === this.form.paraContaId)?.nome;
      this.dlg().nativeElement.close();
      this.salvo.emit(`Transferência de ${this.dinheiro(this.form.valor ?? 0)}: ${de} → ${para}.`);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }

  private dinheiro(v: number): string {
    return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }
}

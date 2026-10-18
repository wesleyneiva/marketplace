import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ContaFinanceira, URL_FINANCEIRO, hojeIso, mensagemDeErro } from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';
import { CampoData } from '../../shared/campo-data';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

// Transferência entre contas (ex.: levar o dinheiro do cofre para o banco). Não é receita nem despesa.
@Component({
  selector: 'app-dialogo-transferencia',
  imports: [FecharAoClicarFora, Icone, FormsModule, CurrencyPipe, CampoData],
  styleUrl: './financeiro.scss',
  template: `
    <dialog #dlg fecharAoClicarFora class="janela" (close)="erro.set(null)">
      <form (ngSubmit)="salvar()" #f="ngForm">
        <header><h2><app-icone nome="arrow-left-right" /> Transferência entre contas</h2><button type="button" class="fechar" (click)="dlg.close()"><app-icone nome="x" /></button></header>
        <div class="corpo">
          <p class="dica">O dinheiro só muda de lugar: não é receita nem despesa (não mexe no resultado).</p>
          <div class="duas">
            <label>Sai de
              <select name="de" [(ngModel)]="form.deContaId" required>
                @for (c of contas(); track c.id) {
                  <option [ngValue]="c.id">{{ c.nome }} ({{ c.saldo | currency: 'BRL' }})</option>
                }
              </select>
            </label>
            <label>Entra em
              <select name="para" [(ngModel)]="form.paraContaId" required>
                @for (c of contas(); track c.id) {
                  <option [ngValue]="c.id" [disabled]="c.id === form.deContaId">{{ c.nome }}</option>
                }
              </select>
            </label>
          </div>
          <div class="duas">
            <label>Data<app-campo-data name="data" [max]="hoje" [(ngModel)]="form.data" required /></label>
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

  protected readonly hoje = hojeIso();
  protected readonly contas = signal<ContaFinanceira[]>([]);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected form = { data: '', valor: null as number | null, deContaId: 0, paraContaId: 0, descricao: '' };

  // Abre já com as contas (e saldos) atualizadas; "de" opcional (ex.: clicou em transferir no cartão do cofre).
  async abrir(deContaId?: number, paraContaId?: number, valor?: number): Promise<void> {
    this.erro.set(null);
    const contas = await firstValueFrom(this.http.get<ContaFinanceira[]>(`${URL_FINANCEIRO}/contas`));
    this.contas.set(contas);
    // Para onde: o pedido; senão o banco. De onde: o pedido; senão (se o destino veio) o cofre; senão a primeira.
    const para = contas.find((c) => c.id === paraContaId)
      ?? contas.find((c) => c.id !== deContaId && c.tipo === 'Banco') ?? contas.find((c) => c.id !== deContaId);
    const de = contas.find((c) => c.id === deContaId)
      ?? contas.find((c) => c.id !== para?.id && c.tipo === 'Dinheiro') ?? contas.find((c) => c.id !== para?.id);
    this.form = {
      data: this.hoje, valor: valor ? Math.round(valor * 100) / 100 : null, deContaId: de?.id ?? 0, paraContaId: para?.id ?? 0,
      descricao: paraContaId && valor ? 'Troco inicial do caixa (veio do cofre)' : '',
    };
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

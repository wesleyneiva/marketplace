import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Cliente, ClienteCadastro } from '../../core/api/clientes.api';
import { CampoData } from '../../shared/campo-data';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';
import { Icone } from '../../shared/icone';

// Cadastro / edição do cliente (usado na lista e na ficha). Na loja de esporte, também o que ele usa.
@Component({
  selector: 'app-cliente-form',
  imports: [FormsModule, CampoData, FecharAoClicarFora, Icone],
  template: `
    <dialog #dlg fecharAoClicarFora class="janela" (close)="erro.set(null)">
      <form (ngSubmit)="salvar()" #f="ngForm">
        <header><h2>{{ id ? 'Editar cliente' : 'Novo cliente' }}</h2><button type="button" class="fechar" (click)="dlg.close()" aria-label="Fechar"><app-icone nome="x" /></button></header>
        <div class="corpo">
          <div class="dupla">
            <label>Nome *<input name="nome" [(ngModel)]="form.nome" required minlength="2" maxlength="120" /></label>
            <label>Apelido <small>como a loja chama</small><input name="apelido" [(ngModel)]="form.apelido" maxlength="60" /></label>
          </div>
          <div class="dupla">
            <label>WhatsApp / telefone<input name="telefone" [(ngModel)]="form.telefone" maxlength="20" inputmode="tel" placeholder="51 99999-8888" /></label>
            <label>CPF <small>opcional</small><input name="cpf" [(ngModel)]="form.cpf" maxlength="14" inputmode="numeric" /></label>
          </div>
          <div class="dupla">
            <label>E-mail<input name="email" type="email" [(ngModel)]="form.email" maxlength="120" /></label>
            <label>Aniversário<app-campo-data name="nascimento" [(ngModel)]="form.nascimento" /></label>
          </div>
          <label>Endereço<input name="endereco" [(ngModel)]="form.endereco" maxlength="200" /></label>
          @if (ramo().servicos) {
            <fieldset>
              <legend>O que usa <small>(a oficina já sugere na próxima ordem)</small></legend>
              <div class="tres">
                <label>Raquete<input name="raquete" [(ngModel)]="form.raquete" maxlength="120" placeholder="Babolat Pure Aero" /></label>
                <label>Corda<input name="corda" [(ngModel)]="form.corda" maxlength="120" placeholder="RPM Blast 1.25" /></label>
                <label>Tensão<input name="tensao" [(ngModel)]="form.tensao" maxlength="20" placeholder="24/22 lb" /></label>
              </div>
            </fieldset>
          }
          <label>Observação<input name="obs" [(ngModel)]="form.observacao" maxlength="300" /></label>
          @if (erro()) { <div class="erro" role="alert">{{ erro() }}</div> }
        </div>
        <footer>
          <button type="button" class="botao" (click)="dlg.close()">Cancelar</button>
          <button type="submit" class="botao primario" [disabled]="f.invalid || salvando()">{{ salvando() ? 'Salvando…' : 'Salvar' }}</button>
        </footer>
      </form>
    </dialog>`,
  styleUrl: '../usuarios/usuarios.scss',
  styles: `
    .tres { display: grid; grid-template-columns: 1fr 1fr 110px; gap: 10px; }
    .tres label { display: flex; flex-direction: column; gap: 6px; font-weight: 600; font-size: 14px; }
    legend small { font-weight: 400; color: var(--cor-texto-suave); }
    @media (max-width: 640px) { .tres { grid-template-columns: 1fr; } }
  `,
})
export class ClienteForm {
  private readonly http = inject(HttpClient);
  protected readonly ramo = inject(AuthService).ramo;
  readonly salvo = output<Cliente>();
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected id: number | null = null;
  protected form: ClienteCadastro = ClienteForm.vazio();

  private static vazio(): ClienteCadastro {
    return { nome: '', apelido: null, telefone: null, cpf: null, email: null, endereco: null, nascimento: null, raquete: null, corda: null, tensao: null, observacao: null };
  }

  abrir(c?: Cliente): void {
    this.id = c?.id ?? null;
    this.form = c ? { nome: c.nome, apelido: c.apelido, telefone: c.telefone, cpf: c.cpf, email: c.email, endereco: c.endereco,
      nascimento: c.nascimento, raquete: c.raquete, corda: c.corda, tensao: c.tensao, observacao: c.observacao } : ClienteForm.vazio();
    this.erro.set(null);
    this.dlg().nativeElement.showModal();
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const corpo = { ...this.form, nascimento: this.form.nascimento || null };
      const c = await firstValueFrom(this.id
        ? this.http.put<Cliente>(`/api/clientes/${this.id}`, corpo)
        : this.http.post<Cliente>('/api/clientes', corpo));
      this.dlg().nativeElement.close();
      this.salvo.emit(c);
    } catch (e) {
      this.erro.set(e instanceof HttpErrorResponse
        ? e.error?.mensagem ?? Object.values<string[]>(e.error?.errors ?? {}).flat()[0] ?? 'Não foi possível salvar.'
        : 'Não foi possível salvar.');
    } finally {
      this.salvando.set(false);
    }
  }
}

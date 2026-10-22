import { Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ClienteBuscaPdv, ClientesApi } from '../core/api/clientes.api';
import { Icone } from './icone';

// CLIENTE da venda no caixa (F8): procura por nome, telefone ou CPF; não achou, cadastra na hora (nome + telefone
// ou CPF: o resto o gerente completa em Clientes). A venda entra no histórico dele.
@Component({
  selector: 'app-cliente-caixa',
  imports: [FormsModule, DatePipe, Icone],
  template: `
    <dialog #dlg class="janela-cliente" (close)="fechada.emit()">
      <header>
        <h2><app-icone nome="users" /> Cliente da venda</h2>
        <button type="button" class="fechar" (click)="fechar()" aria-label="Fechar"><app-icone nome="x" /></button>
      </header>
      <div class="corpo">
        @if (!cadastrando()) {
          <label for="buscaCliente">Nome, telefone ou CPF</label>
          <input #campoBusca id="buscaCliente" autocomplete="off" [ngModel]="termo" (ngModelChange)="digitar($event)"
                 (keydown.enter)="$event.preventDefault(); achados()[0] && escolher(achados()[0])" placeholder="Ex.: ana, 99881, 123.456…" />
          @if (procurando()) { <small class="dica">Procurando…</small> }
          @if (achados().length) {
            <ul class="achados">
              @for (c of achados(); track c.id) {
                <li><button type="button" (click)="escolher(c)">
                  <b>{{ c.nome }}</b>@if (c.apelido) { <small> · {{ c.apelido }}</small> }@if (c.telefoneFinal) { <small> · fone …{{ c.telefoneFinal }}</small> }
                  @if (c.aniversarioHoje) { <span class="selo"><app-icone nome="sparkles" /> aniversário hoje</span> }
                  <small class="linha">{{ c.compras }} compra(s)@if (c.ultimaCompra) { · última em {{ c.ultimaCompra | date: 'dd/MM/yyyy' }} }
                    @if (c.raquete) { · usa {{ c.raquete }}@if (c.tensao) { ({{ c.tensao }}) } }</small>
                </button></li>
              }
            </ul>
          } @else if (termo.trim().length >= 2 && !procurando()) {
            <p class="dica">Ninguém com "{{ termo }}".</p>
          }
          <button type="button" class="botao" (click)="novo()"><app-icone nome="plus" /> Cadastrar cliente novo</button>
        } @else {
          <form class="novo" (ngSubmit)="salvar()">
            <label>Nome <input #campoNome name="nome" [(ngModel)]="nome" maxlength="120" autocomplete="off" /></label>
            <label>Telefone (WhatsApp) <input name="telefone" [(ngModel)]="telefone" inputmode="tel" maxlength="20" placeholder="51 99999-8888" /></label>
            <label>CPF <small>opcional</small><input name="cpf" [(ngModel)]="cpf" inputmode="numeric" maxlength="14" /></label>
            <div class="botoes">
              <button type="button" class="botao" (click)="cadastrando.set(false)">Voltar</button>
              <button type="submit" class="botao primario" [disabled]="ocupado()">{{ ocupado() ? 'Salvando…' : 'Cadastrar e usar' }}</button>
            </div>
          </form>
        }
        @if (erro(); as e) { <p class="erro" role="alert">{{ e }}</p> }
      </div>
    </dialog>`,
  styles: `
    .janela-cliente { width: min(520px, calc(100vw - 32px)); max-height: calc(100dvh - 32px); padding: 0; border: 0; border-radius: 14px;
      box-shadow: 0 20px 60px rgb(0 0 0 / 0.25); color: var(--cor-texto); background: var(--cor-superficie); }
    .janela-cliente[open] { display: flex; flex-direction: column; }
    .janela-cliente::backdrop { background: rgb(10 20 15 / 0.5); }
    header { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 14px 18px; border-bottom: 1px solid var(--cor-borda); }
    h2 { margin: 0; font-size: 18px; display: flex; align-items: center; gap: 8px; }
    .fechar { border: 0; background: transparent; color: inherit; cursor: pointer; padding: 4px; }
    .corpo { padding: 16px 18px; display: flex; flex-direction: column; gap: 10px; overflow: auto; }
    label { display: flex; flex-direction: column; gap: 6px; font-weight: 600; font-size: 14px; }
    label small { font-weight: 400; color: var(--cor-texto-suave); }
    .dica { margin: 0; font-size: 13px; color: var(--cor-texto-suave); }
    .achados { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    .achados button { width: 100%; text-align: left; padding: 9px 12px; border: 1px solid var(--cor-borda); border-radius: 10px;
      background: var(--cor-superficie); color: inherit; cursor: pointer; font: inherit; display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 6px; }
    .achados button:hover { border-color: var(--cor-primaria); }
    .achados small { color: var(--cor-texto-suave); font-size: 13px; }
    .achados .linha { flex-basis: 100%; }
    .selo { font-size: 12px; font-weight: 600; padding: 1px 8px; border-radius: 99px; background: var(--cor-primaria-clara); color: var(--cor-primaria-escura);
      display: inline-flex; align-items: center; gap: 4px; }
    form { display: flex; flex-direction: column; gap: 10px; }
    .botoes { display: flex; justify-content: flex-end; gap: 8px; }
    .erro { margin: 0; color: var(--cor-erro); font-size: 14px; }
  `,
})
export class ClienteCaixa {
  private readonly api = inject(ClientesApi);
  readonly escolhido = output<ClienteBuscaPdv>();
  readonly fechada = output<void>();
  readonly semInternet = input(false);

  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campoBusca = viewChild<ElementRef<HTMLInputElement>>('campoBusca');
  private readonly campoNome = viewChild<ElementRef<HTMLInputElement>>('campoNome');

  protected readonly achados = signal<ClienteBuscaPdv[]>([]);
  protected readonly procurando = signal(false);
  protected readonly cadastrando = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected termo = '';
  protected nome = '';
  protected telefone = '';
  protected cpf = '';
  private espera?: ReturnType<typeof setTimeout>;

  abrir(): void {
    this.termo = '';
    this.achados.set([]);
    this.cadastrando.set(false);
    this.erro.set(null);
    this.dlg().nativeElement.showModal();
    setTimeout(() => this.campoBusca()?.nativeElement.focus());
  }

  fechar(): void {
    this.dlg().nativeElement.close();
  }

  protected digitar(texto: string): void {
    this.termo = texto;
    clearTimeout(this.espera);
    if (texto.trim().length < 2) { this.achados.set([]); return; }
    this.espera = setTimeout(async () => {
      this.procurando.set(true);
      try { this.achados.set(await this.api.buscar(texto.trim())); }
      catch { this.achados.set([]); this.erro.set('Sem conexão: não deu para procurar o cliente.'); }
      finally { this.procurando.set(false); }
    }, 300);
  }

  protected escolher(c: ClienteBuscaPdv): void {
    this.escolhido.emit(c);
    this.fechar();
  }

  protected novo(): void {
    // O que foi digitado na busca já vai para o campo certo (número → telefone; texto → nome).
    const digitos = this.termo.replace(/\D/g, '');
    this.nome = digitos.length >= 8 ? '' : this.termo.trim();
    this.telefone = digitos.length >= 10 ? digitos : '';
    this.cpf = '';
    this.erro.set(null);
    this.cadastrando.set(true);
    setTimeout(() => this.campoNome()?.nativeElement.focus());
  }

  protected async salvar(): Promise<void> {
    if (this.nome.trim().length < 2) { this.erro.set('Informe o nome.'); return; }
    if (!this.telefone.trim() && !this.cpf.trim()) { this.erro.set('Informe o telefone ou o CPF (é como a gente acha o cliente da próxima vez).'); return; }
    this.ocupado.set(true);
    this.erro.set(null);
    try {
      const c = await this.api.rapido({ nome: this.nome.trim(), telefone: this.telefone.trim() || null, cpf: this.cpf.trim() || null });
      this.escolher(c);
    } catch (e) {
      this.erro.set(e instanceof HttpErrorResponse && e.error?.mensagem ? e.error.mensagem : 'Não foi possível cadastrar.');
    } finally {
      this.ocupado.set(false);
    }
  }
}

import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Fornecedor, Pedido } from '../../core/api/compras.api';
import { AuthService } from '../../core/auth/auth.service';
import { copiarTexto } from '../../shared/copiar';
import { Icone } from '../../shared/icone';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

// Mandar o pedido de compra ao fornecedor: a mensagem sai pronta e a pessoa escolhe o canal do cadastro do fornecedor
// (WhatsApp ou e-mail, abertos no aparelho dela — sai do número/e-mail da própria loja) ou copia o texto.
// O pedido só passa a "Enviado" quando um canal é usado (ou ao marcar "já enviei por outro meio").
// Envio automático (sem clicar) fica para quando houver fornecedores reais: WhatsApp da loja pela API ou e-mail próprio.
@Component({
  selector: 'app-enviar-pedido',
  imports: [Icone, FormsModule, RouterLink, FecharAoClicarFora],
  template: `
    <dialog #dlg fecharAoClicarFora class="dialogo-enviar" (close)="aoFechar()">
      @if (pedido(); as p) {
        <header>
          <h2><app-icone nome="send" /> Enviar pedido #{{ p.id }} ao fornecedor</h2>
          <button type="button" class="fechar" (click)="fechar()" aria-label="Fechar"><app-icone nome="x" /></button>
        </header>
        <div class="corpo">
          <p class="para"><b>{{ p.fornecedor }}</b>
            @if (fornecedor(); as f) {
              @if (f.contato) { · {{ f.contato }} }
              @if (f.telefone) { · {{ f.telefone }} }
              @if (f.email) { · {{ f.email }} }
            }
          </p>
          @if (fornecedor() && !telefone() && !fornecedor()!.email) {
            <p class="aviso"><app-icone nome="circle-alert" /> Este fornecedor não tem WhatsApp nem e-mail no cadastro.
              <a routerLink="/fornecedores" (click)="fechar()">Cadastrar o contato</a> ou copie a mensagem.</p>
          }
          <label for="msg">Mensagem (dá para mudar antes de enviar)</label>
          <textarea id="msg" rows="12" [(ngModel)]="mensagem"></textarea>

          <div class="canais">
            <button type="button" class="botao primario" [disabled]="!telefone() || ocupado()" (click)="porWhatsApp()"
                    [title]="telefone() ? 'Abre o WhatsApp com a mensagem pronta' : 'Sem WhatsApp no cadastro do fornecedor'">
              <app-icone nome="message-circle" /> WhatsApp</button>
            <button type="button" class="botao" [disabled]="!fornecedor()?.email || ocupado()" (click)="porEmail()"
                    [title]="fornecedor()?.email ? 'Abre o e-mail com o pedido' : 'Sem e-mail no cadastro do fornecedor'">
              <app-icone nome="mail" /> E-mail</button>
            <button type="button" class="botao" [disabled]="ocupado()" (click)="copiar()">
              <app-icone [nome]="copiado() ? 'check' : 'copy'" /> {{ copiado() ? 'Copiada' : 'Copiar mensagem' }}</button>
          </div>
          @if (!jaEnviado()) {
            <p class="outro">
              <button type="button" class="link" [disabled]="ocupado()" (click)="marcarEnviado('por outro meio')">Já enviei por outro meio (telefone, em mãos…)</button>
            </p>
          }
          @if (erro(); as e) { <p class="msg falhou" role="alert">{{ e }}</p> }
          @if (ok(); as m) { <p class="msg ok" role="status"><app-icone nome="circle-check" /> {{ m }}</p> }
        </div>
      }
    </dialog>`,
  styles: `
    .dialogo-enviar { width: min(560px, calc(100vw - 32px)); padding: 0; border: 0; border-radius: 14px; background: var(--cor-superficie); color: var(--cor-texto); }
    header { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 16px 18px; border-bottom: 1px solid var(--cor-borda); }
    h2 { margin: 0; font-size: 17px; display: flex; align-items: center; gap: 8px; }
    .fechar { border: 0; background: transparent; color: inherit; cursor: pointer; padding: 4px; }
    .corpo { padding: 16px 18px 18px; display: flex; flex-direction: column; gap: 8px; }
    .para { margin: 0 0 4px; font-size: 14px; color: var(--cor-texto-suave); }
    .para b { color: var(--cor-texto); }
    label { font-weight: 600; font-size: 14px; }
    textarea { font: inherit; font-size: 14px; resize: vertical; }
    .canais { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 6px; }
    .outro { margin: 2px 0 0; font-size: 13px; }
    .link { border: 0; background: transparent; padding: 0; color: var(--cor-texto-suave); text-decoration: underline; cursor: pointer; font: inherit; }
    .aviso { margin: 0; padding: 8px 12px; border-radius: 8px; font-size: 14px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap;
      background: var(--cor-alerta-fundo); color: var(--cor-alerta); border: 1px solid var(--cor-alerta-borda); }
    .aviso a { color: inherit; font-weight: 700; }
    .msg { margin: 4px 0 0; font-size: 14px; display: flex; align-items: center; gap: 6px; }
    .msg.ok { color: var(--cor-primaria-escura); font-weight: 600; }
    .msg.falhou { color: var(--cor-erro); }
  `,
})
export class EnviarPedido {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  readonly enviado = output<Pedido>(); // o pedido passou a "Enviado" (a tela recarrega a lista)
  readonly desistiu = output<Pedido>(); // fechou sem mandar um pedido novo (a tela apaga o rascunho)

  protected readonly pedido = signal<Pedido | null>(null);
  protected readonly fornecedor = signal<Fornecedor | null>(null);
  protected readonly jaEnviado = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly copiado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly ok = signal<string | null>(null);
  protected mensagem = '';

  async abrir(p: Pedido): Promise<void> {
    this.pedido.set(p);
    this.jaEnviado.set(p.status !== 'Rascunho');
    this.fornecedor.set(null);
    this.copiado.set(false);
    this.erro.set(null);
    this.ok.set(null);
    this.mensagem = this.montarMensagem(p, null);
    this.dlg().nativeElement.showModal();
    try {
      const lista = await firstValueFrom(this.http.get<Fornecedor[]>('/api/fornecedores'));
      const f = lista.find((x) => x.id === p.fornecedorId) ?? null;
      this.fornecedor.set(f);
      this.mensagem = this.montarMensagem(p, f);
    } catch { /* sem a lista: só não sabe o contato; copiar continua valendo */ }
  }

  fechar(): void {
    this.dlg().nativeElement.close();
  }

  protected aoFechar(): void {
    const p = this.pedido();
    if (p && !this.jaEnviado()) this.desistiu.emit(p);
  }

  // Número para o wa.me: só dígitos, com o 55 do Brasil na frente.
  protected telefone(): string | null {
    const d = (this.fornecedor()?.telefone ?? '').replace(/\D/g, '');
    if (d.length < 10) return null;
    return d.startsWith('55') && d.length >= 12 ? d : `55${d}`;
  }

  protected porWhatsApp(): void {
    const fone = this.telefone();
    if (!fone) return;
    window.open(`https://wa.me/${fone}?text=${encodeURIComponent(this.mensagem)}`, '_blank', 'noopener'); // antes do await: o navegador só abre no clique
    void this.marcarEnviado('pelo WhatsApp');
  }

  protected porEmail(): void {
    const f = this.fornecedor();
    const p = this.pedido();
    if (!f?.email || !p) return;
    const assunto = `Pedido de compra nº ${p.id} — ${this.loja()}`;
    window.location.href = `mailto:${f.email}?subject=${encodeURIComponent(assunto)}&body=${encodeURIComponent(this.mensagem)}`;
    void this.marcarEnviado('por e-mail');
  }

  protected async copiar(): Promise<void> {
    this.copiado.set(await copiarTexto(this.mensagem));
  }

  protected async marcarEnviado(como: string): Promise<void> {
    const p = this.pedido();
    if (!p) return;
    if (this.jaEnviado()) { this.ok.set(`Pedido reenviado ${como}.`); return; }
    this.ocupado.set(true);
    this.erro.set(null);
    try {
      const r = await firstValueFrom(this.http.post<Pedido>(`/api/compras/pedidos/${p.id}/enviar`, {}));
      this.jaEnviado.set(true);
      this.pedido.set(r);
      this.ok.set(`Pedido marcado como enviado ${como}.` + (r.previsaoEntrega ? ` Entrega prevista em ${this.dia(r.previsaoEntrega)}.` : ''));
      this.enviado.emit(r);
    } catch (e) {
      this.erro.set(e instanceof HttpErrorResponse && e.error?.mensagem ? e.error.mensagem : 'Não foi possível marcar como enviado. Tente de novo.');
    } finally {
      this.ocupado.set(false);
    }
  }

  private loja(): string {
    return this.auth.usuario()?.empresa.nome ?? '';
  }

  private montarMensagem(p: Pedido, f: Fornecedor | null): string {
    const nome = (f?.contato ?? '').split(' ')[0];
    const linhas = p.itens.map((i) => `• ${this.qtd(i.quantidade, i.unidade)} ${i.unidade} — ${i.produto}`);
    // Sem data de entrega na mensagem: quem diz o prazo é o fornecedor (a loja ajusta a previsão no pedido depois).
    return [
      `Olá${nome ? `, ${nome}` : ''}! Segue o pedido de compra nº ${p.id} do ${this.loja()}:`,
      '',
      ...linhas,
      ...(p.observacao ? ['', `Observação: ${p.observacao}`] : []),
      '',
      'Por favor, confirme o pedido, os valores e a data de entrega. Obrigado!',
    ].join('\n');
  }

  private qtd(n: number, unidade: string): string {
    return n.toLocaleString('pt-BR', { maximumFractionDigits: unidade === 'KG' || unidade === 'L' ? 3 : 0 });
  }

  private dia(iso: string): string {
    const [, mes, dia] = iso.slice(0, 10).split('-');
    return `${dia}/${mes}`;
  }
}

import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Icone } from './icone';
import { copiarTexto } from './copiar';

// Formato igual a PixQrCodeResponse (Contracts/ProdutoDtos.cs).
export interface PixQrCode {
  valor: number;
  payload: string;   // o "Pix copia e cola"
  svg: string;       // o desenho do QR Code (gerado no servidor a partir da chave da loja)
  recebedor: string;
  chave: string;
  tipoChave: string;
}

// Chave como as pessoas leem: CNPJ 11.222.333/0001-81, CPF 123.456.789-09, celular (51) 99999-8888.
export function chavePixLegivel(tipo: string | null | undefined, chave: string | null | undefined): string {
  const c = chave ?? '';
  if (tipo === 'Cnpj' && c.length === 14) return c.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5');
  if (tipo === 'Cpf' && c.length === 11) return c.replace(/(\d{3})(\d{3})(\d{3})(\d{2})/, '$1.$2.$3-$4');
  if (tipo === 'Celular' && c.startsWith('+55')) {
    const d = c.slice(3);
    return d.length === 11 ? `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}` : `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
  }
  return c;
}

// QR Code do Pix com o valor (padrão do Banco Central): o cliente lê com o app do banco e o dinheiro cai direto na chave da loja.
// O valor pode mudar (pagamento dividido): espera 0,3 s parado e pede o QR novo.
@Component({
  selector: 'app-pix-qrcode',
  imports: [Icone, CurrencyPipe],
  template: `
    @if (qr(); as q) {
      <div class="pix" [class.velho]="carregando()">
        <div class="qr" [innerHTML]="svg()"></div>
        <div class="info">
          <span class="valor">{{ q.valor | currency: 'BRL' }}</span>
          <span class="para">para <b>{{ q.recebedor }}</b></span>
          <small class="chave">chave {{ legivel(q.tipoChave, q.chave) }}</small>
          <ng-content />
          <button type="button" class="botao pequeno" (click)="copiar(q.payload)"><app-icone nome="copy" /> {{ copiado() ? 'Copiado' : 'Copiar código (copia e cola)' }}</button>
        </div>
      </div>
    } @else if (erro()) {
      <p class="sem-chave" role="status"><app-icone nome="qr-code" /> {{ erro() }}</p>
    } @else if (carregando()) {
      <p class="sem-chave"><app-icone nome="loader-circle" girando /> Gerando o QR Code…</p>
    }
  `,
  styles: `
    .pix { display: flex; gap: 16px; align-items: center; padding: 12px; margin: 10px 0; border: 1px solid var(--cor-borda); border-radius: 12px; background: var(--cor-superficie); }
    .pix.velho .qr { opacity: 0.35; }
    .qr { flex: none; width: 190px; height: 190px; border-radius: 8px; overflow: hidden; background: #fff; transition: opacity 0.15s; }
    .qr ::ng-deep svg { display: block; width: 100%; height: 100%; }
    .info { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .valor { font-size: 26px; font-weight: 700; font-variant-numeric: tabular-nums; }
    .para { font-size: 14px; }
    .chave { color: var(--cor-texto-suave); overflow-wrap: anywhere; }
    .info .botao { align-self: flex-start; margin-top: 6px; }
    .sem-chave { margin: 10px 0; font-size: 13px; color: var(--cor-texto-suave); }
    @media (max-width: 480px) { .pix { flex-direction: column; align-items: flex-start; } }
  `,
})
export class PixQrCodeComponent {
  private readonly http = inject(HttpClient);
  private readonly sanitizer = inject(DomSanitizer);

  readonly valor = input.required<number>();

  protected readonly qr = signal<PixQrCode | null>(null);
  protected readonly svg = signal<SafeHtml>('');
  protected readonly erro = signal<string | null>(null);
  protected readonly carregando = signal(false);
  protected readonly copiado = signal(false);
  private espera?: ReturnType<typeof setTimeout>;
  private pedido = 0;

  constructor() {
    effect(() => {
      const v = Math.round(this.valor() * 100) / 100;
      untracked(() => {
        clearTimeout(this.espera);
        if (!(v > 0)) { this.qr.set(null); return; }
        if (this.qr()?.valor === v) return;
        this.carregando.set(true);
        this.espera = setTimeout(() => this.buscar(v), 300);
      });
    });
  }

  private async buscar(valor: number): Promise<void> {
    const meu = ++this.pedido;
    try {
      const q = await firstValueFrom(this.http.get<PixQrCode>('/api/empresa/configuracao/pix/qrcode', { params: { valor } }));
      if (meu !== this.pedido) return;
      // O SVG vem do próprio servidor (QRCoder, a partir da chave cadastrada): só quadradinhos, nenhum texto do usuário.
      this.svg.set(this.sanitizer.bypassSecurityTrustHtml(q.svg));
      this.qr.set(q);
      this.erro.set(null);
      this.copiado.set(false);
    } catch (e) {
      if (meu !== this.pedido) return;
      this.qr.set(null);
      this.erro.set((e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível gerar o QR Code do Pix.');
    } finally {
      if (meu === this.pedido) this.carregando.set(false);
    }
  }

  protected readonly legivel = chavePixLegivel;

  protected async copiar(texto: string): Promise<void> {
    this.copiado.set(await copiarTexto(texto));
  }
}

import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Icone } from './icone';
import { copiarTexto } from './copiar';

// Formato igual a PixController.CobrancaPixResponse.
export interface CobrancaPix {
  id: number;
  valor: number;
  status: 'Pendente' | 'Paga' | 'Expirada' | 'Cancelada';
  copiaECola: string;
  svg: string;
  expiraEm: string;
  pagaEm: string | null;
}

// Nome do provedor do Pix automático para o caixa ("MercadoPago" → "Mercado Pago"); igual a ProvedoresPix.Nome.
const NOMES_PIX: Record<string, string> = { MercadoPago: 'Mercado Pago', BancoDoBrasil: 'Banco do Brasil', Inter: 'Inter', Sicoob: 'Sicoob', Itau: 'Itaú',
  Bradesco: 'Bradesco', Santander: 'Santander', Sicredi: 'Sicredi', Efi: 'Efí' };
export function nomeProvedorPix(codigo: string | null | undefined): string {
  return (codigo && NOMES_PIX[codigo]) || 'banco';
}

// Pix AUTOMÁTICO (Mercado Pago ou o banco da loja): gera um QR único com o valor, espera o pagamento e avisa quem usa
// (o caixa) quando o provedor confirmar. Quem decide se está pago é o servidor (pergunta ao provedor).
@Component({
  selector: 'app-pix-automatico',
  imports: [Icone, CurrencyPipe],
  template: `
    @if (!cobranca()) {
      <div class="gerar">
        <button type="button" class="botao primario" (click)="gerar()" [disabled]="gerando() || !(valor() > 0)">
          @if (gerando()) { <app-icone nome="loader-circle" girando /> Gerando… } @else { <app-icone nome="qr-code" /> Gerar QR do Pix de {{ valor() | currency: 'BRL' }} }
        </button>
        <small>O caixa é avisado sozinho quando o cliente pagar ({{ nome() }}).</small>
      </div>
      @if (erro()) { <p class="erro-pix" role="alert">{{ erro() }}</p> }
    } @else if (cobranca(); as c) {
      <div class="pix" [class.pago]="c.status === 'Paga'">
        @if (c.status === 'Paga') {
          <div class="ok"><app-icone nome="circle-check" /></div>
        } @else {
          <div class="qr" [innerHTML]="svg()"></div>
        }
        <div class="info">
          <span class="valor">{{ c.valor | currency: 'BRL' }}</span>
          @switch (c.status) {
            @case ('Pendente') {
              <span class="esperando"><app-icone nome="loader-circle" girando /> Aguardando o pagamento…</span>
              <small>O QR vale por {{ restante() }} · o caixa é avisado sozinho quando cair.</small>
              <div class="acoes">
                <button type="button" class="botao pequeno" (click)="copiar(c.copiaECola)"><app-icone nome="copy" /> {{ copiado() ? 'Copiado' : 'Copia e cola' }}</button>
                <button type="button" class="botao pequeno" (click)="cancelar()">Cancelar este Pix</button>
              </div>
            }
            @case ('Paga') { <span class="recebido">Pix recebido</span><small>Confirmado pelo {{ nome() }}.</small> }
            @case ('Expirada') { <span class="ruim">O QR expirou sem pagamento.</span><button type="button" class="botao pequeno" (click)="recomecar()">Gerar outro</button> }
            @case ('Cancelada') { <span class="ruim">Pix cancelado.</span><button type="button" class="botao pequeno" (click)="recomecar()">Gerar outro</button> }
          }
          @if (erro()) { <small class="erro-pix" role="alert">{{ erro() }}</small> }
        </div>
      </div>
    }
  `,
  styles: `
    .gerar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin: 10px 0; small { color: var(--cor-texto-suave); } }
    .pix { display: flex; gap: 16px; align-items: center; padding: 12px; margin: 10px 0; border: 1px solid var(--cor-borda); border-radius: 12px; background: var(--cor-superficie); }
    .pix.pago { border-color: var(--cor-primaria); background: var(--cor-verde-fundo); }
    .qr { flex: none; width: 190px; height: 190px; border-radius: 8px; overflow: hidden; background: #fff; }
    .qr ::ng-deep svg { display: block; width: 100%; height: 100%; }
    .ok { flex: none; width: 72px; height: 72px; color: var(--cor-primaria); }
    .info { display: flex; flex-direction: column; gap: 4px; min-width: 0; small { color: var(--cor-texto-suave); } }
    .valor { font-size: 26px; font-weight: 700; font-variant-numeric: tabular-nums; }
    .esperando { font-weight: 600; color: var(--cor-info); }
    .recebido { font-size: 18px; font-weight: 700; color: var(--cor-primaria-escura); }
    .ruim { font-weight: 600; color: var(--cor-erro); }
    .acoes { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 6px; }
    .erro-pix { margin: 6px 0 0; color: var(--cor-erro); font-size: 13px; }
    @media (max-width: 480px) { .pix { flex-direction: column; align-items: flex-start; } }
  `,
})
export class PixAutomatico {
  private readonly http = inject(HttpClient);
  private readonly sanitizer = inject(DomSanitizer);

  readonly valor = input.required<number>();
  readonly sessaoCaixaId = input<number | null>(null);
  readonly provedor = input<string | null>(null); // "MercadoPago", "BancoDoBrasil", "Inter", "Sicoob", "Itau", "Bradesco", "Santander", "Sicredi" ou "Efi"
  protected readonly nome = computed(() => nomeProvedorPix(this.provedor()));
  readonly pago = output<CobrancaPix>();

  protected readonly cobranca = signal<CobrancaPix | null>(null);
  protected readonly svg = signal<SafeHtml>('');
  protected readonly gerando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly copiado = signal(false);
  private readonly agora = signal(Date.now());
  private relogio?: ReturnType<typeof setInterval>;
  private consulta?: ReturnType<typeof setTimeout>;

  protected readonly restante = computed(() => {
    const c = this.cobranca();
    if (!c) return '';
    const s = Math.max(0, Math.round((new Date(c.expiraEm).getTime() - this.agora()) / 1000));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.parar());
  }

  protected async gerar(): Promise<void> {
    this.erro.set(null);
    this.gerando.set(true);
    try {
      const c = await firstValueFrom(this.http.post<CobrancaPix>('/api/pix/cobrancas', { valor: this.valor(), sessaoCaixaId: this.sessaoCaixaId() }));
      // O SVG vem do próprio servidor (QRCoder a partir do código do provedor): só quadradinhos.
      this.svg.set(this.sanitizer.bypassSecurityTrustHtml(c.svg));
      this.cobranca.set(c);
      this.copiado.set(false);
      this.relogio = setInterval(() => this.agora.set(Date.now()), 1000);
      this.agendar();
    } catch (e) {
      this.erro.set(mensagem(e, `Não foi possível gerar o Pix no ${this.nome()}.`));
    } finally {
      this.gerando.set(false);
    }
  }

  // Pergunta a cada 2,5 s (o servidor só vai ao provedor a cada 3 s; o aviso do Mercado Pago adianta).
  private agendar(): void {
    clearTimeout(this.consulta);
    this.consulta = setTimeout(() => void this.consultar(), 2500);
  }

  private async consultar(): Promise<void> {
    const c = this.cobranca();
    if (!c || c.status !== 'Pendente') return;
    try {
      const s = await firstValueFrom(this.http.get<CobrancaPix>(`/api/pix/cobrancas/${c.id}`));
      if (this.cobranca()?.id !== c.id) return;
      this.cobranca.set({ ...c, status: s.status, pagaEm: s.pagaEm });
      this.erro.set(null);
      if (s.status === 'Paga') { this.parar(); this.pago.emit({ ...c, status: 'Paga', pagaEm: s.pagaEm }); return; }
      if (s.status !== 'Pendente') { this.parar(); return; }
    } catch (e) {
      this.erro.set(mensagem(e, 'Sem resposta do servidor: tentando de novo…'));
    }
    this.agendar();
  }

  protected async cancelar(): Promise<void> {
    const c = this.cobranca();
    if (!c) return;
    try {
      const s = await firstValueFrom(this.http.post<CobrancaPix>(`/api/pix/cobrancas/${c.id}/cancelar`, null));
      this.cobranca.set({ ...c, status: s.status });
      this.parar();
    } catch (e) {
      this.erro.set(mensagem(e, 'Não foi possível cancelar.'));
      void this.consultar(); // pode ter sido pago bem agora
    }
  }

  protected recomecar(): void {
    this.parar();
    this.cobranca.set(null);
    this.erro.set(null);
  }

  protected async copiar(texto: string): Promise<void> {
    this.copiado.set(await copiarTexto(texto));
  }

  private parar(): void {
    clearTimeout(this.consulta);
    clearInterval(this.relogio);
  }
}

function mensagem(e: unknown, padrao: string): string {
  return (e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? padrao;
}

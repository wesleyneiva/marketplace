import { Component, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Cupom, EstornoPrevia, FORMAS_PAGAMENTO, OpcaoPixEstorno, PdvApi } from '../core/api/pdv.api';
import { AutorizacaoGerente } from './autorizacao-gerente';
import { Icone } from './icone';

// Cancelar uma venda JÁ PAGA e devolver o dinheiro ao cliente (estorno). Mostra, antes de confirmar, por onde volta
// cada forma de pagamento (gaveta, Mercado Pago, maquininha, app do banco, cofre) — a mesma conta que o servidor faz.
// Usado no caixa (com a senha do gerente) e na tela Vendas (o gerente cancela direto).
@Component({
  selector: 'app-estorno-venda',
  imports: [CurrencyPipe, Icone, AutorizacaoGerente],
  template: `
    @if (previa(); as p) {
      <div class="bloco">
        <h3><app-icone nome="undo-2" /> Como o dinheiro volta para o cliente</h3>
        <ul class="formas">
          @for (f of p.formas; track $index) {
            <li>
              <span class="forma">{{ rotulo(f.forma) }}</span>
              <b>{{ f.valor | currency: 'BRL' }}</b>
              <small>{{ f.pixAutomatico ? comoPix(f.como) : f.como }}</small>
            </li>
          }
        </ul>
        @if (temPixAutomatico()) {
          <fieldset class="opcoes-pix">
            <legend>Pix confirmado pelo Mercado Pago</legend>
            <label><input type="radio" name="pix" [checked]="pix() === 'Devolver'" (change)="pix.set('Devolver')" /> Devolver ao cliente pelo Mercado Pago agora</label>
            @if (p.caixaAberto) {
              <label><input type="radio" name="pix" [checked]="pix() === 'Manter'" (change)="pix.set('Manter')" /> Não devolver: usar o Pix numa nova compra (troca de produto)</label>
            }
            <label><input type="radio" name="pix" [checked]="pix() === 'PorFora'" (change)="pix.set('PorFora')" /> A loja devolve por conta própria</label>
          </fieldset>
        }
        @if (p.aviso) { <p class="aviso-estorno"><app-icone nome="circle-alert" /> {{ p.aviso }}</p> }
        <p class="estoque"><app-icone nome="package-plus" /> Os produtos voltam para o estoque (no mesmo lote de validade).</p>
      </div>

      @if (p.bloqueio) {
        <div class="erro" role="alert"><app-icone nome="ban" /> {{ p.bloqueio }}</div>
      } @else {
        <form class="confirmar" (submit)="$event.preventDefault(); confirmar()">
          <app-autorizacao-gerente #autorizacao [precisa]="p.precisaAutorizacao" [(motivo)]="motivo"
            [sugestoes]="['Cliente desistiu', 'Produto com defeito', 'Cobrado errado', 'Troca de produto']" />
          <div class="acao">
            @if (erro()) { <div class="erro" role="alert"><app-icone nome="triangle-alert" /> {{ erro() }}</div> }
            <button type="submit" class="botao perigo-forte" [disabled]="enviando()">
              @if (enviando()) { <app-icone nome="loader-circle" girando /> Cancelando… } @else { <app-icone nome="undo-2" /> Cancelar venda e estornar {{ cupom()?.total | currency: 'BRL' }} }
            </button>
          </div>
        </form>
      }
    } @else if (erroPrevia()) {
      <div class="erro" role="alert">{{ erroPrevia() }}</div>
    } @else {
      <p class="carregando"><app-icone nome="loader-circle" girando /> Conferindo a venda…</p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 12px; }
    .bloco { display: flex; flex-direction: column; gap: 8px; }
    h3 { margin: 0; font-size: 15px; display: flex; align-items: center; gap: 6px; }
    .formas { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    .formas li {
      display: grid; grid-template-columns: 1fr auto; gap: 2px 10px; padding: 8px 10px;
      border: 1px solid var(--cor-borda); border-radius: 8px; background: var(--cor-fundo);
    }
    .formas .forma { font-weight: 600; }
    .formas b { font-variant-numeric: tabular-nums; }
    .formas small { grid-column: 1 / -1; color: var(--cor-texto-suave); font-size: 13px; }
    .opcoes-pix { margin: 0; padding: 8px 12px 10px; border: 1px solid var(--cor-borda); border-radius: 10px; display: flex; flex-direction: column; gap: 6px; }
    .opcoes-pix legend { padding: 0 4px; font-weight: 600; font-size: 13px; color: var(--cor-texto-suave); }
    .opcoes-pix label { display: flex; align-items: center; gap: 8px; font-size: 14px; font-weight: 400; cursor: pointer; }
    .aviso-estorno { margin: 0; padding: 8px 10px; border-radius: 8px; background: var(--cor-info-fundo); color: var(--cor-info); font-size: 13px; display: flex; gap: 6px; align-items: center; }
    .estoque { margin: 0; font-size: 13px; color: var(--cor-texto-suave); display: flex; gap: 6px; align-items: center; }
    .confirmar { display: flex; flex-direction: column; gap: 12px; }
    .acao { display: flex; flex-direction: column; align-items: flex-end; gap: 8px; }
    .acao .erro { align-self: stretch; }
    .erro { padding: 10px 12px; border-radius: 10px; background: var(--cor-erro-fundo); color: var(--cor-erro); font-weight: 600; display: flex; gap: 6px; align-items: center; }
    .carregando { margin: 0; color: var(--cor-texto-suave); display: flex; gap: 6px; align-items: center; }
    .perigo-forte { background: var(--cor-erro); border-color: var(--cor-erro); color: #fff; font-weight: 600; }
    .perigo-forte:hover:not(:disabled) { filter: brightness(0.92); border-color: var(--cor-erro); }
  `,
})
export class EstornoVenda {
  private readonly api = inject(PdvApi);

  readonly cupom = input<Cupom | null>(null);
  readonly cancelada = output<Cupom>();

  protected readonly previa = signal<EstornoPrevia | null>(null);
  protected readonly erroPrevia = signal<string | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly enviando = signal(false);
  protected readonly pix = signal<OpcaoPixEstorno>('Devolver');
  protected readonly motivo = signal('');
  protected readonly temPixAutomatico = computed(() => !!this.previa()?.formas.some((f) => f.pixAutomatico && f.como !== 'Já devolvido pelo Mercado Pago'));
  private readonly autorizacao = viewChild<AutorizacaoGerente>('autorizacao');

  constructor() {
    // Trocou a venda: confere de novo (o servidor diz se pode e por onde volta o dinheiro).
    effect(() => {
      const c = this.cupom();
      this.previa.set(null);
      this.erro.set(null);
      this.erroPrevia.set(null);
      this.pix.set('Devolver');
      this.motivo.set('');
      if (c) void this.carregar(c.id);
    });
  }

  private async carregar(id: number): Promise<void> {
    try {
      this.previa.set(await this.api.previaEstorno(id));
      this.autorizacao()?.focar();
      setTimeout(() => this.autorizacao()?.focar());
    } catch (e) {
      this.erroPrevia.set(mensagem(e));
    }
  }

  protected async confirmar(): Promise<void> {
    const c = this.cupom(), aut = this.autorizacao();
    if (!c || !aut || this.enviando()) return;
    const falta = aut.falta();
    if (falta) { this.erro.set(falta); return; }
    this.enviando.set(true);
    this.erro.set(null);
    try {
      const cancelado = await this.api.cancelarVenda(c.id, this.motivo().trim(), aut.autorizacao(), this.temPixAutomatico() ? this.pix() : null);
      this.cancelada.emit(cancelado);
    } catch (e) {
      this.erro.set(mensagem(e));
      if (e instanceof HttpErrorResponse && e.error?.autorizacao) aut.senhaErrada();
    } finally {
      this.enviando.set(false);
    }
  }

  protected rotulo(forma: string): string {
    return FORMAS_PAGAMENTO.find((f) => f.valor === forma)?.rotulo ?? forma;
  }

  // O texto do Pix automático acompanha a opção escolhida (o servidor manda o da opção padrão: devolver).
  protected comoPix(padrao: string): string {
    if (padrao === 'Já devolvido pelo Mercado Pago') return padrao;
    return this.pix() === 'Manter' ? 'Fica como "Pix recebido sem venda" para usar numa nova compra'
      : this.pix() === 'PorFora' ? 'A loja devolve ao cliente por conta própria'
      : padrao;
  }
}

function mensagem(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.error?.mensagem) return e.error.mensagem;
    if (e.error?.errors) return Object.values<string[]>(e.error.errors).flat().join(' ');
    if (e.status === 0) return 'Sem conexão com o servidor.';
    if (e.status === 403) return 'Sem permissão para cancelar esta venda.';
  }
  return 'Não foi possível concluir. Tente de novo.';
}

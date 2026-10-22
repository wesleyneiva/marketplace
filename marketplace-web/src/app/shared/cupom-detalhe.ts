import { Component, input } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { Cupom, FORMAS_PAGAMENTO } from '../core/api/pdv.api';
import { QuantidadePipe } from './quantidade.pipe';
import { Icone } from './icone';

// O cupom completo de uma venda na tela (itens, pagamentos, troco e, se cancelada, quem cancelou e quem autorizou).
@Component({
  selector: 'app-cupom-detalhe',
  imports: [CurrencyPipe, DatePipe, QuantidadePipe, Icone],
  template: `
    @let c = cupom();
    <div class="topo">
      <div>
        <b>Venda #{{ c.id }}</b>
        <small>{{ c.dataHora | date: 'dd/MM/yyyy HH:mm' }} · Caixa {{ c.numeroCaixa }} · {{ c.operador }}@if (c.vendedor) { · vendedor {{ c.vendedor }} }@if (c.cliente) { · cliente {{ c.cliente.nome }} }</small>
      </div>
      @if (c.status === 'Cancelada') { <span class="selo cancelada"><app-icone nome="ban" /> Cancelada</span> }
      @else { <span class="selo concluida"><app-icone nome="check" /> Concluída</span> }
    </div>

    @if (c.status === 'Cancelada') {
      <div class="cancelamento">
        <b><app-icone nome="undo-2" /> Venda cancelada e estornada</b>
        <span>
          @if (c.cancelamento?.em) { {{ c.cancelamento!.em | date: 'dd/MM HH:mm' }} · }
          @if (c.cancelamento?.por) { por {{ c.cancelamento!.por }} }
          @if (c.cancelamento?.autorizadoPor && c.cancelamento!.autorizadoPor !== c.cancelamento!.por) { · autorizado por <b>{{ c.cancelamento!.autorizadoPor }}</b> }
        </span>
        @if (c.motivoCancelamento) { <span>Motivo: {{ c.motivoCancelamento }}</span> }
      </div>
    }

    <table class="itens">
      @for (i of c.itens; track $index) {
        <tr>
          <td>{{ i.descricao }}<small>{{ i.quantidade | quantidade: i.unidade }} × {{ i.precoUnitario | currency: 'BRL' }}</small></td>
          <td class="num">{{ i.total | currency: 'BRL' }}</td>
        </tr>
      }
    </table>

    <div class="totais">
      <div><span>{{ c.itens.length }} {{ c.itens.length === 1 ? 'item' : 'itens' }} · subtotal</span><span>{{ c.subtotal | currency: 'BRL' }}</span></div>
      @if (c.desconto > 0) { <div><span>Desconto</span><span>−{{ c.desconto | currency: 'BRL' }}</span></div> }
      <div class="grande"><span>Total</span><span>{{ c.total | currency: 'BRL' }}</span></div>
      @for (p of c.pagamentos; track $index) {
        <div>
          <span>{{ rotulo(p.forma) }}@if (p.valeCodigo) { {{ p.valeCodigo }} }@if (p.parcelas && p.parcelas > 1) { em {{ p.parcelas }}x }@if (p.pixConfirmado) { <small class="selo pix"><app-icone nome="circle-check" /> confirmado pelo Mercado Pago</small> }</span>
          <span>{{ p.valor | currency: 'BRL' }}</span>
        </div>
        @if (p.valeCodigo && p.valeSaldo) {
          <div><span>Saldo no vale (até {{ p.valeValidade?.split('-')?.reverse()?.join('/') }})</span><span>{{ p.valeSaldo | currency: 'BRL' }}</span></div>
        }
      }
      @if (c.troco > 0) { <div class="forte"><span>Troco</span><span>{{ c.troco | currency: 'BRL' }}</span></div> }
    </div>
    @if (c.cliente; as cli) { <p class="cliente"><app-icone nome="notebook-pen" /> Cliente: {{ cli.nome }}</p> }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 10px; font-size: 14px; }
    .topo { display: flex; justify-content: space-between; align-items: flex-start; gap: 10px; }
    .topo b { font-size: 16px; }
    .topo small { display: block; color: var(--cor-texto-suave); font-size: 13px; }
    .selo.cancelada { background: var(--cor-erro-fundo-forte); color: var(--cor-erro); }
    .selo.concluida { background: var(--cor-primaria-clara); color: var(--cor-primaria-escura); }
    .selo.pix { margin-left: 6px; background: var(--cor-primaria-clara); color: var(--cor-primaria-escura); font-weight: 600; }
    .cancelamento {
      display: flex; flex-direction: column; gap: 2px; padding: 8px 10px; border-radius: 8px;
      background: var(--cor-erro-fundo); border: 1px solid var(--cor-erro-borda); font-size: 13px;
    }
    .cancelamento > b { color: var(--cor-erro); display: flex; gap: 6px; align-items: center; }
    .itens { width: 100%; border-collapse: collapse; }
    .itens td { padding: 6px 0; border-bottom: 1px dashed var(--cor-borda); vertical-align: top; }
    .itens small { display: block; color: var(--cor-texto-suave); font-size: 12px; }
    .num { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
    .totais { display: flex; flex-direction: column; gap: 4px; }
    .totais > div { display: flex; justify-content: space-between; gap: 10px; font-variant-numeric: tabular-nums; }
    .totais .grande { font-size: 17px; font-weight: 700; padding: 4px 0; }
    .totais .forte { font-weight: 700; }
    .cliente { margin: 0; color: var(--cor-texto-suave); display: flex; gap: 6px; align-items: center; }
  `,
})
export class CupomDetalhe {
  readonly cupom = input.required<Cupom>();

  protected rotulo(forma: string): string {
    return FORMAS_PAGAMENTO.find((f) => f.valor === forma)?.rotulo ?? forma;
  }
}

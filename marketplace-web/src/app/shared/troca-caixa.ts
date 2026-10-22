import { Component, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Produto } from '../core/api/produtos.api';
import { MOTIVOS_TROCA, Troca, TrocasApi, VendaComProduto, VendaParaTroca } from '../core/api/trocas.api';
import { ImpressoraService } from '../core/equipamentos/impressora.service';
import { AutorizacaoGerente } from './autorizacao-gerente';
import { Icone } from './icone';
import { ProdutoBusca } from './produto-busca';
import { QuantidadePipe } from './quantidade.pipe';

// TROCA no caixa (F7): acha a venda (número do cupom, ou bipando o produto que voltou), marca o que o cliente devolve
// e o motivo. Os itens voltam ao estoque e o valor vira um VALE-TROCA: usa agora no produto novo (o caixa já entra com
// o crédito) ou imprime o vale para o cliente usar depois. Fora do prazo da loja, só com a senha de um gerente.
@Component({
  selector: 'app-troca-caixa',
  imports: [FormsModule, CurrencyPipe, DatePipe, Icone, ProdutoBusca, QuantidadePipe, AutorizacaoGerente],
  template: `
    <dialog #dlg class="janela-troca" (close)="aoFechar()">
      <header>
        <h2><app-icone nome="repeat" /> Troca</h2>
        <button type="button" class="fechar" (click)="fechar()" aria-label="Fechar"><app-icone nome="x" /></button>
      </header>

      @switch (etapa()) {
        @case ('buscar') {
          <div class="corpo">
            <form class="linha-busca" (ngSubmit)="buscarVenda()">
              <label for="numeroVenda">Número da venda <small>(está no cupom: "Venda nº …")</small></label>
              <div class="campo-botao">
                <input #campoNumero id="numeroVenda" name="numeroVenda" inputmode="numeric" autocomplete="off" [(ngModel)]="numero" placeholder="Ex.: 1234" />
                <button type="submit" class="botao primario" [disabled]="ocupado()">Buscar</button>
              </div>
            </form>
            <div class="ou">ou, sem o cupom, procure o produto que o cliente está devolvendo</div>
            <app-produto-busca [flutuante]="false" placeholder="Nome ou código de barras do produto devolvido…" (selecionado)="buscarPorProduto($event)" />
            @if (produtoBuscado(); as p) {
              @if (vendasComProduto().length) {
                <p class="dica">Vendas recentes com <b>{{ p }}</b> (a mais nova primeiro):</p>
                <ul class="vendas">
                  @for (v of vendasComProduto(); track v.vendaId) {
                    <li><button type="button" (click)="abrirVenda(v.vendaId)">
                      <b>Venda #{{ v.vendaId }}</b> <span>{{ v.dataHora | date: 'dd/MM HH:mm' }} · Caixa {{ v.numeroCaixa }} · {{ v.total | currency: 'BRL' }}</span>
                      @if (!v.dentroDoPrazo) { <span class="selo alerta">fora do prazo</span> }
                      <small>{{ v.itens }}</small>
                    </button></li>
                  }
                </ul>
              } @else if (!ocupado()) {
                <p class="dica">Nenhuma venda recente com <b>{{ p }}</b>. Peça o cupom ou confira o produto.</p>
              }
            }
            @if (erro(); as e) { <p class="erro" role="alert">{{ e }}</p> }
          </div>
        }

        @case ('itens') {
          @if (venda(); as v) {
            <div class="corpo">
              <div class="venda-resumo">
                <button type="button" class="botao pequeno" (click)="voltarBusca()"><app-icone nome="arrow-left" /> Outra venda</button>
                <span><b>Venda #{{ v.vendaId }}</b> · {{ v.dataHora | date: 'dd/MM/yyyy HH:mm' }} · Caixa {{ v.numeroCaixa }} · {{ v.total | currency: 'BRL' }}</span>
                @if (v.dentroDoPrazo) {
                  <span class="selo ok"><app-icone nome="check" /> {{ v.dias === 0 ? 'hoje' : 'há ' + v.dias + (v.dias === 1 ? ' dia' : ' dias') }} · prazo de {{ v.prazoDias }} dias</span>
                } @else {
                  <span class="selo alerta"><app-icone nome="triangle-alert" /> há {{ v.dias }} dias: passou do prazo de {{ v.prazoDias }}</span>
                }
              </div>

              @if (v.bloqueio) {
                <p class="erro" role="alert">{{ v.bloqueio }}</p>
              } @else {
                <div class="rolagem">
                  <table>
                    <thead><tr><th></th><th>Produto</th><th class="num">Vendido</th><th class="num">Devolve</th><th class="num">Crédito</th></tr></thead>
                    <tbody>
                      @for (i of v.itens; track i.itemVendaId) {
                        <tr [class.apagado]="i.disponivel <= 0">
                          <td><input type="checkbox" [checked]="(qtd()[i.itemVendaId] ?? 0) > 0" [disabled]="i.disponivel <= 0"
                                     (change)="marcar(i.itemVendaId, $any($event.target).checked ? i.disponivel : 0)" [attr.aria-label]="'Devolver ' + i.descricao" /></td>
                          <td>{{ i.descricao }}@if (i.jaDevolvido > 0) { <small>já trocado: {{ i.jaDevolvido | quantidade: i.unidade }}</small> }</td>
                          <td class="num">{{ i.quantidade | quantidade: i.unidade }}</td>
                          <td class="num">
                            @if (i.disponivel > 0) {
                              <input class="qtd" type="number" min="0" [max]="i.disponivel" [step]="i.unidade === 'KG' || i.unidade === 'L' ? 0.001 : 1"
                                     [ngModel]="qtd()[i.itemVendaId] ?? 0" (ngModelChange)="marcar(i.itemVendaId, $event)" [attr.aria-label]="'Quantidade de ' + i.descricao" />
                            } @else { — }
                          </td>
                          <td class="num">{{ credito(i.itemVendaId) | currency: 'BRL' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>

                <fieldset class="motivos">
                  <legend>Motivo da troca</legend>
                  @for (m of motivos; track m.valor) {
                    <button type="button" class="botao pequeno" [class.ligado]="motivo() === m.valor" (click)="motivo.set(m.valor)">{{ m.rotulo }}</button>
                  }
                </fieldset>
                <div class="dois">
                  <label>Observação <small>opcional</small><input [(ngModel)]="observacao" maxlength="200" placeholder="Ex.: troca do 42 pelo 41" /></label>
                  <label>Nome do cliente <small>opcional, sai no vale</small><input [(ngModel)]="cliente" maxlength="100" /></label>
                </div>
                @if (!v.dentroDoPrazo) {
                  <app-autorizacao-gerente #autorizacao [precisa]="!ehGerencia()" [(motivo)]="justificativa"
                    [sugestoes]="['Cliente antigo', 'Defeito de fábrica', 'Combinado na venda']" />
                }
                <div class="total-credito"><span>Crédito para o cliente</span><strong>{{ totalCredito() | currency: 'BRL' }}</strong></div>
              }
              @if (erro(); as e) { <p class="erro" role="alert">{{ e }}</p> }
            </div>
            <footer>
              <button type="button" class="botao" (click)="fechar()">Cancelar</button>
              @if (!v.bloqueio) {
                <button type="button" class="botao primario" (click)="confirmar()" [disabled]="ocupado() || totalCredito() <= 0">
                  {{ ocupado() ? 'Registrando…' : 'Confirmar troca' }}</button>
              }
            </footer>
          }
        }

        @case ('pronto') {
          @if (troca(); as t) {
            <div class="corpo pronto">
              <app-icone nome="circle-check" class="grande" />
              <p><b>Troca registrada.</b> {{ t.itens.length === 1 ? 'O item voltou' : 'Os itens voltaram' }} para o estoque.</p>
              <div class="vale">
                <span>Crédito no vale-troca</span>
                <strong>{{ t.vale.valor | currency: 'BRL' }}</strong>
                <code>{{ t.vale.codigo }}</code>
                <small>válido até {{ diaBr(t.vale.validade) }}</small>
              </div>
              <p class="dica">Vai levar o produto novo agora? O crédito já entra no pagamento (se o novo for mais caro, o cliente paga
                a diferença; se for mais barato, o resto fica no vale).</p>
            </div>
            <footer>
              <button type="button" class="botao" (click)="imprimirVale()"><app-icone nome="printer" /> Imprimir vale</button>
              <button type="button" class="botao" (click)="fechar()">Cliente leva o vale</button>
              <button #btnUsar type="button" class="botao primario" (click)="usar()">Escolher o produto novo</button>
            </footer>
          }
        }
      }
    </dialog>`,
  styles: `
    .janela-troca { width: min(720px, calc(100vw - 32px)); max-height: calc(100dvh - 32px); padding: 0; border: 0; border-radius: 14px;
      box-shadow: 0 20px 60px rgb(0 0 0 / 0.25); color: var(--cor-texto); background: var(--cor-superficie); }
    .janela-troca[open] { display: flex; flex-direction: column; }
    .janela-troca::backdrop { background: rgb(10 20 15 / 0.5); }
    header, footer { display: flex; align-items: center; gap: 10px; padding: 14px 18px; flex-shrink: 0; flex-wrap: wrap; }
    header { justify-content: space-between; border-bottom: 1px solid var(--cor-borda); }
    footer { justify-content: flex-end; border-top: 1px solid var(--cor-borda); }
    h2 { margin: 0; font-size: 18px; display: flex; align-items: center; gap: 8px; }
    .fechar { border: 0; background: transparent; color: inherit; cursor: pointer; padding: 4px; }
    .corpo { padding: 16px 18px; display: flex; flex-direction: column; gap: 12px; overflow: auto; }
    form { display: contents; }
    label { display: flex; flex-direction: column; gap: 6px; font-weight: 600; font-size: 14px; }
    label small { font-weight: 400; color: var(--cor-texto-suave); }
    .campo-botao { display: flex; gap: 8px; }
    .campo-botao input { flex: 1; min-width: 0; font-size: 18px; }
    .ou { font-size: 13px; color: var(--cor-texto-suave); text-align: center; }
    .dica { margin: 0; font-size: 14px; color: var(--cor-texto-suave); }
    .vendas { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    .vendas button { width: 100%; text-align: left; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; padding: 10px 12px;
      border: 1px solid var(--cor-borda); border-radius: 10px; background: var(--cor-superficie); color: inherit; cursor: pointer; font: inherit; }
    .vendas button:hover { border-color: var(--cor-primaria); }
    .vendas small { flex-basis: 100%; color: var(--cor-texto-suave); font-size: 13px; }
    .venda-resumo { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; font-size: 14px; }
    .selo { display: inline-flex; align-items: center; gap: 4px; font-size: 12px; font-weight: 600; padding: 2px 8px; border-radius: 99px; }
    .selo.ok { background: var(--cor-primaria-clara); color: var(--cor-primaria-escura); }
    .selo.alerta { background: var(--cor-alerta-fundo); color: var(--cor-alerta); border: 1px solid var(--cor-alerta-borda); }
    .rolagem { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; font-size: 14px; }
    th, td { padding: 7px 6px; border-bottom: 1px solid var(--cor-borda); text-align: left; vertical-align: middle; }
    th { font-size: 12px; color: var(--cor-texto-suave); font-weight: 600; }
    .num { text-align: right; white-space: nowrap; }
    td small { display: block; color: var(--cor-texto-suave); font-size: 12px; }
    tr.apagado { opacity: 0.5; }
    .qtd { width: 80px; text-align: right; }
    .motivos { border: 0; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; }
    .motivos legend { font-weight: 600; font-size: 14px; margin-bottom: 6px; padding: 0; }
    .dois { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .total-credito { display: flex; justify-content: space-between; align-items: baseline; padding: 10px 12px; border-radius: 10px;
      background: var(--cor-primaria-clara); color: var(--cor-primaria-escura); }
    .total-credito strong { font-size: 22px; }
    .erro { margin: 0; color: var(--cor-erro); font-size: 14px; }
    .pronto { align-items: center; text-align: center; }
    .pronto p { margin: 0; }
    .grande { font-size: 40px; color: var(--cor-primaria); }
    .vale { display: flex; flex-direction: column; align-items: center; gap: 4px; padding: 14px 22px; border: 2px dashed var(--cor-primaria);
      border-radius: 12px; }
    .vale strong { font-size: 26px; }
    .vale code { font-size: 22px; font-weight: 700; letter-spacing: 2px; }
    .vale small, .vale span { color: var(--cor-texto-suave); font-size: 13px; }
    @media (max-width: 560px) { .dois { grid-template-columns: 1fr; } }
  `,
})
export class TrocaCaixa {
  private readonly api = inject(TrocasApi);
  private readonly impressora = inject(ImpressoraService);

  readonly ehGerencia = input(false);
  readonly loja = input('');
  readonly usarAgora = output<Troca>();   // cliente leva o produto novo agora: o crédito entra na compra
  readonly fechada = output<void>();

  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campoNumero = viewChild<ElementRef<HTMLInputElement>>('campoNumero');
  private readonly btnUsar = viewChild<ElementRef<HTMLButtonElement>>('btnUsar');
  private readonly autorizacao = viewChild<AutorizacaoGerente>('autorizacao');

  protected readonly motivos = MOTIVOS_TROCA;
  protected readonly etapa = signal<'buscar' | 'itens' | 'pronto'>('buscar');
  protected readonly venda = signal<VendaParaTroca | null>(null);
  protected readonly vendasComProduto = signal<VendaComProduto[]>([]);
  protected readonly produtoBuscado = signal<string | null>(null);
  protected readonly qtd = signal<Record<number, number>>({});
  protected readonly motivo = signal<string>('Tamanho');
  protected readonly justificativa = signal('');
  protected readonly troca = signal<Troca | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected numero = '';
  protected observacao = '';
  protected cliente = '';

  protected readonly totalCredito = computed(() =>
    Math.round((this.venda()?.itens ?? []).reduce((t, i) => t + this.credito(i.itemVendaId), 0) * 100) / 100);

  abrir(): void {
    this.etapa.set('buscar');
    this.venda.set(null);
    this.troca.set(null);
    this.vendasComProduto.set([]);
    this.produtoBuscado.set(null);
    this.erro.set(null);
    this.numero = '';
    this.dlg().nativeElement.showModal();
    setTimeout(() => this.campoNumero()?.nativeElement.focus());
  }

  fechar(): void {
    this.dlg().nativeElement.close();
  }

  protected aoFechar(): void {
    this.fechada.emit();
  }

  protected async buscarVenda(): Promise<void> {
    const id = Number(this.numero.replace(/\D/g, ''));
    if (!id) { this.erro.set('Digite o número da venda.'); return; }
    await this.abrirVenda(id);
  }

  protected async buscarPorProduto(p: Produto): Promise<void> {
    this.erro.set(null);
    this.produtoBuscado.set(p.nome);
    this.ocupado.set(true);
    try { this.vendasComProduto.set(await this.api.vendasComProduto(p.id)); }
    catch (e) { this.erro.set(this.mensagem(e)); }
    finally { this.ocupado.set(false); }
  }

  protected async abrirVenda(id: number): Promise<void> {
    this.erro.set(null);
    this.ocupado.set(true);
    try {
      const v = await this.api.venda(id);
      this.venda.set(v);
      // Uma linha só com 1 unidade (o caso comum: um tênis): já vem marcada.
      const livres = v.itens.filter((i) => i.disponivel > 0);
      this.qtd.set(livres.length === 1 ? { [livres[0].itemVendaId]: livres[0].disponivel } : {});
      this.motivo.set('Tamanho');
      this.observacao = '';
      this.cliente = '';
      this.justificativa.set('');
      this.etapa.set('itens');
    } catch (e) {
      this.erro.set(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  protected voltarBusca(): void {
    this.etapa.set('buscar');
    this.erro.set(null);
    setTimeout(() => this.campoNumero()?.nativeElement.focus());
  }

  protected marcar(itemVendaId: number, quantidade: number): void {
    const item = this.venda()?.itens.find((i) => i.itemVendaId === itemVendaId);
    if (!item) return;
    const q = Math.max(0, Math.min(Number(quantidade) || 0, item.disponivel));
    this.qtd.update((m) => ({ ...m, [itemVendaId]: q }));
  }

  protected credito(itemVendaId: number): number {
    const item = this.venda()?.itens.find((i) => i.itemVendaId === itemVendaId);
    const q = this.qtd()[itemVendaId] ?? 0;
    return item ? Math.round(item.creditoUnitario * q * 100) / 100 : 0;
  }

  protected async confirmar(): Promise<void> {
    const v = this.venda();
    if (!v) return;
    const itens = Object.entries(this.qtd()).filter(([, q]) => q > 0).map(([id, q]) => ({ itemVendaId: Number(id), quantidade: q }));
    if (!itens.length) { this.erro.set('Marque o que o cliente está devolvendo.'); return; }
    const aut = this.autorizacao();
    if (!v.dentroDoPrazo && aut) {
      const falta = aut.falta();
      if (falta) { this.erro.set(falta); return; }
    }
    this.erro.set(null);
    this.ocupado.set(true);
    try {
      const t = await this.api.registrar({
        vendaId: v.vendaId, itens, motivo: this.motivo(),
        observacao: [this.observacao.trim(), !v.dentroDoPrazo ? this.justificativa().trim() : ''].filter(Boolean).join(' · ') || null,
        cliente: this.cliente.trim() || null, autorizacao: aut?.autorizacao() ?? null,
      });
      this.troca.set(t);
      this.etapa.set('pronto');
      setTimeout(() => this.btnUsar()?.nativeElement.focus());
    } catch (e) {
      this.erro.set(this.mensagem(e));
      if (e instanceof HttpErrorResponse && e.error?.autorizacao) aut?.senhaErrada();
    } finally {
      this.ocupado.set(false);
    }
  }

  protected usar(): void {
    const t = this.troca();
    if (t) this.usarAgora.emit(t);
    this.fechar();
  }

  protected imprimirVale(): void {
    const t = this.troca();
    if (t) this.impressora.imprimirHtml(this.impressora.htmlValeTroca(t, this.loja()));
  }

  protected diaBr(iso: string): string {
    const [a, m, d] = iso.slice(0, 10).split('-');
    return `${d}/${m}/${a}`;
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      if (e.status === 0) return 'Sem conexão com o servidor. Tente de novo.';
    }
    return 'Não foi possível concluir.';
  }
}

import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth/auth.service';
import { ImpressoraService } from '../../core/equipamentos/impressora.service';
import { CancelamentoRegistro, Cupom, FORMAS_PAGAMENTO, ListaVendas, PdvApi, VendaLista } from '../../core/api/pdv.api';
import { MODULOS } from '../../core/modulos';
import { MARCA } from '../../shared/marca';
import { CampoData } from '../../shared/campo-data';
import { CupomDetalhe } from '../../shared/cupom-detalhe';
import { EstornoVenda } from '../../shared/estorno-venda';
import { Icone } from '../../shared/icone';
import { hojeIso } from '../plataforma/comum';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

const ACOES: Record<CancelamentoRegistro['acao'], string> = {
  CancelarItem: 'Item tirado da compra',
  CancelarCompra: 'Compra cancelada (antes de pagar)',
  CancelarVenda: 'Venda paga cancelada (estorno)',
};

// Vendas: a lista do dia (filtros por caixa, forma e situação), o cupom completo de cada uma com Reimprimir e
// Cancelar venda (estorno), e a aba Cancelamentos — tudo o que saiu do caixa com autorização do gerente.
@Component({
  selector: 'app-vendas',
  imports: [FecharAoClicarFora, Icone, FormsModule, CurrencyPipe, DatePipe, CampoData, CupomDetalhe, EstornoVenda],
  templateUrl: './vendas.html',
  styleUrl: './vendas.scss',
})
export class Vendas {
  private readonly api = inject(PdvApi);
  private readonly auth = inject(AuthService);
  private readonly impressora = inject(ImpressoraService);

  protected readonly formas = MODULOS.fiado ? FORMAS_PAGAMENTO : FORMAS_PAGAMENTO.filter((f) => f.valor !== 'Fiado');
  protected readonly aba = signal<'vendas' | 'cancelamentos'>('vendas');

  // ----- Filtros -----
  protected readonly data = signal(hojeIso());
  protected readonly caixa = signal<number | null>(null);
  protected readonly forma = signal('');
  protected readonly situacao = signal('');
  protected readonly pagina = signal(1);
  protected readonly tamanho = 25;

  protected readonly lista = signal<ListaVendas | null>(null);
  protected readonly carregando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly cancelamentos = signal<CancelamentoRegistro[] | null>(null);
  protected readonly paginas = computed(() => Math.max(1, Math.ceil((this.lista()?.total ?? 0) / this.tamanho)));
  protected readonly totalCancelamentos = computed(() => (this.cancelamentos() ?? []).reduce((s, c) => s + c.valor, 0));

  // ----- Venda aberta (cupom) -----
  protected readonly venda = signal<Cupom | null>(null);
  protected readonly estornando = signal(false);
  protected readonly avisoVenda = signal<string | null>(null);
  protected readonly erroVenda = signal<string | null>(null);
  private readonly dlgVenda = viewChild<ElementRef<HTMLDialogElement>>('dlgVenda');

  constructor() {
    // Mudou um filtro (ou a página, ou a aba): busca de novo.
    effect(() => {
      const filtro = { data: this.data(), caixa: this.caixa(), forma: this.forma(), situacao: this.situacao(), pagina: this.pagina() };
      const aba = this.aba();
      untracked(() => void (aba === 'vendas' ? this.carregar(filtro) : this.carregarCancelamentos(filtro.data, filtro.caixa)));
    });
  }

  private async carregar(f: { data: string; caixa: number | null; forma: string; situacao: string; pagina: number }): Promise<void> {
    if (!f.data) return;
    this.carregando.set(true);
    this.erro.set(null);
    try {
      this.lista.set(await this.api.vendas({ ...f, tamanho: this.tamanho }));
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.carregando.set(false);
    }
  }

  private async carregarCancelamentos(data: string, caixa: number | null): Promise<void> {
    if (!data) return;
    this.carregando.set(true);
    this.erro.set(null);
    try {
      this.cancelamentos.set(await this.api.cancelamentos(data, caixa));
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.carregando.set(false);
    }
  }

  // Trocar um filtro volta para a página 1.
  protected filtrar(acao: () => void): void {
    acao();
    this.pagina.set(1);
  }

  protected irPara(p: number): void {
    this.pagina.set(Math.min(Math.max(1, p), this.paginas()));
  }

  protected async abrir(v: VendaLista): Promise<void> {
    this.venda.set(null);
    this.estornando.set(false);
    this.avisoVenda.set(null);
    this.erroVenda.set(null);
    this.dlgVenda()?.nativeElement.showModal();
    try {
      this.venda.set(await this.api.venda(v.id));
    } catch (e) {
      this.erroVenda.set(mensagem(e));
    }
  }

  protected async abrirPorId(id: number | null): Promise<void> {
    if (id) await this.abrir({ id } as VendaLista);
  }

  protected reimprimir(): void {
    const c = this.venda();
    if (c) this.impressora.imprimirCupom(c, this.auth.usuario()?.empresa.nome ?? MARCA.nome);
  }

  protected vendaCancelada(c: Cupom): void {
    this.venda.set(c);
    this.estornando.set(false);
    const dinheiro = c.pagamentos.filter((p) => p.forma === 'Dinheiro').reduce((s, p) => s + p.valor, 0) - c.troco;
    this.avisoVenda.set(`Venda #${c.id} cancelada e estornada${dinheiro > 0 ? `: devolva ${reais(dinheiro)} em dinheiro ao cliente` : ''}.`);
    if (this.impressora.config().automatica) this.reimprimir();
    void this.carregar({ data: this.data(), caixa: this.caixa(), forma: this.forma(), situacao: this.situacao(), pagina: this.pagina() });
  }

  protected rotuloFormas(formas: string): string {
    return formas.split(' + ').map((f) => FORMAS_PAGAMENTO.find((x) => x.valor === f)?.rotulo ?? f).join(' + ');
  }

  protected rotuloAcao(a: CancelamentoRegistro['acao']): string {
    return ACOES[a] ?? a;
  }
}

function reais(v: number): string {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

function mensagem(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.error?.mensagem) return e.error.mensagem;
    if (e.status === 0) return 'Sem conexão com o servidor.';
  }
  return 'Não foi possível carregar. Tente de novo.';
}

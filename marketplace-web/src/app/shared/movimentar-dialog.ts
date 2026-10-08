import { Component, ElementRef, computed, inject, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { EstoqueApi, Lote, Movimentacao } from '../core/api/estoque.api';
import { Produto, ProdutosApi } from '../core/api/produtos.api';
import { DiaPipe } from './dia.pipe';
import { ProdutoBusca } from './produto-busca';
import { QuantidadePipe } from './quantidade.pipe';

export type TipoAcao = 'Entrada' | 'Perda' | 'Ajuste';

// Janela (modal) para registrar Entrada, Perda ou Ajuste.
// Usa o <dialog> nativo do navegador: já vem com fundo escuro, tecla Esc e foco preso dentro dele.
// Uso:  <app-movimentar-dialog #dialogo (salvo)="..." />   e   dialogo.abrir('Entrada', produto?)
@Component({
  selector: 'app-movimentar-dialog',
  imports: [FormsModule, CurrencyPipe, DiaPipe, ProdutoBusca, QuantidadePipe],
  templateUrl: './movimentar-dialog.html',
  styleUrl: './movimentar-dialog.scss',
})
export class MovimentarDialog {
  private readonly estoqueApi = inject(EstoqueApi);
  private readonly produtosApi = inject(ProdutosApi);

  readonly salvo = output<Movimentacao>();

  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');
  private readonly busca = viewChild(ProdutoBusca);

  protected readonly motivos = ['Vencido', 'Avariado', 'Furto/Extravio', 'Consumo interno', 'Outro'];
  protected readonly hoje = new Date().toLocaleDateString('sv-SE'); // "AAAA-MM-DD" no fuso local

  protected readonly tipo = signal<TipoAcao>('Entrada');
  protected readonly produto = signal<Produto | null>(null);
  protected readonly lotes = signal<Lote[]>([]);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  // Campos do formulário (formulário "template-driven": [(ngModel)] liga o campo direto à variável).
  protected quantidade: number | null = null;
  protected custoUnitario: number | null = null;
  protected validade = '';
  protected motivo = '';
  protected loteId: number | null = null;
  protected observacao = '';

  protected readonly titulo = computed(
    () => ({ Entrada: '📥 Entrada de mercadoria', Perda: '🗑️ Registrar perda', Ajuste: '⚖️ Ajuste de inventário' })[this.tipo()],
  );
  protected readonly fracionado = computed(() => ['KG', 'L'].includes(this.produto()?.unidade ?? ''));

  async abrir(tipo: TipoAcao, produto?: Produto | null): Promise<void> {
    this.tipo.set(tipo);
    this.limpar();
    this.dialogo().nativeElement.showModal();
    if (produto) {
      // Busca a versão mais recente (o estoque pode ter mudado desde que a lista carregou).
      await this.escolherProduto(await this.produtosApi.obter(produto.id));
    } else {
      setTimeout(() => this.busca()?.focar());
    }
  }

  fechar(): void {
    this.dialogo().nativeElement.close();
  }

  protected async escolherProduto(produto: Produto): Promise<void> {
    this.produto.set(produto);
    this.erro.set(null);
    this.custoUnitario = produto.precoCusto;
    this.lotes.set(produto.controlaValidade ? await this.estoqueApi.lotesDoProduto(produto.id) : []);
  }

  protected trocarProduto(): void {
    this.produto.set(null);
    this.lotes.set([]);
    setTimeout(() => this.busca()?.focar());
  }

  // Diferença do ajuste (contado − sistema), mostrada enquanto a pessoa digita.
  protected diferencaAjuste(): number | null {
    const p = this.produto();
    return p && this.quantidade !== null ? this.quantidade - p.estoqueAtual : null;
  }

  protected precisaValidade(): boolean {
    const p = this.produto();
    if (!p?.controlaValidade) return false;
    if (this.tipo() === 'Entrada') return true;
    return this.tipo() === 'Ajuste' && (this.diferencaAjuste() ?? 0) > 0;
  }

  protected async salvar(): Promise<void> {
    const p = this.produto();
    if (!p) return;
    this.erro.set(null);
    this.salvando.set(true);

    const observacao = this.observacao.trim() || null;
    try {
      let mov: Movimentacao;
      if (this.tipo() === 'Entrada') {
        mov = await this.estoqueApi.entrada({
          produtoId: p.id, quantidade: this.quantidade ?? 0, custoUnitario: this.custoUnitario,
          validade: this.validade || null, observacao,
        });
      } else if (this.tipo() === 'Perda') {
        mov = await this.estoqueApi.perda({
          produtoId: p.id, quantidade: this.quantidade ?? 0, motivo: this.motivo,
          loteId: this.loteId, observacao,
        });
      } else {
        mov = await this.estoqueApi.ajuste({
          produtoId: p.id, quantidadeContada: this.quantidade ?? 0,
          validade: this.validade || null, observacao: observacao ?? '',
        });
      }
      this.salvo.emit(mov);
      this.fechar();
    } catch (e) {
      this.erro.set(this.mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }

  // A API responde { mensagem } (regra de negócio) ou { errors: {...} } (validação dos campos).
  private mensagemDeErro(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      if (e.error?.errors) return Object.values<string[]>(e.error.errors).flat().join(' ');
      if (e.status === 403) return 'Seu perfil não pode movimentar o estoque.';
    }
    return 'Não foi possível registrar. Tente de novo.';
  }

  private limpar(): void {
    this.produto.set(null);
    this.lotes.set([]);
    this.erro.set(null);
    this.quantidade = null;
    this.custoUnitario = null;
    this.validade = '';
    this.motivo = '';
    this.loteId = null;
    this.observacao = '';
  }
}

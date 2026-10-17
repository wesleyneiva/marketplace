import { Component, computed, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { Lote, Movimentacao, TIPOS_MOVIMENTACAO } from '../../core/api/estoque.api';
import { Pagina, Produto, ProdutosApi } from '../../core/api/produtos.api';
import { DiaPipe } from '../../shared/dia.pipe';
import { MovimentarDialog } from '../../shared/movimentar-dialog';
import { ProdutoBusca } from '../../shared/produto-busca';
import { QuantidadePipe } from '../../shared/quantidade.pipe';
import { Icone } from '../../shared/icone';
import { FormsModule } from '@angular/forms';
import { CampoData } from '../../shared/campo-data';

type Aba = 'validades' | 'movimentacoes' | 'baixo';

@Component({
  selector: 'app-estoque',
  imports: [Icone, RouterLink, CurrencyPipe, DatePipe, DiaPipe, QuantidadePipe, MovimentarDialog, ProdutoBusca, FormsModule, CampoData],
  templateUrl: './estoque.html',
  styleUrl: './estoque.scss',
})
export class Estoque {
  private readonly router = inject(Router);
  private readonly produtosApi = inject(ProdutosApi);

  // ----- Consultar um produto (busca no topo): estoque, lotes com validade e últimas movimentações -----
  protected readonly consultado = signal<Produto | null>(null);
  protected readonly consultaLotes = httpResource<Lote[]>(() => {
    const p = this.consultado();
    return p?.controlaValidade ? `/api/estoque/produtos/${p.id}/lotes` : undefined;
  });
  protected readonly consultaMov = httpResource<Pagina<Movimentacao>>(() => {
    const p = this.consultado();
    return p ? { url: '/api/estoque/movimentacoes', params: { produtoId: p.id, tamanho: 5 } } : undefined;
  });

  protected async consultar(p: Produto): Promise<void> {
    this.consultado.set(p);
    try { this.consultado.set(await this.produtosApi.obter(p.id)); } catch { /* fica com o da busca */ }
  }

  // A aba vem da URL (?aba=movimentacoes) → dá para mandar o link direto de uma aba.
  readonly aba = input<Aba>('validades');
  protected readonly abaAtual = computed<Aba>(() => this.aba() ?? 'validades');

  protected readonly tipos = TIPOS_MOVIMENTACAO;
  protected readonly aviso = signal<string | null>(null);

  // ----- Aba Validades -----
  protected readonly diasValidade = signal(7);
  protected readonly validades = httpResource<Lote[]>(() =>
    this.abaAtual() === 'validades' ? { url: '/api/estoque/validades', params: { dias: this.diasValidade() } } : undefined,
  );
  protected readonly resumoValidades = computed(() => {
    const lotes = this.validades.value() ?? [];
    const vencidos = lotes.filter((l) => l.diasRestantes < 0);
    return {
      vencidos: vencidos.length,
      valorVencido: vencidos.reduce((s, l) => s + l.valorEmRisco, 0),
      proximos: lotes.length - vencidos.length,
      valorTotal: lotes.reduce((s, l) => s + l.valorEmRisco, 0),
    };
  });

  // ----- Aba Movimentações -----
  protected readonly filtroProduto = signal<Produto | null>(null);
  protected readonly filtroTipo = signal('');
  protected readonly filtroDe = signal('');
  protected readonly filtroAte = signal('');
  protected readonly paginaMov = signal(1);
  protected readonly movimentacoes = httpResource<Pagina<Movimentacao>>(() =>
    this.abaAtual() === 'movimentacoes'
      ? {
          url: '/api/estoque/movimentacoes',
          params: {
            ...(this.filtroProduto() ? { produtoId: this.filtroProduto()!.id } : {}),
            ...(this.filtroTipo() ? { tipo: this.filtroTipo() } : {}),
            ...(this.filtroDe() ? { de: this.filtroDe() } : {}),
            ...(this.filtroAte() ? { ate: this.filtroAte() } : {}),
            pagina: this.paginaMov(),
            tamanho: 20,
          },
        }
      : undefined,
  );
  protected readonly totalPaginasMov = computed(() => Math.max(1, Math.ceil((this.movimentacoes.value()?.total ?? 0) / 20)));

  // ----- Aba Estoque baixo -----
  protected readonly baixo = httpResource<Pagina<Produto>>(() =>
    this.abaAtual() === 'baixo' ? { url: '/api/produtos', params: { estoqueBaixo: true, tamanho: 100 } } : undefined,
  );

  // Quando uma movimentação é registrada: mostra o aviso e recarrega o que estiver na tela.
  protected aoSalvar(m: Movimentacao): void {
    this.aviso.set(
      `${m.tipo} registrada: ${m.produto} — estoque ${this.fmt(m.estoqueAnterior, m.unidade)} → ${this.fmt(m.estoquePosterior, m.unidade)}.`,
    );
    setTimeout(() => this.aviso.set(null), 6000);
    this.validades.reload();
    this.movimentacoes.reload();
    this.baixo.reload();
    const c = this.consultado();
    if (c && c.id === m.produtoId) void this.consultar(c); // o cartão do produto consultado mostra o estoque novo
  }

  protected irPara(aba: Aba): void {
    void this.router.navigate([], { queryParams: { aba } });
  }

  protected mudarFiltro(acao: () => void): void {
    acao();
    this.paginaMov.set(1);
  }

  protected iconeTipo(tipo: string): string {
    return this.tipos.find((t) => t.valor === tipo)?.icone ?? '';
  }

  private fmt(valor: number, unidade: string): string {
    const casas = unidade === 'KG' || unidade === 'L' ? 3 : 0;
    return `${valor.toLocaleString('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas })} ${unidade}`;
  }
}

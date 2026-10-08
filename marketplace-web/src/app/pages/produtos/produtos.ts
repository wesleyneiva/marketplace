import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Categoria, Pagina, Produto, ProdutosApi } from '../../core/api/produtos.api';

@Component({
  selector: 'app-produtos',
  imports: [RouterLink, CurrencyPipe, DecimalPipe],
  templateUrl: './produtos.html',
  styleUrl: './produtos.scss',
})
export class Produtos {
  private readonly api = inject(ProdutosApi);

  // ----- Filtros (cada um é um signal: mudou → a lista recarrega sozinha) -----
  protected readonly busca = signal('');
  protected readonly categoriaId = signal<number | null>(null);
  protected readonly estoqueBaixo = signal(false);
  protected readonly incluirInativos = signal(false);
  protected readonly pagina = signal(1);
  protected readonly tamanho = 15;

  // httpResource: faz o GET e REFAZ automaticamente quando algum signal usado aqui dentro muda.
  protected readonly produtos = httpResource<Pagina<Produto>>(() => ({
    url: '/api/produtos',
    params: {
      busca: this.busca(),
      ...(this.categoriaId() ? { categoriaId: this.categoriaId()! } : {}),
      estoqueBaixo: this.estoqueBaixo(),
      incluirInativos: this.incluirInativos(),
      pagina: this.pagina(),
      tamanho: this.tamanho,
    },
  }));

  protected readonly categorias = httpResource<Categoria[]>(() => '/api/categorias');

  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil((this.produtos.value()?.total ?? 0) / this.tamanho)),
  );

  protected readonly processando = signal<number | null>(null);
  protected readonly erro = signal<string | null>(null);

  // Espera o usuário parar de digitar (300 ms) antes de buscar — evita uma chamada por tecla.
  private esperaBusca?: ReturnType<typeof setTimeout>;
  digitarBusca(texto: string): void {
    clearTimeout(this.esperaBusca);
    this.esperaBusca = setTimeout(() => {
      this.busca.set(texto.trim());
      this.pagina.set(1);
    }, 300);
  }

  escolherCategoria(valor: string): void {
    this.categoriaId.set(valor ? Number(valor) : null);
    this.pagina.set(1);
  }

  alternarEstoqueBaixo(): void {
    this.estoqueBaixo.update((v) => !v);
    this.pagina.set(1);
  }

  alternarInativos(): void {
    this.incluirInativos.update((v) => !v);
    this.pagina.set(1);
  }

  irParaPagina(numero: number): void {
    this.pagina.set(Math.min(Math.max(numero, 1), this.totalPaginas()));
  }

  async alternarAtivo(produto: Produto): Promise<void> {
    if (produto.ativo && !confirm(`Desativar "${produto.nome}"? Ele deixa de aparecer nas vendas.`)) return;

    this.processando.set(produto.id);
    this.erro.set(null);
    try {
      if (produto.ativo) await this.api.desativar(produto.id);
      else await this.api.reativar(produto.id);
      this.produtos.reload();
    } catch {
      this.erro.set('Não foi possível alterar o produto. Tente de novo.');
    } finally {
      this.processando.set(null);
    }
  }
}

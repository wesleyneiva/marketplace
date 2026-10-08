import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Categoria, Pagina, Produto, ProdutoRequest, ProdutosApi, UNIDADES } from '../../core/api/produtos.api';
import { Lote, Movimentacao } from '../../core/api/estoque.api';
import { DatePipe } from '@angular/common';
import { DiaPipe } from '../../shared/dia.pipe';
import { MovimentarDialog } from '../../shared/movimentar-dialog';
import { QuantidadePipe } from '../../shared/quantidade.pipe';

// Mesma tela para "novo" e "editar": se a rota tem :id, estamos editando.
@Component({
  selector: 'app-produto-form',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, DecimalPipe, DatePipe, DiaPipe, QuantidadePipe, MovimentarDialog],
  templateUrl: './produto-form.html',
  styleUrl: './produto-form.scss',
})
export class ProdutoForm {
  private readonly api = inject(ProdutosApi);
  private readonly router = inject(Router);

  // Vem da rota (/produtos/:id) graças ao withComponentInputBinding() no app.config.ts.
  readonly id = input<string>();
  protected readonly editando = computed(() => !!this.id());

  protected readonly unidades = UNIDADES;
  protected readonly categorias = httpResource<Categoria[]>(() => '/api/categorias');
  protected readonly fornecedores = httpResource<{ id: number; nome: string; prazoEntregaDias: number }[]>(() => '/api/fornecedores');

  protected readonly form = inject(NonNullableFormBuilder).group({
    nome: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    codigoBarras: ['', [Validators.pattern(/^\d{8,14}$/)]],
    categoriaId: [0, [Validators.min(1)]],
    unidade: ['UN', Validators.required],
    precoCusto: [0, [Validators.required, Validators.min(0)]],
    precoVenda: [0, [Validators.required, Validators.min(0.01)]],
    estoqueMinimo: [0, [Validators.required, Validators.min(0)]],
    controlaValidade: [false],
    fornecedorId: [null as number | null],
  });

  protected readonly produto = signal<Produto | null>(null);

  // Só na edição: lotes de validade e as últimas movimentações deste produto.
  protected readonly lotes = httpResource<Lote[]>(() =>
    this.id() && this.produto()?.controlaValidade ? `/api/estoque/produtos/${this.id()}/lotes` : undefined,
  );
  protected readonly historico = httpResource<Pagina<Movimentacao>>(() =>
    this.id() ? { url: '/api/estoque/movimentacoes', params: { produtoId: this.id()!, tamanho: 8 } } : undefined,
  );
  protected readonly carregando = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erroGeral = signal<string | null>(null);
  // Mensagens que vieram da API, por campo (ex.: { codigoBarras: 'Já existe...' }).
  protected readonly errosApi = signal<Record<string, string>>({});

  // Margem calculada AO VIVO enquanto o usuário digita os preços.
  private readonly valores = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly margem = computed(() => {
    const venda = Number(this.valores().precoVenda) || 0;
    const custo = Number(this.valores().precoCusto) || 0;
    return venda > 0 ? ((venda - custo) / venda) * 100 : null;
  });
  protected readonly lucroPorUnidade = computed(
    () => (Number(this.valores().precoVenda) || 0) - (Number(this.valores().precoCusto) || 0),
  );

  constructor() {
    // Mexeu no formulário depois de um erro da API → as mensagens antigas da API somem.
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (Object.keys(this.errosApi()).length) this.errosApi.set({});
    });

    // effect = "quando o id mudar, faça isto". Aqui: carregar o produto para editar.
    effect(() => {
      const id = this.id();
      if (id) void this.carregar(Number(id));
    });
  }

  private async carregar(id: number): Promise<void> {
    this.carregando.set(true);
    try {
      const p = await this.api.obter(id);
      this.produto.set(p);
      this.form.patchValue({
        nome: p.nome,
        codigoBarras: p.codigoBarras ?? '',
        categoriaId: p.categoriaId,
        unidade: p.unidade,
        precoCusto: p.precoCusto,
        precoVenda: p.precoVenda,
        estoqueMinimo: p.estoqueMinimo,
        controlaValidade: p.controlaValidade,
        fornecedorId: p.fornecedorId,
      });
    } catch {
      this.erroGeral.set('Produto não encontrado.');
    } finally {
      this.carregando.set(false);
    }
  }

  // Depois de uma entrada/perda/ajuste feita aqui: atualiza o estoque mostrado, os lotes e o histórico.
  protected async aoMovimentar(): Promise<void> {
    const id = this.id();
    if (!id) return;
    this.produto.set(await this.api.obter(Number(id)));
    this.lotes.reload();
    this.historico.reload();
  }

  async salvar(): Promise<void> {
    this.errosApi.set({});
    this.erroGeral.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const dados: ProdutoRequest = {
      ...v,
      codigoBarras: v.codigoBarras.trim() || null,
      categoriaId: Number(v.categoriaId),
    };

    this.salvando.set(true);
    try {
      const id = this.id();
      if (id) await this.api.atualizar(Number(id), dados);
      else await this.api.criar(dados);
      await this.router.navigateByUrl('/produtos');
    } catch (e) {
      this.tratarErro(e);
    } finally {
      this.salvando.set(false);
    }
  }

  // Traduz a resposta de erro da API para mensagens na tela.
  private tratarErro(e: unknown): void {
    if (!(e instanceof HttpErrorResponse)) {
      this.erroGeral.set('Erro inesperado.');
      return;
    }
    if (e.status === 400 && e.error?.errors) {
      // A API manda { errors: { "PrecoVenda": ["..."] } } → vira { precoVenda: "..." }
      const erros: Record<string, string> = {};
      for (const [campo, mensagens] of Object.entries<string[]>(e.error.errors)) {
        erros[campo.charAt(0).toLowerCase() + campo.slice(1)] = mensagens[0];
      }
      this.errosApi.set(erros);
    } else if (e.status === 409) {
      this.errosApi.set({ codigoBarras: e.error?.mensagem ?? 'Código de barras já usado.' });
    } else if (e.status === 403) {
      this.erroGeral.set('Seu perfil não tem permissão para alterar produtos.');
    } else {
      this.erroGeral.set('Não foi possível salvar. Tente de novo.');
    }
  }

  // Mensagem a mostrar embaixo de um campo: primeiro a da API, senão a da validação local.
  protected erroDe(campo: keyof typeof this.form.controls): string | null {
    const daApi = this.errosApi()[campo];
    if (daApi) return daApi;

    const controle = this.form.controls[campo];
    if (!controle.touched || controle.valid) return null;

    const mensagens: Record<string, string> = {
      nome: 'Informe o nome (de 2 a 150 caracteres).',
      codigoBarras: 'Use de 8 a 14 dígitos, só números.',
      categoriaId: 'Escolha uma categoria.',
      precoVenda: 'O preço de venda deve ser maior que zero.',
      precoCusto: 'Preço de custo inválido.',
      estoqueMinimo: 'Estoque mínimo inválido.',
    };
    return mensagens[campo] ?? 'Valor inválido.';
  }
}

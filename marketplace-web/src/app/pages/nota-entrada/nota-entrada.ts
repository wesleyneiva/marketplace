import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import {
  ConferenciaNota, DecisaoItemNota, ItemConferencia, NotaEntrada, NotasApi, ProdutoConferencia, RegistroNota, URL_NOTAS,
} from '../../core/api/notas.api';
import { Categoria, Produto, UNIDADES } from '../../core/api/produtos.api';
import { DiaPipe } from '../../shared/dia.pipe';
import { ProdutoBusca } from '../../shared/produto-busca';
import { QuantidadePipe } from '../../shared/quantidade.pipe';

type Acao = 'existente' | 'novo' | 'ignorar';

// O que a pessoa decidiu para cada item da nota (começa com o que o sistema achou).
interface ItemEstado {
  base: ItemConferencia;
  acao: Acao;
  produto: ProdutoConferencia | null;
  trocando: boolean;
  fator: number;
  validade: string | null;
  novo: {
    nome: string;
    codigoBarras: string | null;
    categoriaId: number | null;
    unidade: string;
    precoVenda: number | null;
    controlaValidade: boolean;
  };
}

// Entrada de mercadoria pelo XML da NF-e: enviar o XML → conferir item a item → dar entrada no estoque.
@Component({
  selector: 'app-nota-entrada',
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe, DiaPipe, QuantidadePipe, ProdutoBusca],
  templateUrl: './nota-entrada.html',
  styleUrl: './nota-entrada.scss',
})
export class NotaEntradaPagina {
  private readonly api = inject(NotasApi);

  protected readonly unidades = UNIDADES;
  protected readonly urlNotas = URL_NOTAS;
  protected readonly categorias = httpResource<Categoria[]>(() => '/api/categorias');
  protected readonly historico = httpResource<NotaEntrada[]>(() => URL_NOTAS);

  protected readonly lendo = signal(false);
  protected readonly gravando = signal(false);
  protected readonly arrastando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly conferencia = signal<ConferenciaNota | null>(null);
  protected readonly itens = signal<ItemEstado[]>([]);
  protected readonly resultado = signal<RegistroNota | null>(null);
  protected pedidoId: number | null = null;
  private arquivo: File | null = null;

  protected readonly hoje = new Date().toLocaleDateString('sv-SE'); // "2026-10-09" no fuso do navegador

  protected readonly categoriasAtivas = computed(() => (this.categorias.value() ?? []).filter((c) => c.ativa));

  // ------------------------------------------------------------------ enviar o XML

  escolherArquivo(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    const arquivo = campo.files?.[0];
    campo.value = '';
    if (arquivo) this.conferir(arquivo);
  }

  soltarArquivo(evento: DragEvent): void {
    evento.preventDefault();
    this.arrastando.set(false);
    const arquivo = evento.dataTransfer?.files?.[0];
    if (arquivo) this.conferir(arquivo);
  }

  arrastarPorCima(evento: DragEvent): void {
    evento.preventDefault();
    this.arrastando.set(true);
  }

  async conferir(arquivo: File): Promise<void> {
    if (!/\.xml$/i.test(arquivo.name)) {
      this.erro.set('Envie o arquivo .xml da nota (o PDF/DANFE não serve: ele é só a "foto" da nota).');
      return;
    }
    this.lendo.set(true);
    this.erro.set(null);
    this.resultado.set(null);
    try {
      const c = await this.api.conferir(arquivo);
      this.arquivo = arquivo;
      this.pedidoId = c.pedidoSugeridoId;
      this.itens.set(c.itens.map((base) => ({
        base,
        acao: base.produto ? 'existente' : 'novo',
        produto: base.produto,
        trocando: false,
        fator: base.fator,
        validade: base.validade,
        novo: { ...base.sugestao },
      })));
      this.conferencia.set(c);
    } catch (e) {
      this.erro.set(this.mensagem(e, 'Não foi possível ler o XML. Tente de novo.'));
    } finally {
      this.lendo.set(false);
    }
  }

  recomecar(): void {
    this.conferencia.set(null);
    this.itens.set([]);
    this.resultado.set(null);
    this.erro.set(null);
    this.arquivo = null;
  }

  // ------------------------------------------------------------------ decisões de cada item

  escolherProduto(item: ItemEstado, p: Produto): void {
    item.produto = {
      id: p.id, nome: p.nome, codigoBarras: p.codigoBarras, unidade: p.unidade, controlaValidade: p.controlaValidade,
      ativo: p.ativo, precoCusto: p.precoCusto, precoVenda: p.precoVenda,
    };
    item.acao = 'existente';
    item.trocando = false;
    this.mexeu();
  }

  mudarAcao(item: ItemEstado, acao: Acao): void {
    item.acao = acao;
    this.mexeu();
  }

  // Os itens são objetos comuns: depois de mudar um campo, avisa o Angular (novo array = telas recalculam).
  mexeu(): void {
    this.itens.update((lista) => [...lista]);
  }

  protected unidadeFinal(i: ItemEstado): string {
    return i.acao === 'existente' ? (i.produto?.unidade ?? 'UN') : i.novo.unidade;
  }

  protected quantidade(i: ItemEstado): number {
    return i.base.quantidadeNota * (Number(i.fator) || 0);
  }

  protected custoUnitario(i: ItemEstado): number {
    const q = this.quantidade(i);
    return q > 0 ? i.base.custoTotal / q : 0;
  }

  protected precisaValidade(i: ItemEstado): boolean {
    return i.acao === 'existente' ? !!i.produto?.controlaValidade : i.acao === 'novo' && i.novo.controlaValidade;
  }

  protected margem(i: ItemEstado): number | null {
    const preco = i.acao === 'existente' ? i.produto?.precoVenda : i.novo.precoVenda;
    const custo = this.custoUnitario(i);
    return preco && custo > 0 ? (preco / custo - 1) * 100 : null;
  }

  // O que falta resolver neste item (null = pronto).
  protected pendencia(i: ItemEstado): string | null {
    if (i.acao === 'ignorar') return null;
    if (i.acao === 'existente') {
      if (!i.produto) return 'Escolha o produto cadastrado (ou cadastre como novo).';
      if (!i.produto.ativo) return 'Produto desativado: escolha outro ou reative-o na tela de Produtos.';
    } else {
      if ((i.novo.nome ?? '').trim().length < 2) return 'Informe o nome do produto novo.';
      if (!i.novo.categoriaId) return 'Escolha a categoria do produto novo.';
      if (!i.novo.precoVenda || i.novo.precoVenda <= 0) return 'Informe o preço de venda.';
      if (i.novo.codigoBarras && !/^\d{8,14}$/.test(i.novo.codigoBarras)) return 'Código de barras: só números, de 8 a 14 dígitos.';
    }
    const fator = Number(i.fator);
    if (!fator || fator <= 0) return 'Informe quantas unidades vêm em cada embalagem (fator).';
    const unidade = this.unidadeFinal(i);
    if (unidade !== 'KG' && unidade !== 'L' && this.quantidade(i) % 1 !== 0)
      return `Daria ${this.quantidade(i)} ${unidade} (número quebrado): confira o fator.`;
    if (this.precisaValidade(i)) {
      if (!i.validade) return 'Informe a data de validade.';
      if (i.validade < this.hoje) return 'A validade já passou.';
    }
    return null;
  }

  protected readonly resumo = computed(() => {
    const lista = this.itens();
    return {
      existentes: lista.filter((i) => i.acao === 'existente').length,
      novos: lista.filter((i) => i.acao === 'novo').length,
      ignorados: lista.filter((i) => i.acao === 'ignorar').length,
      pendentes: lista.filter((i) => this.pendencia(i) !== null).length,
    };
  });

  // ------------------------------------------------------------------ gravar

  async darEntrada(): Promise<void> {
    const c = this.conferencia();
    const r = this.resumo();
    if (!c || !this.arquivo || r.pendentes > 0 || c.bloqueio) return;
    const lancar = r.existentes + r.novos;
    const texto = `Dar entrada de ${lancar} ${lancar === 1 ? 'item' : 'itens'} da NF-e ${c.numero} no estoque?`
      + (r.novos ? `\n${r.novos} produto(s) novo(s) serão cadastrados.` : '')
      + (this.pedidoId ? `\nO pedido de compra #${this.pedidoId} será marcado como recebido.` : '');
    if (!confirm(texto)) return;

    const decisoes: DecisaoItemNota[] = this.itens().map((i) => ({
      numeroItem: i.base.numeroItem,
      acao: i.acao,
      produtoId: i.acao === 'existente' ? (i.produto?.id ?? null) : null,
      novo: i.acao === 'novo'
        ? { ...i.novo, nome: i.novo.nome.trim(), codigoBarras: i.novo.codigoBarras || null, categoriaId: i.novo.categoriaId!, precoVenda: Number(i.novo.precoVenda) }
        : null,
      fator: Number(i.fator),
      validade: this.precisaValidade(i) ? i.validade : null,
    }));

    this.gravando.set(true);
    this.erro.set(null);
    try {
      this.resultado.set(await this.api.registrar(this.arquivo, this.pedidoId, decisoes));
      this.conferencia.set(null);
      this.itens.set([]);
      this.arquivo = null;
      this.historico.reload();
    } catch (e) {
      this.erro.set(this.mensagem(e, 'Não foi possível dar entrada. Nada foi gravado; tente de novo.'));
    } finally {
      this.gravando.set(false);
    }
  }

  protected chaveFormatada(chave: string): string {
    return chave.replace(/(\d{4})(?=\d)/g, '$1 ');
  }

  private mensagem(e: unknown, padrao: string): string {
    return (e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? padrao;
  }
}

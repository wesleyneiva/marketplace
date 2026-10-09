import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Entrada de mercadoria pelo XML da NF-e (Contracts/NotaEntradaDtos.cs).

export interface ProdutoConferencia {
  id: number;
  nome: string;
  codigoBarras: string | null;
  unidade: string;
  controlaValidade: boolean;
  ativo: boolean;
  precoCusto: number;
  precoVenda: number;
}

export interface NovoProdutoSugestao {
  nome: string;
  codigoBarras: string | null;
  categoriaId: number | null;
  unidade: string;
  controlaValidade: boolean;
  precoVenda: number | null;
}

export interface ItemConferencia {
  numeroItem: number;
  codigoFornecedor: string;
  codigoBarras: string | null;
  descricao: string;
  ncm: string | null;
  unidadeNota: string;
  quantidadeNota: number;
  custoTotal: number;
  produto: ProdutoConferencia | null;
  comoAchou: string | null;
  fator: number;
  fatorVeioDaNota: boolean;
  validade: string | null;
  sugestao: NovoProdutoSugestao;
  avisos: string[];
}

export interface PedidoAberto {
  id: number;
  criadoEm: string;
  previsaoEntrega: string | null;
  quantidadeItens: number;
  valorEstimado: number;
}

export interface ConferenciaNota {
  chave: string;
  numero: string;
  serie: string;
  dataEmissao: string;
  valorTotal: number;
  fornecedor: { id: number | null; nome: string; razaoSocial: string; cnpj: string; novo: boolean };
  bloqueio: string | null;
  avisos: string[];
  pedidosAbertos: PedidoAberto[];
  pedidoSugeridoId: number | null;
  itens: ItemConferencia[];
  pagamento: { parcelas: { numero: string | null; vencimento: string; valor: number }[]; formas: string[] };
}

export interface NovoProdutoNota {
  nome: string;
  codigoBarras: string | null;
  categoriaId: number;
  unidade: string;
  precoVenda: number;
  controlaValidade: boolean;
}

export interface DecisaoItemNota {
  numeroItem: number;
  acao: 'existente' | 'novo' | 'ignorar';
  produtoId: number | null;
  novo: NovoProdutoNota | null;
  fator: number;
  validade: string | null;
}

export interface RegistroNota {
  notaId: number;
  itensLancados: number;
  itensIgnorados: number;
  produtosCriados: number;
  fornecedorCriado: boolean;
  fornecedor: string;
  pedidoRecebidoId: number | null;
  contasCriadas: number;
}

export interface NotaEntrada {
  id: number;
  chave: string;
  numero: string;
  serie: string;
  dataEmissao: string;
  valorTotal: number;
  quantidadeItens: number;
  fornecedor: string;
  pedidoCompraId: number | null;
  registradaEm: string;
  registradaPor: string;
}

export const URL_NOTAS = '/api/compras/notas';

@Injectable({ providedIn: 'root' })
export class NotasApi {
  private readonly http = inject(HttpClient);

  // Lê o XML e devolve o que o sistema entendeu (não grava nada).
  conferir(arquivo: File): Promise<ConferenciaNota> {
    const formulario = new FormData();
    formulario.append('arquivo', arquivo, arquivo.name);
    return firstValueFrom(this.http.post<ConferenciaNota>(`${URL_NOTAS}/conferencia`, formulario));
  }

  // Grava: o XML vai de novo (o servidor confere tudo outra vez) junto com as decisões de cada item.
  registrar(arquivo: File, pedidoCompraId: number | null, itens: DecisaoItemNota[], gerarContas: boolean, jaPaga: boolean): Promise<RegistroNota> {
    const formulario = new FormData();
    formulario.append('arquivo', arquivo, arquivo.name);
    formulario.append('dados', JSON.stringify({ pedidoCompraId, itens, gerarContas, jaPaga }));
    return firstValueFrom(this.http.post<RegistroNota>(URL_NOTAS, formulario));
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Formatos iguais aos DTOs da API (Contracts/ProdutoDtos.cs).
export interface Produto {
  id: number;
  codigoBarras: string | null;
  nome: string;
  categoriaId: number;
  categoria: string;
  unidade: string;
  precoCusto: number;
  precoVenda: number;
  margemPercentual: number;
  estoqueAtual: number;
  estoqueMinimo: number;
  estoqueBaixo: boolean;
  controlaValidade: boolean;
  ativo: boolean;
  fornecedorId: number | null;
  fornecedor: string | null;
}

export interface ProdutoRequest {
  codigoBarras: string | null;
  nome: string;
  categoriaId: number;
  unidade: string;
  precoCusto: number;
  precoVenda: number;
  estoqueMinimo: number;
  controlaValidade: boolean;
  fornecedorId: number | null;
}

export interface Categoria {
  id: number;
  nome: string;
  ativa: boolean;
  quantidadeProdutos: number;
}

export interface Pagina<T> {
  itens: T[];
  total: number;
  paginaAtual: number;
  tamanhoPagina: number;
}

export const UNIDADES = [
  { sigla: 'UN', nome: 'Unidade' },
  { sigla: 'KG', nome: 'Quilo' },
  { sigla: 'L', nome: 'Litro' },
  { sigla: 'PCT', nome: 'Pacote' },
  { sigla: 'CX', nome: 'Caixa' },
  { sigla: 'DZ', nome: 'Dúzia' },
];

// Importação por planilha (Contracts/ImportacaoDtos.cs). Acao: o que aconteceria com a linha.
export type AcaoImportacao = 'Novo' | 'Atualizar' | 'Erro';

export interface LinhaPrevia {
  linha: number;
  acao: AcaoImportacao;
  codigoBarras: string | null;
  nome: string | null;
  categoria: string | null;
  unidade: string | null;
  precoCusto: number | null;
  precoVenda: number | null;
  estoqueMinimo: number | null;
  controlaValidade: boolean | null;
  estoqueInicial: number | null;
  validade: string | null; // "2026-10-20"
  erros: string[];
  avisos: string[];
}

export interface PreviaImportacao {
  arquivo: string;
  totalLinhas: number;
  novos: number;
  atualizados: number;
  comErro: number;
  comAviso: number;
  categoriasNovas: string[];
  errosGerais: string[];
  avisosGerais: string[];
  linhas: LinhaPrevia[];
}

export interface ResultadoImportacao {
  novos: number;
  atualizados: number;
  ignorados: number;
  categoriasCriadas: string[];
  produtosComEstoqueInicial: number;
}

export const URL_MODELO_IMPORTACAO = '/api/produtos/importacao/modelo';

// As LEITURAS da lista são feitas com httpResource (direto na tela, reagindo aos filtros).
// Aqui ficam as AÇÕES: buscar um, salvar, desativar, reativar.
@Injectable({ providedIn: 'root' })
export class ProdutosApi {
  private readonly http = inject(HttpClient);

  obter(id: number): Promise<Produto> {
    return firstValueFrom(this.http.get<Produto>(`/api/produtos/${id}`));
  }

  criar(dados: ProdutoRequest): Promise<Produto> {
    return firstValueFrom(this.http.post<Produto>('/api/produtos', dados));
  }

  atualizar(id: number, dados: ProdutoRequest): Promise<Produto> {
    return firstValueFrom(this.http.put<Produto>(`/api/produtos/${id}`, dados));
  }

  desativar(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/produtos/${id}`));
  }

  reativar(id: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/produtos/${id}/reativar`, {}));
  }

  // Envia a planilha e recebe a PRÉVIA (o servidor não grava nada).
  previaImportacao(arquivo: File): Promise<PreviaImportacao> {
    const formulario = new FormData();
    formulario.append('arquivo', arquivo, arquivo.name);
    return firstValueFrom(this.http.post<PreviaImportacao>('/api/produtos/importacao/previa', formulario));
  }

  // GRAVA: o servidor confere a planilha de novo e grava só as linhas sem erro.
  importar(arquivo: File): Promise<ResultadoImportacao> {
    const formulario = new FormData();
    formulario.append('arquivo', arquivo, arquivo.name);
    return firstValueFrom(this.http.post<ResultadoImportacao>('/api/produtos/importacao', formulario));
  }
}

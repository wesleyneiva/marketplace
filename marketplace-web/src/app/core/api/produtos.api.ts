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
  estoqueInicial: number;
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
}

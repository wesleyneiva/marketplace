import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Pagina } from './produtos.api';

// Grade de tamanho e cor. Formatos iguais a Contracts/ModeloDtos.cs.
// Cada variação (tamanho + cor) é um produto comum: venda, estoque e caixa continuam por produto.
export interface ModeloResumo {
  id: number;
  nome: string;
  referencia: string | null;
  marca: string | null;
  categoriaId: number;
  categoria: string;
  fornecedor: string | null;
  precoMinimo: number;
  precoMaximo: number;
  variacoes: number;
  estoqueTotal: number;
  variacoesZeradas: number;
  ativo: boolean;
  tamanhos: string[];
  cores: string[];
}

export interface Variacao {
  produtoId: number;
  nome: string;
  tamanho: string;
  cor: string;
  codigoBarras: string | null;
  precoCusto: number;
  precoVenda: number;
  estoqueAtual: number;
  estoqueMinimo: number;
  ativo: boolean;
}

export interface ModeloDetalhe {
  id: number;
  nome: string;
  referencia: string | null;
  marca: string | null;
  categoriaId: number;
  categoria: string;
  fornecedorId: number | null;
  fornecedor: string | null;
  unidade: string;
  ncm: string | null;
  tamanhos: string[];
  cores: string[];
  ativo: boolean;
  variacoes: Variacao[];
}

export interface Combinacao { tamanho: string; cor: string; codigoBarras?: string | null }

export interface NovoModelo {
  nome: string;
  referencia: string | null;
  marca: string | null;
  categoriaId: number;
  fornecedorId: number | null;
  unidade: string;
  precoCusto: number;
  precoVenda: number;
  estoqueMinimo: number;
  ncm: string | null;
  tamanhos: string[];
  cores: string[];
  combinacoes: Combinacao[] | null; // null = todas
}

export interface EditarModelo {
  nome: string;
  referencia: string | null;
  marca: string | null;
  categoriaId: number;
  fornecedorId: number | null;
  ncm: string | null;
  aplicarPrecos: boolean;
  precoCusto: number;
  precoVenda: number;
  estoqueMinimo: number;
}

// Grades prontas para começar (a loja ajusta: tira, põe e reordena).
export const GRADES_PRONTAS: { nome: string; tamanhos: string[] }[] = [
  { nome: 'Calçado adulto (34 a 45)', tamanhos: ['34', '35', '36', '37', '38', '39', '40', '41', '42', '43', '44', '45'] },
  { nome: 'Calçado infantil (25 a 33)', tamanhos: ['25', '26', '27', '28', '29', '30', '31', '32', '33'] },
  { nome: 'Roupa (PP a GG)', tamanhos: ['PP', 'P', 'M', 'G', 'GG'] },
  { nome: 'Empunhadura de raquete (L1 a L4)', tamanhos: ['L1', 'L2', 'L3', 'L4'] },
  { nome: 'Tamanho único', tamanhos: ['Único'] },
];

@Injectable({ providedIn: 'root' })
export class ModelosApi {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/modelos';

  listar(params: { busca?: string; categoriaId?: number | null; incluirInativos?: boolean; pagina?: number; tamanho?: number }): Promise<Pagina<ModeloResumo>> {
    const p: Record<string, string | number | boolean> = {};
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') p[k] = v;
    return firstValueFrom(this.http.get<Pagina<ModeloResumo>>(this.url, { params: p }));
  }

  obter(id: number): Promise<ModeloDetalhe> {
    return firstValueFrom(this.http.get<ModeloDetalhe>(`${this.url}/${id}`));
  }

  criar(m: NovoModelo): Promise<ModeloDetalhe> {
    return firstValueFrom(this.http.post<ModeloDetalhe>(this.url, m));
  }

  editar(id: number, m: EditarModelo): Promise<ModeloDetalhe> {
    return firstValueFrom(this.http.put<ModeloDetalhe>(`${this.url}/${id}`, m));
  }

  acrescentar(id: number, combinacoes: Combinacao[]): Promise<ModeloDetalhe> {
    return firstValueFrom(this.http.post<ModeloDetalhe>(`${this.url}/${id}/variacoes`, { combinacoes }));
  }

  entrada(id: number, itens: { produtoId: number; quantidade: number }[], custoUnitario: number | null, observacao: string | null): Promise<ModeloDetalhe> {
    return firstValueFrom(this.http.post<ModeloDetalhe>(`${this.url}/${id}/entrada`, { itens, custoUnitario, observacao }));
  }

  desativar(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.url}/${id}`));
  }

  reativar(id: number): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.url}/${id}/reativar`, {}));
  }
}

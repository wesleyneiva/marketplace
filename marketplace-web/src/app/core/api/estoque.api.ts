import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Formatos iguais aos DTOs da API (Contracts/EstoqueDtos.cs).
export type TipoMovimentacao = 'Entrada' | 'Venda' | 'Perda' | 'Ajuste' | 'Inventario';

export interface Movimentacao {
  id: number;
  dataHora: string;
  produtoId: number;
  produto: string;
  unidade: string;
  tipo: TipoMovimentacao;
  quantidade: number;
  estoqueAnterior: number;
  estoquePosterior: number;
  custoUnitario: number | null;
  motivo: string | null;
  observacao: string | null;
  validade: string | null; // "2026-10-20" (só a data)
  usuario: string;
}

export interface Lote {
  id: number;
  produtoId: number;
  produto: string;
  categoria: string;
  unidade: string;
  dataValidade: string;
  diasRestantes: number;
  situacao: 'Vencido' | 'Vence hoje' | 'Vencendo' | 'No prazo';
  quantidadeAtual: number;
  valorEmRisco: number;
}

export interface EntradaRequest {
  produtoId: number;
  quantidade: number;
  custoUnitario: number | null;
  validade: string | null;
  observacao: string | null;
}

export interface PerdaRequest {
  produtoId: number;
  quantidade: number;
  motivo: string;
  loteId: number | null;
  observacao: string | null;
}

export interface AjusteRequest {
  produtoId: number;
  quantidadeContada: number;
  validade: string | null;
  observacao: string;
}

export const TIPOS_MOVIMENTACAO: { valor: TipoMovimentacao; rotulo: string; icone: string }[] = [
  { valor: 'Entrada', rotulo: 'Entrada', icone: '📥' },
  { valor: 'Venda', rotulo: 'Venda', icone: '🧾' },
  { valor: 'Perda', rotulo: 'Perda', icone: '🗑️' },
  { valor: 'Ajuste', rotulo: 'Ajuste', icone: '⚖️' },
  { valor: 'Inventario', rotulo: 'Inventário', icone: '📋' },
];

@Injectable({ providedIn: 'root' })
export class EstoqueApi {
  private readonly http = inject(HttpClient);

  entrada(dados: EntradaRequest): Promise<Movimentacao> {
    return firstValueFrom(this.http.post<Movimentacao>('/api/estoque/entradas', dados));
  }

  perda(dados: PerdaRequest): Promise<Movimentacao> {
    return firstValueFrom(this.http.post<Movimentacao>('/api/estoque/perdas', dados));
  }

  ajuste(dados: AjusteRequest): Promise<Movimentacao> {
    return firstValueFrom(this.http.post<Movimentacao>('/api/estoque/ajustes', dados));
  }

  lotesDoProduto(produtoId: number): Promise<Lote[]> {
    return firstValueFrom(this.http.get<Lote[]>(`/api/estoque/produtos/${produtoId}/lotes`));
  }
}

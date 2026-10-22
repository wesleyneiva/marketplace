import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Autorizacao } from './pdv.api';

// Troca e vale-troca (API em Controllers/TrocasController.cs).
export const MOTIVOS_TROCA = [
  { valor: 'Tamanho', rotulo: 'Tamanho / número' },
  { valor: 'Defeito', rotulo: 'Defeito' },
  { valor: 'Gosto', rotulo: 'Não gostou' },
  { valor: 'Outro', rotulo: 'Outro' },
] as const;

export interface ItemParaTroca {
  itemVendaId: number; produtoId: number; descricao: string; unidade: string;
  quantidade: number; jaDevolvido: number; disponivel: number; creditoUnitario: number;
}

export interface VendaParaTroca {
  vendaId: number; dataHora: string; numeroCaixa: number; total: number; dias: number;
  prazoDias: number; dentroDoPrazo: boolean; bloqueio: string | null; itens: ItemParaTroca[];
}

export interface VendaComProduto { vendaId: number; dataHora: string; numeroCaixa: number; total: number; itens: string; dentroDoPrazo: boolean }

export interface ValeTroca {
  id: number; codigo: string; valor: number; saldo: number; validade: string; cliente: string | null;
  situacao: 'Disponivel' | 'Usado' | 'Vencido'; criadoEm: string; trocaId: number | null; vendaId: number | null;
}

export interface Troca {
  id: number; vendaId: number; dataHora: string; numeroCaixa: number; operador: string; motivo: string; observacao: string | null;
  valor: number; foraDoPrazo: boolean; vale: ValeTroca; itens: { produtoId: number; descricao: string; quantidade: number; valor: number }[];
}

export interface NovaTroca {
  vendaId: number; itens: { itemVendaId: number; quantidade: number }[]; motivo: string;
  observacao: string | null; cliente: string | null; autorizacao: Autorizacao | null;
}

export interface RelatorioTrocas {
  de: string; ate: string; quantidade: number; valor: number;
  porMotivo: { motivo: string; quantidade: number; valor: number }[];
  produtos: { produto: string; quantidade: number; valor: number; trocas: number }[];
  trocas: Troca[];
  valesEmAberto: number; saldoEmAberto: number; vencidosNoPeriodo: number; vales: ValeTroca[];
}

@Injectable({ providedIn: 'root' })
export class TrocasApi {
  private readonly http = inject(HttpClient);

  venda(id: number): Promise<VendaParaTroca> {
    return firstValueFrom(this.http.get<VendaParaTroca>(`/api/trocas/venda/${id}`));
  }

  vendasComProduto(produtoId: number): Promise<VendaComProduto[]> {
    return firstValueFrom(this.http.get<VendaComProduto[]>(`/api/trocas/vendas-com-produto/${produtoId}`));
  }

  registrar(t: NovaTroca): Promise<Troca> {
    return firstValueFrom(this.http.post<Troca>('/api/trocas', t));
  }

  vale(codigo: string): Promise<ValeTroca> {
    return firstValueFrom(this.http.get<ValeTroca>(`/api/trocas/vale/${encodeURIComponent(codigo.trim())}`));
  }
}

export function rotuloMotivo(m: string): string {
  return MOTIVOS_TROCA.find((x) => x.valor === m)?.rotulo ?? m;
}

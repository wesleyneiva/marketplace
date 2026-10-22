import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// Clientes da loja (Controllers/ClientesController.cs) e vendedores (VendedoresController.cs).
export interface ClienteBuscaPdv {
  id: number; nome: string; apelido: string | null; telefoneFinal: string | null; compras: number; ultimaCompra: string | null;
  raquete: string | null; corda: string | null; tensao: string | null; aniversarioHoje: boolean;
}

export interface Cliente {
  id: number; nome: string; apelido: string | null; telefone: string | null; cpf: string | null; email: string | null;
  endereco: string | null; nascimento: string | null; raquete: string | null; corda: string | null; tensao: string | null;
  observacao: string | null; ativo: boolean; simulado: boolean; criadoEm: string;
}

export interface ClienteListaItem {
  id: number; nome: string; apelido: string | null; telefone: string | null; nascimento: string | null; raquete: string | null;
  compras: number; totalGasto: number; ultimaCompra: string | null; ativo: boolean; simulado: boolean;
}

export interface ClientesPagina {
  itens: ClienteListaItem[]; total: number; pagina: number; tamanho: number;
  ativos: number; aniversariantesDoMes: number; comprouNosUltimos30Dias: number;
}

export interface ClienteDetalhe {
  cliente: Cliente; compras: number; totalGasto: number; ticketMedio: number; primeiraCompra: string | null; ultimaCompra: string | null;
  diasEntreCompras: number | null; diasSemComprar: number | null;
  maisComprados: { produto: string; quantidade: number; vezes: number; ultimaVez: string }[];
  ultimasCompras: { vendaId: number; dataHora: string; itens: string; quantidadeItens: number; total: number; formas: string; vendedor: string | null; status: string }[];
  trocas: { id: number; dataHora: string; vendaId: number; itens: string; motivo: string; valor: number; valeCodigo: string; valeSaldo: number; valeSituacao: string }[];
  ordens: { id: number; numero: number; criadaEm: string; raquete: string; corda: string | null; tensao: string | null; servico: string; status: string; valor: number }[];
}

export type ClienteCadastro = Omit<Cliente, 'id' | 'ativo' | 'simulado' | 'criadoEm'>;

export interface Vendedor { id: number; nome: string; comissaoPercentual: number; ativo: boolean }

export interface ComissaoMes {
  mes: string; inicio: string; fim: string; mesFechado: boolean; total: number; vendasSemVendedor: number; vendidoSemVendedor: number;
  vendedores: { vendedorId: number; nome: string; percentual: number; vendas: number; vendido: number; comissao: number;
    estornoTrocas: number; total: number; lancada: boolean; contaPagarId: number | null; valorLancado: number | null }[];
}

@Injectable({ providedIn: 'root' })
export class ClientesApi {
  private readonly http = inject(HttpClient);

  buscar(termo: string): Promise<ClienteBuscaPdv[]> {
    return firstValueFrom(this.http.get<ClienteBuscaPdv[]>('/api/clientes/buscar', { params: { termo } }));
  }

  rapido(c: { nome: string; telefone: string | null; cpf: string | null }): Promise<ClienteBuscaPdv> {
    return firstValueFrom(this.http.post<ClienteBuscaPdv>('/api/clientes/rapido', c));
  }

  vendedores(todos = false): Promise<Vendedor[]> {
    return firstValueFrom(this.http.get<Vendedor[]>('/api/vendedores', { params: { todos } }));
  }
}

// Formatos iguais aos DTOs da API (Contracts/ComprasDtos.cs).
export interface Fornecedor {
  id: number; nome: string; cnpj: string | null; contato: string | null; telefone: string | null; email: string | null;
  prazoEntregaDias: number; observacao: string | null; ativo: boolean; produtos: number; pedidosAbertos: number;
}

export interface ItemSugestao {
  produtoId: number; produto: string; unidade: string; estoqueAtual: number; estoqueMinimo: number;
  consumoDiario: number; diasDeEstoque: number | null; jaPedido: number; sugerido: number; custoUnitario: number; motivo: string;
}

export interface SugestaoFornecedor {
  fornecedorId: number; fornecedor: string; prazoEntregaDias: number; total: number; itens: ItemSugestao[];
}

export interface ItemPedido {
  id: number; produtoId: number; produto: string; unidade: string; perecivel: boolean; quantidade: number;
  custoUnitario: number; quantidadeRecebida: number | null; validade: string | null; total: number;
}

export type StatusPedido = 'Rascunho' | 'Enviado' | 'Recebido' | 'Cancelado';

export interface Pedido {
  id: number; fornecedorId: number; fornecedor: string; status: StatusPedido; automatico: boolean; observacao: string | null;
  criadoEm: string; criadoPor: string; enviadoEm: string | null; previsaoEntrega: string | null;
  recebidoEm: string | null; recebidoPor: string | null; total: number; atrasado: boolean; itens: ItemPedido[];
}

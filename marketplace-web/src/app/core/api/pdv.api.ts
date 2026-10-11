import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Formatos iguais aos DTOs da API (Contracts/PdvDtos.cs).
export type FormaPagamento = 'Dinheiro' | 'Pix' | 'Debito' | 'Credito' | 'Fiado';

export const FORMAS_PAGAMENTO: { valor: FormaPagamento; rotulo: string; icone: string; tecla: string }[] = [
  { valor: 'Dinheiro', rotulo: 'Dinheiro', icone: 'banknote', tecla: '1' },
  { valor: 'Pix', rotulo: 'Pix', icone: 'qr-code', tecla: '2' },
  { valor: 'Debito', rotulo: 'Débito', icone: 'credit-card', tecla: '3' },
  { valor: 'Credito', rotulo: 'Crédito', icone: 'credit-card', tecla: '4' },
  { valor: 'Fiado', rotulo: 'Fiado', icone: 'notebook-pen', tecla: '5' },
];

// Formas para RECEBER um fiado (o cliente paga a dívida): fiado não paga fiado.
export const FORMAS_RECEBER = FORMAS_PAGAMENTO.filter((f) => f.valor !== 'Fiado');

export interface ResumoCaixa {
  sessaoId: number;
  numeroCaixa: number;
  operador: string;
  status: 'Aberta' | 'Fechada';
  abertaEm: string;
  fechadaEm: string | null;
  valorAbertura: number;
  quantidadeVendas: number;
  totalVendido: number;
  ticketMedio: number;
  porForma: { forma: FormaPagamento; valor: number }[];
  trocoDado: number;
  sangrias: number;
  suprimentos: number;
  dinheiroEsperado: number;
  vendasCanceladas: number;
  valorCancelado: number;
  valorContado: number | null;
  diferenca: number | null;
  observacaoFechamento: string | null;
  recebimentosFiado?: { forma: FormaPagamento; valor: number }[]; // clientes que pagaram o fiado neste caixa
}

export interface Cupom {
  id: number;
  dataHora: string;
  numeroCaixa: number;
  operador: string;
  status: 'Concluida' | 'Cancelada';
  subtotal: number;
  desconto: number;
  total: number;
  valorPago: number;
  troco: number;
  itens: { produtoId: number; descricao: string; unidade: string; quantidade: number; precoUnitario: number; total: number }[];
  pagamentos: { forma: FormaPagamento; valor: number }[];
  motivoCancelamento: string | null;
  // No fiado: quanto o cliente deve AGORA e quando esta compra vence (sai no cupom para assinar).
  cliente?: { nome: string; apelido: string | null; saldoDevedor: number | null; vencimentoFiado: string | null } | null;
}

// Busca de cliente no PDV (só o necessário: telefone com os 4 últimos dígitos).
export interface ClientePdv {
  id: number;
  nome: string;
  apelido: string | null;
  telefoneFinal: string | null;
  saldo: number;          // negativo = crédito do cliente
  limiteCredito: number;
  disponivel: number;
  vencido: number;
  diasAtraso: number;
  bloqueado: boolean;
  restricao: string | null; // por que só gerente libera (nulo = pode comprar)
}

export interface ReciboFiado {
  cliente: string;
  valor: number;
  forma: FormaPagamento;
  saldoAnterior: number;
  saldoAtual: number;
  dataHora: string;
  operador: string;
}

export interface ResumoVendas {
  data: string;
  quantidadeVendas: number;
  totalVendido: number;
  ticketMedio: number;
  lucroBruto: number;
  porForma: { forma: FormaPagamento; valor: number }[];
  porHora: { hora: number; quantidade: number; total: number }[];
}

export interface NovaVenda {
  itens: { produtoId: number; quantidade: number }[];
  desconto: number;
  pagamentos: { forma: FormaPagamento; valor: number }[];
  clienteId?: number | null; // obrigatório com fiado
}

export interface CaixaDisponivel {
  numero: number;
  ocupadoPor: string | null;
}

@Injectable({ providedIn: 'root' })
export class PdvApi {
  private readonly http = inject(HttpClient);

  // 204 (sem caixa aberto) chega como null.
  caixaAtual(): Promise<ResumoCaixa | null> {
    return firstValueFrom(this.http.get<ResumoCaixa | null>('/api/caixa/atual'));
  }

  // Os caixas da empresa (1 até o limite do plano) e quem está em cada um.
  caixas(): Promise<CaixaDisponivel[]> {
    return firstValueFrom(this.http.get<CaixaDisponivel[]>('/api/caixa/caixas'));
  }

  abrir(numeroCaixa: number, valorAbertura: number): Promise<ResumoCaixa> {
    return firstValueFrom(this.http.post<ResumoCaixa>('/api/caixa/abrir', { numeroCaixa, valorAbertura }));
  }

  movimentar(tipo: 'sangria' | 'suprimento', valor: number, motivo: string): Promise<ResumoCaixa> {
    return firstValueFrom(this.http.post<ResumoCaixa>(`/api/caixa/${tipo}`, { valor, motivo }));
  }

  fechar(valorContado: number, observacao: string | null): Promise<ResumoCaixa> {
    return firstValueFrom(this.http.post<ResumoCaixa>('/api/caixa/fechar', { valorContado, observacao }));
  }

  vender(venda: NovaVenda): Promise<Cupom> {
    return firstValueFrom(this.http.post<Cupom>('/api/vendas', venda));
  }

  buscarClientes(termo: string): Promise<ClientePdv[]> {
    return firstValueFrom(this.http.get<ClientePdv[]>('/api/fiado/pdv/buscar', { params: { termo } }));
  }

  receberFiado(clienteId: number, valor: number, forma: FormaPagamento): Promise<ReciboFiado> {
    return firstValueFrom(this.http.post<ReciboFiado>('/api/fiado/pdv/receber', { clienteId, valor, forma }));
  }
}

// Dinheiro com 2 casas, igual à API (0,005 arredonda para cima). O "EPSILON" evita o clássico
// problema do JavaScript: 1.005 * 100 = 100.49999999999999.
export function arredondar(valor: number): number {
  return Math.round((valor + Number.EPSILON) * 100) / 100;
}

// "1,235" → 1.235 · "1.234,56" → 1234.56 · "2.5" → 2.5  (aceita vírgula, como o brasileiro digita)
export function lerNumero(texto: string | number | null | undefined): number {
  if (typeof texto === 'number') return texto;
  const t = String(texto ?? '').trim();
  // Com vírgula: o ponto é separador de milhar (some) e a vírgula vira o ponto decimal.
  const normal = t.includes(',') ? t.replace(/\./g, '').replace(',', '.') : t;
  const n = Number(normal);
  return t !== '' && Number.isFinite(n) ? n : NaN;
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Formatos iguais aos DTOs da API (Contracts/PdvDtos.cs).
export type FormaPagamento = 'Dinheiro' | 'Pix' | 'Debito' | 'Credito' | 'Fiado' | 'ValeTroca';

export const FORMAS_PAGAMENTO: { valor: FormaPagamento; rotulo: string; icone: string; tecla: string }[] = [
  { valor: 'Dinheiro', rotulo: 'Dinheiro', icone: 'banknote', tecla: '1' },
  { valor: 'Pix', rotulo: 'Pix', icone: 'qr-code', tecla: '2' },
  { valor: 'Debito', rotulo: 'Débito', icone: 'credit-card', tecla: '3' },
  { valor: 'Credito', rotulo: 'Crédito', icone: 'credit-card', tecla: '4' },
  { valor: 'Fiado', rotulo: 'Fiado', icone: 'notebook-pen', tecla: '5' },
  { valor: 'ValeTroca', rotulo: 'Vale-troca', icone: 'ticket', tecla: '6' },
];

// Formas para RECEBER um fiado (o cliente paga a dívida): fiado não paga fiado, nem vale-troca.
export const FORMAS_RECEBER = FORMAS_PAGAMENTO.filter((f) => f.valor !== 'Fiado' && f.valor !== 'ValeTroca');

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
  devolucoes?: number; // dinheiro devolvido daqui em estornos de vendas de caixas já fechados
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
  // pixConfirmado: Pix automático (Mercado Pago ou banco). vale*: vale-troca usado (código, saldo que ficou e validade, para o cupom).
  pagamentos: { forma: FormaPagamento; valor: number; pixConfirmado?: boolean; valeCodigo?: string | null; valeSaldo?: number | null; valeValidade?: string | null; parcelas?: number }[];
  vendedor?: string | null; // quem atendeu
  motivoCancelamento: string | null;
  cancelamento?: { em: string | null; por: string | null; autorizadoPor: string | null } | null;
  offline?: boolean;       // feita sem internet (caixa offline)
  idLocal?: string | null; // enquanto não foi enviada, a venda offline só tem este número (id = 0)
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
  pagamentos: { forma: FormaPagamento; valor: number; cobrancaPixId?: number; valeTrocaId?: number; parcelas?: number }[]; // Pix automático / vale-troca / crédito em N vezes
  clienteId?: number | null; // cliente identificado (obrigatório com fiado)
  vendedorId?: number | null; // quem atendeu (comissão)
  idLocal?: string;          // criado no caixa: a mesma venda nunca entra duas vezes (ver caixa offline)
}

// ----- Vendas (lista), estorno e autorização do gerente -----
export interface VendaLista {
  id: number;
  dataHora: string;
  numeroCaixa: number;
  operador: string;
  status: 'Concluida' | 'Cancelada';
  quantidadeItens: number;
  total: number;
  formas: string;            // "Dinheiro + Pix"
  pixConfirmado: boolean;    // tem Pix confirmado automaticamente (Mercado Pago ou banco)
  origem: 'Caixa' | 'Simulador' | 'Historico';
  offline: boolean;          // feita sem internet e enviada depois
  precoDivergente: boolean;  // offline com preço diferente do cadastro na hora em que chegou
}

export interface ListaVendas {
  itens: VendaLista[];
  total: number;
  paginaAtual: number;
  tamanhoPagina: number;
  concluidas: number;
  totalVendido: number;
  ticketMedio: number;
  canceladas: number;
  valorCancelado: number;
  caixas: number[];
}

export interface EstornoPrevia {
  pode: boolean;
  bloqueio: string | null;
  caixaAberto: boolean;
  precisaAutorizacao: boolean;
  formas: { forma: FormaPagamento; valor: number; como: string; pixAutomatico: boolean; pixPor: string | null }[];
  aviso: string | null;
}

export type OpcaoPixEstorno = 'Devolver' | 'Manter' | 'PorFora';

export interface Gerente { id: string; nome: string; }

// Senha do gerente digitada no caixa (vazio quando quem está logado já é gerente).
export interface Autorizacao { gerenteId: string; senha: string; }

export interface CancelamentoRegistro {
  id: number;
  dataHora: string;
  acao: 'CancelarItem' | 'CancelarCompra' | 'CancelarVenda' | 'TrocaForaPrazo';
  numeroCaixa: number | null;
  operador: string;
  autorizadoPor: string;
  motivo: string;
  descricao: string;
  valor: number;
  vendaId: number | null;
  semConferencia: boolean; // feito sem internet: a senha do gerente não pôde ser conferida
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

  // ----- Vendas, estorno e autorização do gerente -----
  vendas(filtro: { data?: string; sessaoId?: number; caixa?: number | null; forma?: string; situacao?: string; pagina?: number; tamanho?: number }): Promise<ListaVendas> {
    const params: Record<string, string | number> = {};
    for (const [k, v] of Object.entries(filtro)) if (v !== undefined && v !== null && v !== '') params[k] = v;
    return firstValueFrom(this.http.get<ListaVendas>('/api/vendas', { params }));
  }

  venda(id: number): Promise<Cupom> {
    return firstValueFrom(this.http.get<Cupom>(`/api/vendas/${id}`));
  }

  previaEstorno(id: number): Promise<EstornoPrevia> {
    return firstValueFrom(this.http.get<EstornoPrevia>(`/api/vendas/${id}/estorno`));
  }

  cancelarVenda(id: number, motivo: string, autorizacao: Autorizacao | null, pix: OpcaoPixEstorno | null): Promise<Cupom> {
    return firstValueFrom(this.http.post<Cupom>(`/api/vendas/${id}/cancelar`, { motivo, autorizacao, pix }));
  }

  cancelamentos(data: string, caixa?: number | null): Promise<CancelamentoRegistro[]> {
    return firstValueFrom(this.http.get<CancelamentoRegistro[]>('/api/vendas/cancelamentos', { params: caixa ? { data, caixa } : { data } }));
  }

  gerentes(): Promise<Gerente[]> {
    return firstValueFrom(this.http.get<Gerente[]>('/api/caixa/gerentes'));
  }

  // Tirar um item ou cancelar a compra em andamento (antes de pagar): só registra, com a autorização do gerente.
  cancelarNoPdv(tipo: 'Item' | 'Compra', itens: { produtoId: number; quantidade: number }[], motivo: string,
    autorizacao: Autorizacao | null): Promise<{ autorizadoPor: string; valor: number }> {
    return firstValueFrom(this.http.post<{ autorizadoPor: string; valor: number }>('/api/caixa/cancelamentos', { tipo, itens, motivo, autorizacao }));
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

// Formatos iguais a Contracts/FiadoDtos.cs.

export interface ClienteFiado {
  id: number;
  nome: string;
  apelido: string | null;
  telefone: string | null; // só dígitos, com DDD
  cpf: string | null;
  endereco: string | null;
  observacao: string | null;
  limiteCredito: number;
  diaPagamento: number;
  bloqueado: boolean;
  ativo: boolean;
  saldo: number;           // negativo = crédito do cliente
  disponivel: number;
  vencido: number;
  diasAtraso: number;
  proximoVencimento: string | null;
  valorProximo: number;
  ultimaCompra: string | null;
  ultimoPagamento: string | null;
}

export type TipoLancamentoFiado = 'Compra' | 'Pagamento' | 'Estorno' | 'Baixa';

export interface LancamentoFiado {
  id: number;
  dataHora: string;
  tipo: TipoLancamentoFiado;
  valor: number;
  vencimento: string | null;
  vendaId: number | null;
  forma: string | null;
  noCaixa: boolean;
  observacao: string | null;
  usuario: string | null;
  saldoApos: number;
  podeDesfazer: boolean;
}

export interface Caderneta {
  cliente: ClienteFiado;
  lancamentos: LancamentoFiado[]; // mais recente primeiro
}

export interface ResumoFiado {
  totalAReceber: number;
  vencido: number;
  clientesComSaldo: number;
  clientesEmAtraso: number;
  clientesAtrasoGrave: number;
  venceProximos7Dias: number;
  vendidoNoMes: number;
  recebidoNoMes: number;
  clientesAtivos: number;
  limiteTotal: number;
}

export const URL_FIADO = '/api/fiado';

export const DIAS_TOLERANCIA = 10; // igual a FiadoService.DiasDeTolerancia

// Telefone para mostrar: 51998765432 → (51) 99876-5432
export function telefoneBonito(t: string | null): string {
  if (!t) return '';
  const d = t.replace(/\D/g, '').replace(/^55(?=\d{10,11}$)/, '');
  if (d.length === 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
  if (d.length === 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
  return t;
}

// Link do WhatsApp com a mensagem de cobrança PRONTA (quem envia é a pessoa, no WhatsApp dela).
export function linkCobranca(c: ClienteFiado, loja: string): string | null {
  const d = (c.telefone ?? '').replace(/\D/g, '');
  if (d.length < 10) return null;
  const numero = d.length <= 11 ? '55' + d : d;
  const real = (v: number) => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const dia = (iso: string) => iso.split('-').reverse().slice(0, 2).join('/');
  const nome = comoChamar(c);
  let texto = `Olá, ${nome}! Aqui é do ${loja}. 😊\nSua conta no fiado está em ${real(c.saldo)}`;
  if (c.vencido > 0) texto += `, sendo ${real(c.vencido)} já vencido${c.diasAtraso ? ` há ${c.diasAtraso} dia(s)` : ''}`;
  else if (c.proximoVencimento) texto += `, com vencimento dia ${dia(c.proximoVencimento)}`;
  texto += '.\nQuando puder, passa aqui para acertar? Também aceitamos PIX. Obrigado!';
  return `https://wa.me/${numero}?text=${encodeURIComponent(texto)}`;
}

// Como chamar o cliente na mensagem: "Seu João" / "Dona Maria" (tratamento + nome) ou só o primeiro nome.
function comoChamar(c: ClienteFiado): string {
  const apelido = (c.apelido ?? '').trim().split(/\s+/);
  if (/^(seu|dona|sr\.?|sra\.?|dr\.?|dra\.?)$/i.test(apelido[0] ?? '') && apelido[1]) return `${apelido[0]} ${apelido[1]}`;
  return c.nome.trim().split(/\s+/)[0];
}

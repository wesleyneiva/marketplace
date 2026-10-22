import { HttpErrorResponse } from '@angular/common/http';

// Formatos iguais a Contracts/FinanceiroDtos.cs.

export type TipoConta = 'Dinheiro' | 'Banco' | 'Maquininha';
export type Natureza = 'Entrada' | 'Saida';
export type TipoMovimento = 'Entrada' | 'Saida';
export type OrigemMovimento = 'Manual' | 'ContaPagar' | 'Transferencia' | 'Importacao' | 'Caixa' | 'Recebimento';
export type Grupo =
  | 'ReceitaVendas' | 'OutrasReceitas' | 'ImpostosVendas' | 'ComprasMercadorias' | 'Pessoal'
  | 'Ocupacao' | 'Administrativas' | 'DespesasVendas' | 'Financeiras' | 'ForaDoResultado';

export interface ContaFinanceira {
  id: number;
  nome: string;
  tipo: TipoConta;
  banco: string | null;
  agencia: string | null;
  numero: string | null;
  saldoInicial: number;
  saldoInicialEm: string;
  saldo: number;
  movimentos: number;
  doSistema: boolean;
  ativa: boolean;
}

export interface GrupoFinanceiro {
  grupo: Grupo;
  numero: number;
  nome: string;
  natureza: Natureza | null; // nulo = "Fora do resultado" (cada categoria diz se é entrada ou saída)
  explicacao: string;
}

export interface CategoriaFinanceira {
  id: number;
  codigo: string;
  nome: string;
  grupo: Grupo;
  nomeGrupo: string;
  natureza: Natureza;
  doSistema: boolean;
  ativa: boolean;
  usos: number;
}

export interface Movimento {
  id: number;
  data: string;
  tipo: TipoMovimento;
  valor: number;
  contaId: number;
  conta: string;
  categoriaId: number | null;
  categoria: string | null;
  codigoCategoria: string | null;
  descricao: string;
  documento: string | null;
  origem: OrigemMovimento;
  contaPagarId: number | null;
  transferenciaId: string | null;
  conciliado: boolean;
  criadoPor: string | null;
  saldoApos: number | null;
}

export interface Extrato {
  de: string;
  ate: string;
  entradas: number;
  saidas: number;
  saldoAnterior: number | null;
  saldoFinal: number | null;
  cortado: boolean;
  movimentos: Movimento[];
  implantacao: { data: string; valor: number } | null;
}

// ----- Recebíveis (maquininha) -----

export type FormaEletronica = 'Pix' | 'Debito' | 'Credito';

export interface Recebivel {
  id: number;
  sessaoCaixaId: number;
  numeroCaixa: number;
  forma: FormaEletronica;
  dataVenda: string;
  previsao: string;
  valorBruto: number;
  taxaPercentual: number;
  taxa: number;
  valorLiquido: number;
  conta: string;
  status: 'Previsto' | 'Recebido';
  recebidoEm: string | null;
  parcela: number;  // crédito parcelado: parcela X de Y
  parcelas: number;
}

export interface ResumoRecebiveis {
  hoje: number;
  amanha: number;
  proximos7Dias: number;
  proximos30Dias: number;
  totalPrevisto: number;
  quantidadePrevistos: number;
  vendasEletronicasNoMes: number;
  taxasNoMes: number;
  proximosDias: { data: string; liquido: number }[];
}

export interface ConfiguracaoRecebimento {
  forma: FormaEletronica;
  taxaPercentual: number;
  prazoDias: number;
  diasUteis: boolean;
  contaFinanceiraId: number;
  conta: string;
  exemploHoje: string;
}

export interface CaixaAberto {
  sessaoId: number;
  numeroCaixa: number;
  operador: string;
  abertaEm: string;
  vendas: number;
  total: number;
  dinheiroNaGaveta: number;
  porForma: { forma: string; valor: number }[];
}

export const NOME_FORMA: Record<string, string> = { Dinheiro: 'Dinheiro', Pix: 'PIX', Debito: 'Débito', Credito: 'Crédito' };
// Nomes de ícones (shared/icones.ts), desenhados com <app-icone [nome]="...">. Não use dentro de <option>.
export const ICONE_FORMA: Record<string, string> = { Dinheiro: 'banknote', Pix: 'qr-code', Debito: 'credit-card', Credito: 'credit-card' };

export const URL_FINANCEIRO = '/api/financeiro';

export const ICONE_CONTA: Record<TipoConta, string> = { Dinheiro: 'banknote', Banco: 'landmark', Maquininha: 'credit-card' };

export const NOME_TIPO_CONTA: Record<TipoConta, string> = {
  Dinheiro: 'Dinheiro (gaveta, cofre)',
  Banco: 'Banco (conta corrente, digital, poupança)',
  Maquininha: 'Maquininha (recebíveis de cartão)',
};

export const NOME_ORIGEM: Record<OrigemMovimento, string> = {
  Manual: 'lançamento',
  ContaPagar: 'conta paga',
  Transferencia: 'transferência',
  Importacao: 'importado',
  Caixa: 'caixa (PDV)',
  Recebimento: 'maquininha',
};

// Categorias agrupadas para o <select> com <optgroup> (só ativas e da natureza pedida).
export function categoriasPorGrupo(
  categorias: CategoriaFinanceira[], natureza: Natureza, manterId?: number | null,
): { grupo: string; itens: CategoriaFinanceira[] }[] {
  const grupos = new Map<string, CategoriaFinanceira[]>();
  for (const c of categorias) {
    if (c.natureza !== natureza || c.grupo === 'ReceitaVendas') continue;
    if (!c.ativa && c.id !== manterId) continue;
    const lista = grupos.get(c.nomeGrupo) ?? [];
    lista.push(c);
    grupos.set(c.nomeGrupo, lista);
  }
  return [...grupos].map(([grupo, itens]) => ({ grupo, itens }));
}

// A mensagem que a API mandou (ou uma genérica).
export function mensagemDeErro(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 403) return 'Sem permissão para esta ação.';
    if (e.error?.mensagem) return e.error.mensagem;
    const erros = e.error?.errors as Record<string, string[]> | undefined;
    if (erros) return Object.values(erros).flat().join(' ');
  }
  return 'Não foi possível concluir. Tente de novo.';
}

// "Hoje" no calendário do navegador, no formato do <input type="date"> (AAAA-MM-DD).
export function hojeIso(): string {
  return new Date().toLocaleDateString('sv-SE');
}

// Primeiro dia do mês de uma data AAAA-MM-DD, com deslocamento de meses (−1 = mês passado).
export function inicioDoMes(iso: string, deslocamento = 0): string {
  const [a, m] = iso.split('-').map(Number);
  const d = new Date(Date.UTC(a, m - 1 + deslocamento, 1));
  return d.toISOString().slice(0, 10);
}

export function fimDoMes(iso: string, deslocamento = 0): string {
  const [a, m] = iso.split('-').map(Number);
  const d = new Date(Date.UTC(a, m + deslocamento, 0));
  return d.toISOString().slice(0, 10);
}

// ----- Fluxo de caixa -----

export type SituacaoDia = 'ok' | 'minimo' | 'negativo';

export interface ItemFluxo {
  tipo: 'conta' | 'recebivel' | 'fiado' | 'estimativa' | 'caixa';
  descricao: string;
  valor: number; // + entra, − sai
  contaPagarId: number | null;
  vencida: boolean;
}

export interface DiaFluxo {
  data: string;
  recebiveis: number;
  estimado: number;
  saidas: number;
  saldoFinal: number;
  situacao: SituacaoDia;
  itens: ItemFluxo[];
}

export interface FluxoPrevisto {
  hoje: string;
  dias: number;
  comEstimativa: boolean;
  saldoHoje: number;
  saldoMinimo: number;
  totalRecebiveis: number;
  totalEstimado: number;
  totalSaidas: number;
  saldoFinal: number;
  menorSaldo: number;
  menorSaldoEm: string;
  primeiroNegativo: string | null;
  primeiroAbaixoMinimo: string | null;
  alerta: string | null;
  diasDeHistoricoNaEstimativa: number;
  diasPrevistos: DiaFluxo[];
  fiadoAtrasadoFora: number; // fiado já vencido: fica fora do previsto
}

export interface PeriodoFluxo {
  inicio: string;
  fim: string;
  rotulo: string;
  entradas: number;
  saidas: number;
  implantacao: number;
  saldoFinal: number;
}

export interface FluxoRealizado {
  de: string;
  ate: string;
  agrupar: 'dia' | 'semana' | 'mes';
  saldoInicial: number;
  entradas: number;
  saidas: number;
  saldoFinal: number;
  periodos: PeriodoFluxo[];
}

const SEMANA = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];

// "2026-10-13" → "ter 13/10" (sem passar por Date em UTC: o dia não "volta" para 12).
export function diaCurto(iso: string): string {
  const [a, m, d] = iso.split('-').map(Number);
  return `${SEMANA[new Date(a, m - 1, d).getDay()]} ${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}`;
}

// ----- DRE gerencial -----

export interface MesDre {
  mes: string;      // "2026-09"
  rotulo: string;   // "set/2026"
  inicio: string;
  fim: string;
  parcial: boolean; // mês em andamento
  dasPagoNoMes: number;
  comprasPagasNoMes: number;
}

export interface LinhaDre {
  chave: string;    // "receita-bruta", "cmv", "grupo:Pessoal", "categoria:12"...
  nome: string;
  nivel: number;    // 0 = total/subtotal, 1 = grupo, 2 = categoria
  tipo: 'receita' | 'deducao' | 'subtotal' | 'resultado';
  valores: number[]; // um por mês, com sinal (+ aumenta o resultado)
  detalhavel: boolean;
}

export interface Dre {
  meses: MesDre[];
  linhas: LinhaDre[];
  foraDoResultado: LinhaDre[];
  aliquotaSimples: number;
  aliquotaInformada: boolean;
}

export interface DetalheDre {
  titulo: string;
  observacao: string | null;
  itens: { data: string | null; descricao: string; complemento: string | null; valor: number }[];
}

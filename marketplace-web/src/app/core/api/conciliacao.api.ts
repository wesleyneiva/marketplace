import { OrigemMovimento, TipoMovimento } from './financeiro.api';

// Conciliação bancária (extrato OFX). Formatos iguais a Contracts/ConciliacaoDtos.cs.

export const URL_CONCILIACAO = '/api/financeiro/conciliacao';

export type StatusLinha = 'Pendente' | 'Conciliada' | 'Ignorada';

export interface MovimentoResumo {
  id: number;
  data: string;
  tipo: TipoMovimento;
  valor: number;
  descricao: string;
  categoria: string | null;
  origem: OrigemMovimento;
}

export interface SugestaoConciliacao {
  tipo: 'exata' | 'agrupada';
  linhaIds: number[];
  movimentoIds: number[];
  movimentos: MovimentoResumo[];
  explicacao: string;
}

export interface LinhaExtrato {
  id: number;
  data: string;
  valor: number;          // com sinal: + entrou, − saiu
  descricao: string;
  documento: string | null;
  status: StatusLinha;
  conciliacaoId: string | null;
  sugestao: SugestaoConciliacao | null;
  categoriaSugeridaId: number | null;
}

export interface ContaConciliacao {
  id: number;
  nome: string;
  banco: string | null;
  pendentes: number;
  ultimoExtratoAte: string | null;
}

export interface GrupoConciliado {
  id: string;
  em: string | null;
  linhas: LinhaExtrato[];
  movimentos: MovimentoResumo[];
}

export interface Conciliacao {
  contas: ContaConciliacao[];
  contaId: number | null;
  saldoBanco: number | null;
  saldoBancoEm: string | null;
  saldoSistema: number | null;
  de: string | null;
  ate: string | null;
  conciliadas: number;
  ignoradas: number;
  pendentes: LinhaExtrato[];
  semExtrato: MovimentoResumo[];
  recentes: GrupoConciliado[];
  ignoradasLista: LinhaExtrato[];
}

export interface ImportacaoExtrato {
  extratoId: number;
  contaId: number;
  conta: string;
  banco: string | null;
  de: string;
  ate: string;
  linhas: number;
  novas: number;
  repetidas: number;
  sugestoes: number;
  aviso: string | null;
}

// Valor do movimento com sinal (como no extrato do banco).
export const comSinal = (m: MovimentoResumo): number => (m.tipo === 'Entrada' ? m.valor : -m.valor);

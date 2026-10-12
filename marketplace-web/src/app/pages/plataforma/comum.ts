import { HttpErrorResponse } from '@angular/common/http';

// Peças comuns das telas da área do dono.

export const API = '/api/plataforma';

export interface Plano {
  id: number; nome: string; descricao: string | null; precoMensal: number; caixasIncluidos: number; precoCaixaExtra: number;
  ativo: boolean; clientes: number;
}

export interface Fatura {
  id: number; clienteId: number; cliente: string; telefone: string | null; responsavel: string | null;
  competencia: string; vencimento: string; valor: number; descricao: string; status: 'Aberta' | 'Paga' | 'Cancelada';
  pagaEm: string | null; valorPago: number | null; formaPagamento: string | null; observacao: string | null; diasAtraso: number;
}

export interface ConfiguracaoCobranca {
  diasGerarAntes: number; bloqueioAutomatico: boolean; diasParaBloquear: number; diasAvisoAntes: number;
  chavePix: string | null; nomeRecebedor: string | null; contatoCobranca: string | null;
}

export const FORMAS: { valor: string; texto: string }[] = [
  { valor: 'Pix', texto: 'PIX' }, { valor: 'Boleto', texto: 'Boleto' }, { valor: 'Transferencia', texto: 'Transferência' },
  { valor: 'Dinheiro', texto: 'Dinheiro' }, { valor: 'Cartao', texto: 'Cartão' },
];

const MESES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

// "2026-10-01" → "out/2026" (sem new Date: datas "só dia" viram o dia anterior no fuso de Brasília).
export function mesAno(data: string): string {
  const [ano, mes] = data.split('-').map(Number);
  return `${MESES[mes - 1]}/${ano}`;
}

export function hojeIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function reais(v: number): string {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

export function mensagemErro(e: unknown, padrao = 'Não foi possível concluir. Tente de novo.'): string {
  if (e instanceof HttpErrorResponse) {
    if (e.error?.mensagem) return e.error.mensagem;
    const erros = e.error?.errors as Record<string, string[]> | undefined;
    if (erros) return Object.values(erros).flat().join(' ');
  }
  return padrao;
}

// Link do WhatsApp com a mensagem de cobrança pronta (o dono confere e envia).
export function linkCobranca(f: Fatura, cfg: ConfiguracaoCobranca | undefined): string | null {
  const fone = (f.telefone ?? '').replace(/\D/g, '');
  if (fone.length < 10) return null;
  const [ano, mes, dia] = f.vencimento.split('-');
  const nome = (f.responsavel ?? '').split(' ')[0] || 'tudo bem';
  const texto = `Olá, ${nome}! Lembrete da mensalidade do sistema do ${f.cliente}: ${f.descricao}, `
    + `${reais(f.valor)}, ${f.diasAtraso > 0 ? `venceu em ${dia}/${mes}/${ano}` : `vence em ${dia}/${mes}/${ano}`}.`
    + (cfg?.chavePix ? `\n\nPIX: ${cfg.chavePix}${cfg.nomeRecebedor ? ` (${cfg.nomeRecebedor})` : ''}` : '')
    + '\n\nSe já pagou, por favor desconsidere. Obrigado!';
  return `https://wa.me/55${fone}?text=${encodeURIComponent(texto)}`;
}

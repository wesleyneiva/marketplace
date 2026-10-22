// Oficina (ordem de serviço: encordoamento, troca de grip). Formatos iguais a Contracts/OrdemDtos.cs.
export type StatusOrdem = 'Recebida' | 'EmAndamento' | 'Pronta' | 'Entregue' | 'Cancelada';

export interface Ordem {
  id: number;
  numero: number;
  cliente: string;
  telefone: string | null;
  raquete: string;
  corda: string | null;
  tensao: string | null;
  servico: string;
  valor: number;
  paga: boolean;
  vendaId: number | null;
  observacao: string | null;
  status: StatusOrdem;
  prazo: string;
  criadaEm: string;
  prontaEm: string | null;
  entregueEm: string | null;
  criadaPor: string | null;
  atrasada: boolean;
  automatica: boolean;
  clienteId?: number | null;
  garantiaDeNumero?: number | null; // esta OS é garantia (retrabalho) da OS nº…
  motivoGarantia?: string | null;
  garantias?: number;               // quantas vezes esta OS voltou para garantia
}

export const MOTIVOS_GARANTIA = ['Corda estourou', 'Tensão errada', 'Encordoamento malfeito', 'Cliente não gostou', 'Outro'];

export interface ResumoOrdens {
  recebidas: number; emAndamento: number; prontas: number; atrasadas: number; entreguesHoje: number;
  retrabalhosMes: number; custoRetrabalhoMes: number; entreguesMes: number; // garantia: quantas voltaram no mês e o custo em corda
}
export interface HistoricoCliente { cliente: string; raquete: string | null; corda: string | null; tensao: string | null; em: string }

export const NOME_STATUS: Record<StatusOrdem, string> = {
  Recebida: 'recebida', EmAndamento: 'encordoando', Pronta: 'pronta', Entregue: 'entregue', Cancelada: 'cancelada',
};

export const URL_ORDENS = '/api/ordens';

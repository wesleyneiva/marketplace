// Formatos iguais aos DTOs da API (Contracts/RelatorioDtos.cs).
export interface Periodo { de: string; ate: string; dias: number }

export interface Indicadores {
  faturamento: number; vendas: number; ticketMedio: number; lucroBruto: number; margemPercentual: number;
  faturamentoMedioDia: number;
  variacaoFaturamento: number | null; variacaoVendas: number | null; variacaoTicket: number | null;
}

export interface VendaDia {
  data: string; vendas: number; faturamento: number; lucro: number;
  tempMax: number | null; tempMin: number | null; chuva: number;
}

export interface RelatorioVendas {
  periodo: Periodo;
  indicadores: Indicadores;
  porDia: VendaDia[];
  porDiaSemana: { dia: number; nome: string; vendas: number; faturamento: number }[];
  mapaDeCalor: { diaSemana: number; hora: number; mediaVendas: number }[];
  porForma: { forma: string; valor: number }[];
}

export interface ProdutoAbc {
  produtoId: number; nome: string; categoria: string; unidade: string; quantidade: number; vendas: number;
  faturamento: number; lucro: number; margemPercentual: number; participacao: number; acumulado: number; classe: 'A' | 'B' | 'C';
}

export interface RelatorioProdutos {
  periodo: Periodo;
  classes: { classe: 'A' | 'B' | 'C'; produtos: number; faturamento: number; participacao: number }[];
  produtos: ProdutoAbc[];
  categorias: { categoria: string; faturamento: number; lucro: number; margemPercentual: number; participacao: number; produtos: number }[];
}

export interface FaixaClima { faixa: string; horas: number; clientesPorHora: number; ticketMedio: number | null }
export interface ProdutoSensivel { nome: string; fator: number; explicacao: string }

export interface RelatorioClima {
  periodo: Periodo;
  porTemperatura: FaixaClima[];
  porChuva: FaixaClima[];
  sobemNoCalor: ProdutoSensivel[];
  sobemNoFrio: ProdutoSensivel[];
  sobemNaChuva: ProdutoSensivel[];
}

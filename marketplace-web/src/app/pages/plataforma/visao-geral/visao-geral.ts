import { Component, computed, inject } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { GraficoColunas, PontoColuna } from '../../../shared/graficos/grafico-colunas';
import { mesAno, reais } from '../comum';
import { RouterLink } from '@angular/router';
import { httpResource } from '@angular/common/http';
import { Icone } from '../../../shared/icone';
import { AuthService } from '../../../core/auth/auth.service';

interface ClienteResumo { id: number; nome: string; cidade: string | null; criadoEm: string; ultimoAcesso: string | null; ativa: boolean; responsavel: string | null }
interface Faturamento {
  receitaMensal: number; clientesPagantes: number; recebidoMes: number; aReceber: number; atrasado: number;
  clientesAtrasados: number; bloqueados: number; meses: { mes: string; faturado: number; recebido: number }[];
  porPlano: { plano: string; clientes: number; receita: number }[];
}
interface DadosVisaoGeral {
  clientes: number; ativos: number; suspensos: number; demonstracoes: number; caixasContratados: number; usuariosAtivos: number;
  acessaramHoje: number; acessaramSemana: number; semAcesso: ClienteResumo[]; recentes: ClienteResumo[]; faturamento: Faturamento;
}

// Painel do dono da plataforma: o faturamento DELE (mensalidades), a carteira de clientes e o uso do sistema.
// Nada das vendas dos clientes: isso é deles.
@Component({
  selector: 'app-visao-geral',
  imports: [Icone, DatePipe, CurrencyPipe, RouterLink, GraficoColunas],
  templateUrl: './visao-geral.html',
  styleUrl: './visao-geral.scss',
})
export class VisaoGeral {
  protected readonly auth = inject(AuthService);
  protected readonly dados = httpResource<DadosVisaoGeral>(() => '/api/plataforma/visao-geral');

  // Recebido por mês (12 meses), com o faturado daquele mês no detalhe.
  protected readonly recebidoPorMes = computed<PontoColuna[]>(() =>
    (this.dados.value()?.faturamento.meses ?? []).map((m, i, todos) => ({
      rotulo: mesAno(m.mes).replace('/20', '/'),
      valor: m.recebido,
      detalhes: [`Mensalidades do mês: ${reais(m.faturado)}`],
      apagado: i === todos.length - 1, // o mês atual ainda está acontecendo
    })));

  protected tempoDesde(data: string | null): string {
    if (!data) return 'nunca acessou';
    const dias = Math.floor((Date.now() - new Date(data).getTime()) / 86400000);
    return dias < 1 ? 'hoje' : dias === 1 ? 'ontem' : `há ${dias} dias`;
  }
}

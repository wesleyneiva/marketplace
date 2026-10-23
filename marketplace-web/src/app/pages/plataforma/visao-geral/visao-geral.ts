import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { GraficoColunas, PontoColuna } from '../../../shared/graficos/grafico-colunas';
import { FatiaRosca, GraficoRosca } from '../../../shared/graficos/grafico-rosca';
import { hojeIso, mesAno, reais } from '../comum';
import { RouterLink } from '@angular/router';
import { httpResource } from '@angular/common/http';
import { Icone } from '../../../shared/icone';
import { DiaPipe } from '../../../shared/dia.pipe';
import { AuthService } from '../../../core/auth/auth.service';

interface ClienteResumo { id: number; nome: string; cidade: string | null; criadoEm: string; clienteDesde: string; ultimoAcesso: string | null; ativa: boolean; responsavel: string | null }
interface Faturamento {
  receitaMensal: number; clientesPagantes: number; recebidoMes: number; aReceber: number; atrasado: number;
  clientesAtrasados: number; bloqueados: number; meses: { mes: string; faturado: number; recebido: number }[];
  porPlano: { plano: string; clientes: number; receita: number }[];
  porCliente: { clienteId: number; cliente: string; plano: string | null; receita: number }[];
  recebimentos: { mes: string; clienteId: number; cliente: string; valor: number }[];
  proximas: { clienteId: number; cliente: string; competencia: string; vencimento: string; valor: number; aparecemEm: string }[];
}

type Periodo = 'mes' | 'mesPassado' | '3' | '6' | '12' | 'ano';
type Rosca = 'plano' | 'cliente' | 'recebido';
interface DadosVisaoGeral {
  clientes: number; ativos: number; suspensos: number; demonstracoes: number; caixasContratados: number; usuariosAtivos: number;
  acessaramHoje: number; acessaramSemana: number; semAcesso: ClienteResumo[]; recentes: ClienteResumo[]; faturamento: Faturamento;
}

// Painel do dono da plataforma: o faturamento DELE (mensalidades), a carteira de clientes e o uso do sistema.
// Nada das vendas dos clientes: isso é deles.
@Component({
  selector: 'app-visao-geral',
  imports: [Icone, DiaPipe, CurrencyPipe, RouterLink, GraficoColunas, GraficoRosca],
  templateUrl: './visao-geral.html',
  styleUrl: './visao-geral.scss',
})
export class VisaoGeral {
  protected readonly auth = inject(AuthService);
  protected readonly dados = httpResource<DadosVisaoGeral>(() => '/api/plataforma/visao-geral');

  protected readonly periodos: { valor: Periodo; texto: string }[] = [
    { valor: 'mes', texto: 'Este mês' }, { valor: 'mesPassado', texto: 'Mês passado' }, { valor: '3', texto: '3 meses' },
    { valor: '6', texto: '6 meses' }, { valor: '12', texto: '12 meses' }, { valor: 'ano', texto: 'Este ano' },
  ];
  protected readonly periodo = signal<Periodo>('mes');
  protected readonly rosca = signal<Rosca>('plano');

  // Os meses ("aaaa-mm-01") do período escolhido; o servidor manda os últimos 24.
  protected readonly mesesPeriodo = computed(() => {
    const [ano, mes] = hojeIso().split('-').map(Number);
    const atual = ano * 12 + (mes - 1);
    const [de, ate] = ({
      mes: [atual, atual], mesPassado: [atual - 1, atual - 1], '3': [atual - 2, atual], '6': [atual - 5, atual],
      '12': [atual - 11, atual], ano: [ano * 12, atual],
    } as Record<Periodo, number[]>)[this.periodo()];
    return Array.from({ length: ate - de + 1 }, (_, i) => chaveMes(de + i));
  });

  protected readonly textoPeriodo = computed(() => ({
    mes: 'neste mês', mesPassado: 'no mês passado', '3': 'em 3 meses', '6': 'em 6 meses', '12': 'em 12 meses', ano: 'neste ano',
  } as Record<Periodo, string>)[this.periodo()]);

  protected readonly recebidoPeriodo = computed(() => {
    const meses = new Set(this.mesesPeriodo());
    const doPeriodo = (this.dados.value()?.faturamento.recebimentos ?? []).filter((r) => meses.has(r.mes.slice(0, 10)));
    return { total: doPeriodo.reduce((s, r) => s + r.valor, 0), clientes: new Set(doPeriodo.map((r) => r.clienteId)).size };
  });

  // Recebido por mês no período (no mínimo 6 colunas, terminando no fim do período), o faturado no detalhe.
  protected readonly recebidoPorMes = computed<PontoColuna[]>(() => {
    const periodo = this.mesesPeriodo();
    const fim = periodo[periodo.length - 1];
    const todos = this.dados.value()?.faturamento.meses ?? [];
    const iFim = todos.findIndex((m) => m.mes.startsWith(fim));
    const mostrar = todos.slice(Math.max(0, iFim + 1 - Math.max(6, periodo.length)), iFim + 1);
    const noPeriodo = new Set(periodo);
    const atual = chaveMes(mesAtual());
    return mostrar.map((m) => ({
      rotulo: mesAno(m.mes).replace('/20', '/'),
      valor: m.recebido,
      detalhes: [`Mensalidades do mês: ${reais(m.faturado)}`, ...(m.mes.startsWith(atual) ? ['Mês em andamento'] : [])],
      apagado: !noPeriodo.has(m.mes.slice(0, 10)), // fora do período escolhido: mais claro
    }));
  });

  // Rosca: a carteira por plano ou por cliente (quanto entra por mês), ou quem pagou o que no período.
  // A cor segue o plano/cliente (ordem fixa por nome/número), não a posição no ranking.
  protected readonly fatias = computed<FatiaRosca[]>(() => {
    const f = this.dados.value()?.faturamento;
    if (!f) return [];
    if (this.rosca() === 'plano') {
      const ordem = [...f.porPlano].map((p) => p.plano).sort((a, b) => a.localeCompare(b));
      return dobrar(f.porPlano.map((p) => ({
        rotulo: p.plano, valor: p.receita, chave: ordem.indexOf(p.plano),
        detalhes: [`${p.clientes} cliente${p.clientes === 1 ? '' : 's'}`],
      })));
    }
    if (this.rosca() === 'cliente') {
      const ordem = f.porCliente.map((c) => c.clienteId).sort((a, b) => a - b);
      return dobrar(f.porCliente.map((c) => ({ rotulo: c.cliente, valor: c.receita, chave: ordem.indexOf(c.clienteId), detalhes: c.plano ? [`Plano ${c.plano}`] : [] })));
    }
    const meses = new Set(this.mesesPeriodo());
    const porCliente = new Map<number, { cliente: string; valor: number }>();
    for (const r of f.recebimentos.filter((r) => meses.has(r.mes.slice(0, 10)))) {
      const c = porCliente.get(r.clienteId) ?? { cliente: r.cliente, valor: 0 };
      c.valor += r.valor;
      porCliente.set(r.clienteId, c);
    }
    const ordem = [...porCliente.keys()].sort((a, b) => a - b);
    return dobrar([...porCliente].map(([id, c]) => ({ rotulo: c.cliente, valor: c.valor, chave: ordem.indexOf(id) })));
  });

  // As próximas mensalidades (ainda não apareceram em Cobranças): soma e quando aparecem.
  protected readonly proximas = computed(() => {
    const p = this.dados.value()?.faturamento.proximas ?? [];
    if (!p.length) return null;
    const primeiro = p[0].vencimento;
    const doMes = p.filter((x) => x.vencimento.slice(0, 7) === primeiro.slice(0, 7));
    return { valor: doMes.reduce((s, x) => s + x.valor, 0), vencimento: primeiro, aparecemEm: doMes[0].aparecemEm, clientes: doMes.length };
  });

  protected tempoDesde(data: string | null): string {
    if (!data) return 'nunca acessou';
    const dias = Math.floor((Date.now() - new Date(data).getTime()) / 86400000);
    return dias < 1 ? 'hoje' : dias === 1 ? 'ontem' : `há ${dias} dias`;
  }
}

const mesAtual = () => { const [a, m] = hojeIso().split('-').map(Number); return a * 12 + (m - 1); };
const chaveMes = (n: number) => `${Math.floor(n / 12)}-${String((n % 12) + 1).padStart(2, '0')}-01`;

// Até 5 fatias: as 4 maiores com a sua cor e o resto junto em "Outros" (a 5ª cor). A cor vem da ordem fixa (chave).
function dobrar(itens: { rotulo: string; valor: number; chave: number; detalhes?: string[] }[]): FatiaRosca[] {
  const ordenados = itens.filter((i) => i.valor > 0).sort((a, b) => b.valor - a.valor);
  if (ordenados.length <= 5) return ordenados.map((i) => ({ rotulo: i.rotulo, valor: i.valor, detalhes: i.detalhes, cor: (i.chave % 5) + 1 }));
  const cabeca = ordenados.slice(0, 4);
  const cores = [...cabeca].sort((a, b) => a.chave - b.chave);
  const resto = ordenados.slice(4);
  return [
    ...cabeca.map((i) => ({ rotulo: i.rotulo, valor: i.valor, detalhes: i.detalhes, cor: cores.indexOf(i) + 1 })),
    { rotulo: `Outros (${resto.length})`, valor: resto.reduce((s, i) => s + i.valor, 0), cor: 5 },
  ];
}

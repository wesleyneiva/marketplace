import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo';
import { Lote } from '../../core/api/estoque.api';
import { Pagina, Produto } from '../../core/api/produtos.api';
import { ResumoCaixa, ResumoVendas } from '../../core/api/pdv.api';
import { SimuladorApi, SimuladorStatus } from '../../core/api/simulador.api';

// Dashboard: boas-vindas + indicadores. Os de estoque já são reais;
// "Vendas hoje" e "Ticket médio" ganham números quando o PDV existir.
@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, CurrencyPipe, RouterLink, Logo],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly auth = inject(AuthService);
  private readonly simuladorApi = inject(SimuladorApi);

  protected readonly hoje = new Date();
  protected readonly veEstoque = computed(() => this.auth.temPerfil('Administrador', 'Gerente'));

  protected readonly saudacao = computed(() => {
    const hora = this.hoje.getHours();
    const periodo = hora < 12 ? 'Bom dia' : hora < 18 ? 'Boa tarde' : 'Boa noite';
    const primeiroNome = this.auth.usuario()?.nome.split(' ')[0] ?? '';
    return `${periodo}, ${primeiroNome}!`;
  });

  // O caixa não tem acesso ao estoque: para ele, nem fazemos a chamada (undefined = não buscar).
  protected readonly estoqueBaixo = httpResource<Pagina<Produto>>(() =>
    this.veEstoque() ? { url: '/api/produtos', params: { estoqueBaixo: true, tamanho: 5 } } : undefined,
  );
  protected readonly validades = httpResource<Lote[]>(() =>
    this.veEstoque() ? { url: '/api/estoque/validades', params: { dias: 7 } } : undefined,
  );

  // Vendas: a gerência vê o dia inteiro (todos os caixas); o operador de caixa vê o caixa dele.
  protected readonly vendasDia = httpResource<ResumoVendas>(() => (this.veEstoque() ? '/api/vendas/resumo' : undefined));
  protected readonly meuCaixa = httpResource<ResumoCaixa | null>(() => (this.veEstoque() ? undefined : '/api/caixa/atual'));

  protected readonly vendas = computed(() => {
    if (this.veEstoque()) {
      const r = this.vendasDia.value();
      return r ? { total: r.totalVendido, quantidade: r.quantidadeVendas, ticket: r.ticketMedio, lucro: r.lucroBruto as number | null, rotulo: 'todos os caixas' } : null;
    }
    const c = this.meuCaixa.value();
    return c ? { total: c.totalVendido, quantidade: c.quantidadeVendas, ticket: c.ticketMedio, lucro: null, rotulo: `seu caixa (${c.numeroCaixa})` } : null;
  });

  // ----- Simulador de clientes (gerência vê; só o Administrador pausa/retoma) -----
  protected readonly ehAdmin = computed(() => this.auth.temPerfil('Administrador'));
  protected readonly simulador = httpResource<SimuladorStatus>(() => (this.veEstoque() ? '/api/simulador' : undefined));
  protected readonly ocupado = signal(false);
  protected readonly avisoSimulador = signal<string | null>(null);

  protected async alternarSimulador(): Promise<void> {
    this.ocupado.set(true);
    try {
      const atual = this.simulador.value();
      this.simulador.set(atual?.ativo ? await this.simuladorApi.pausar() : await this.simuladorApi.retomar());
    } finally {
      this.ocupado.set(false);
    }
  }

  protected async atenderAgora(): Promise<void> {
    this.ocupado.set(true);
    try {
      const r = await this.simuladorApi.atenderAgora(5);
      this.avisoSimulador.set(`${r.vendasFeitas} de ${r.clientes} clientes compraram agora.`);
      setTimeout(() => this.avisoSimulador.set(null), 5000);
      this.simulador.reload();
      this.vendasDia.reload();
      this.estoqueBaixo.reload();
    } finally {
      this.ocupado.set(false);
    }
  }

  protected readonly resumoValidade = computed(() => {
    const lotes = this.validades.value() ?? [];
    return {
      vencidos: lotes.filter((l) => l.diasRestantes < 0).length,
      vencendo: lotes.filter((l) => l.diasRestantes >= 0).length,
      valor: lotes.reduce((s, l) => s + l.valorEmRisco, 0),
      primeiros: lotes.slice(0, 5),
    };
  });
}

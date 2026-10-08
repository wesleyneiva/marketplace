import { Component, computed, inject } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Lote } from '../../core/api/estoque.api';
import { Pagina, Produto } from '../../core/api/produtos.api';

// Dashboard: boas-vindas + indicadores. Os de estoque já são reais;
// "Vendas hoje" e "Ticket médio" ganham números quando o PDV existir.
@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, CurrencyPipe, RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly auth = inject(AuthService);

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

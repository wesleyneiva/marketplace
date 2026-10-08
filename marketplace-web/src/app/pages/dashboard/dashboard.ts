import { Component, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { AuthService } from '../../core/auth/auth.service';

interface Indicador {
  titulo: string;
  icone: string;
  descricao: string;
}

// Primeira versão do dashboard: boas-vindas + os cartões que vão ganhar números reais
// quando tivermos produtos e vendas no banco.
@Component({
  selector: 'app-dashboard',
  imports: [DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly auth = inject(AuthService);

  protected readonly hoje = new Date();

  protected readonly saudacao = computed(() => {
    const hora = this.hoje.getHours();
    const periodo = hora < 12 ? 'Bom dia' : hora < 18 ? 'Boa tarde' : 'Boa noite';
    const primeiroNome = this.auth.usuario()?.nome.split(' ')[0] ?? '';
    return `${periodo}, ${primeiroNome}!`;
  });

  protected readonly indicadores: Indicador[] = [
    { titulo: 'Vendas hoje', icone: '💰', descricao: 'Faturamento do dia' },
    { titulo: 'Ticket médio', icone: '🧾', descricao: 'Valor médio por venda' },
    { titulo: 'Estoque baixo', icone: '⚠️', descricao: 'Produtos abaixo do mínimo' },
    { titulo: 'Vencendo', icone: '⏰', descricao: 'Validade nos próximos 7 dias' },
  ];
}

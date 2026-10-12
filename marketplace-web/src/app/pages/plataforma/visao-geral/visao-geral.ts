import { Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { httpResource } from '@angular/common/http';
import { Icone } from '../../../shared/icone';
import { AuthService } from '../../../core/auth/auth.service';

interface ClienteResumo { id: number; nome: string; cidade: string | null; criadoEm: string; ultimoAcesso: string | null; ativa: boolean; responsavel: string | null }
interface DadosVisaoGeral {
  clientes: number; ativos: number; suspensos: number; demonstracoes: number; caixasContratados: number; usuariosAtivos: number;
  acessaramHoje: number; acessaramSemana: number; semAcesso: ClienteResumo[]; recentes: ClienteResumo[];
}

// Painel do dono da plataforma. Por enquanto: clientes e uso. Faturamento (mensalidades) entra com Planos e Cobranças.
@Component({
  selector: 'app-visao-geral',
  imports: [Icone, DatePipe, RouterLink],
  templateUrl: './visao-geral.html',
  styleUrl: './visao-geral.scss',
})
export class VisaoGeral {
  protected readonly auth = inject(AuthService);
  protected readonly dados = httpResource<DadosVisaoGeral>(() => '/api/plataforma/visao-geral');

  protected tempoDesde(data: string | null): string {
    if (!data) return 'nunca acessou';
    const dias = Math.floor((Date.now() - new Date(data).getTime()) / 86400000);
    return dias < 1 ? 'hoje' : dias === 1 ? 'ontem' : `há ${dias} dias`;
  }
}

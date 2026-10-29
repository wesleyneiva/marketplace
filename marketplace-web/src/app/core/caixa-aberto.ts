import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { ResumoCaixa } from './api/pdv.api';

// O caixa que ESTE usuário deixou aberto (ou null). Mostra o aviso no topo, perto do relógio, e impede sair do sistema
// (ou fechar a aba sem aviso) com o caixa aberto: um caixa esquecido de um dia para o outro trava o caixa para o próximo
// operador (e o robô das lojas em teste). A casca recarrega a cada troca de tela e de minuto em minuto; o PDV chama
// recarregar() ao abrir e ao fechar o caixa. Sem internet, fica o último valor conhecido.
@Injectable({ providedIn: 'root' })
export class CaixaAberto {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly caixa = signal<{ numero: number; abertaEm: string } | null>(null);
  // Aberto antes de hoje (horário de Brasília): o aviso fica vermelho.
  readonly deOutroDia = computed(() => {
    const c = this.caixa();
    return !!c && DIA.format(new Date(c.abertaEm)) !== DIA.format(new Date());
  });

  constructor() {
    if (typeof window !== 'undefined')
      window.addEventListener('beforeunload', (e) => {
        // Fechar a aba ou o navegador com o caixa aberto: o navegador pergunta (o texto é dele, não dá para mudar).
        if (this.caixa()) e.preventDefault();
      });
  }

  async recarregar(): Promise<void> {
    const u = this.auth.usuario();
    if (!u || u.soPlataforma || u.somenteLeitura || u.trocarSenha) {
      this.caixa.set(null);
      return;
    }
    try {
      const r = await firstValueFrom(this.http.get<ResumoCaixa | null>('/api/caixa/atual'));
      this.caixa.set(r && r.status === 'Aberta' ? { numero: r.numeroCaixa, abertaEm: r.abertaEm } : null);
    } catch {
      // sem internet ou servidor fora: mantém o que sabia
    }
  }
}

const DIA = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' });

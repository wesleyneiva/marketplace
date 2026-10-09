import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mesmo formato do UsuarioLogadoResponse da API (C#).
export interface UsuarioLogado {
  id: string;
  nome: string;
  email: string;
  perfis: string[];
  trocarSenha: boolean; // senha provisória: precisa trocar antes de usar o sistema
  empresa: Empresa;      // o mercado onde a pessoa trabalha (multi-tenant)
  somenteLeitura: boolean; // visitante da demonstração: só olha
}

export interface Empresa {
  id: number;
  nome: string;
  limiteCaixas: number;
  demonstracao: boolean;
}

// O que a tela de login deve mostrar (pela internet, só o botão da demonstração).
export interface AcessoInfo {
  demonstracao: boolean;
  loginComSenha: boolean;
}

// Serviço de autenticação: fala com /api/auth e guarda "quem está logado" num signal.
// Signal = uma variável "observável": quando muda, as telas que usam ela se atualizam sozinhas.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly usuarioAtual = signal<UsuarioLogado | null>(null);
  private verificado = false;

  readonly usuario = this.usuarioAtual.asReadonly();
  readonly logado = computed(() => this.usuarioAtual() !== null);

  async login(email: string, senha: string): Promise<void> {
    const usuario = await firstValueFrom(
      this.http.post<UsuarioLogado>('/api/auth/login', { email, senha }),
    );
    this.usuarioAtual.set(usuario);
    this.verificado = true;
  }

  // Entra como visitante da demonstração (sem senha; só olha).
  async demonstracao(): Promise<void> {
    this.usuarioAtual.set(await firstValueFrom(this.http.post<UsuarioLogado>('/api/auth/demonstracao', {})));
    this.verificado = true;
  }

  acesso(): Promise<AcessoInfo> {
    return firstValueFrom(this.http.get<AcessoInfo>('/api/auth/acesso'));
  }

  async trocarSenha(senhaAtual: string, novaSenha: string): Promise<void> {
    this.usuarioAtual.set(
      await firstValueFrom(this.http.post<UsuarioLogado>('/api/auth/trocar-senha', { senhaAtual, novaSenha })),
    );
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.usuarioAtual.set(null);
    }
  }

  // Ao abrir/recarregar a página, pergunta à API se o cookie ainda vale (GET /api/auth/eu).
  // Só pergunta uma vez; depois usa o que já sabe.
  async garantirSessao(): Promise<boolean> {
    if (!this.verificado) {
      this.verificado = true;
      try {
        this.usuarioAtual.set(await firstValueFrom(this.http.get<UsuarioLogado>('/api/auth/eu')));
      } catch {
        this.usuarioAtual.set(null);
      }
    }
    return this.logado();
  }

  temPerfil(...perfis: string[]): boolean {
    const meus = this.usuarioAtual()?.perfis ?? [];
    return perfis.some((p) => meus.includes(p));
  }
}

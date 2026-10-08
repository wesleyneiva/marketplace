import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mesmo formato do UsuarioLogadoResponse da API (C#).
export interface UsuarioLogado {
  id: string;
  nome: string;
  email: string;
  perfis: string[];
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

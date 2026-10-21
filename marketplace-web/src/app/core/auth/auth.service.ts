import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { perfilRamo } from '../ramos';
import { firstValueFrom } from 'rxjs';
import { semConexao } from '../offline/conexao';
import { Modulo, moduloLiberado } from '../modulos';

// Caixa offline: quem estava logado fica guardado neste computador. Sem internet, a tela do caixa abre com ele
// (o cookie de login continua no navegador; quando a internet voltar, o servidor confere de novo).
const CHAVE_USUARIO = 'marcus.ultimoUsuario';

// Mesmo formato do UsuarioLogadoResponse da API (C#).
export interface UsuarioLogado {
  id: string;
  nome: string;
  email: string;
  perfis: string[];
  trocarSenha: boolean; // senha provisória: precisa trocar antes de usar o sistema
  empresa: Empresa;      // o mercado onde a pessoa trabalha (multi-tenant)
  somenteLeitura: boolean; // visitante da demonstração: só olha
  donoDaPlataforma: boolean; // administra os clientes do SaaS (área do dono)
  soPlataforma: boolean;     // login do dono da plataforma: não é de um mercado, só vê a área do dono
}

export interface Empresa {
  id: number;
  nome: string;
  limiteCaixas: number;
  demonstracao: boolean;
  modulos: string[] | null; // módulos liberados pelo dono da plataforma (null = todos) — ver core/modulos.ts
  ramo?: string;             // ramo do comércio (core/ramos.ts); ausente = mercado
}

// O que a tela de login deve mostrar (pela internet, só o botão da demonstração).
export interface AcessoInfo {
  demonstracao: boolean;
  loginComSenha: boolean;
  areaDoDono: boolean; // endereço da rede interna: só o dono da plataforma entra
}

// Serviço de autenticação: fala com /api/auth e guarda "quem está logado" num signal.
// Signal = uma variável "observável": quando muda, as telas que usam ela se atualizam sozinhas.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly usuarioAtual = signal<UsuarioLogado | null>(null);
  readonly semInternet = signal(false); // entrou com o usuário guardado porque o servidor não respondeu
  private verificado = false;

  readonly usuario = this.usuarioAtual.asReadonly();
  readonly logado = computed(() => this.usuarioAtual() !== null);
  // Palavras e o que aparece conforme o ramo da empresa (core/ramos.ts).
  readonly ramo = computed(() => perfilRamo(this.usuarioAtual()?.empresa?.ramo));

  // Para onde a pessoa vai depois de entrar: o dono da plataforma para a área do dono; os demais, para o painel do mercado.
  inicio(): string {
    return this.usuarioAtual()?.soPlataforma ? '/plataforma' : '/dashboard';
  }

  async login(email: string, senha: string): Promise<void> {
    const usuario = await firstValueFrom(
      this.http.post<UsuarioLogado>('/api/auth/login', { email, senha }),
    );
    this.definir(usuario);
    this.verificado = true;
  }

  private definir(usuario: UsuarioLogado | null): void {
    this.usuarioAtual.set(usuario);
    this.semInternet.set(false);
    try {
      if (usuario && !usuario.somenteLeitura) localStorage.setItem(CHAVE_USUARIO, JSON.stringify(usuario));
      else localStorage.removeItem(CHAVE_USUARIO);
    } catch { /* sem armazenamento: só não abre sem internet */ }
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
    this.definir(await firstValueFrom(this.http.post<UsuarioLogado>('/api/auth/trocar-senha', { senhaAtual, novaSenha })));
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.definir(null);
    }
  }

  // Ao abrir/recarregar a página, pergunta à API se o cookie ainda vale (GET /api/auth/eu).
  // Só pergunta uma vez; depois usa o que já sabe.
  async garantirSessao(): Promise<boolean> {
    if (!this.verificado) {
      this.verificado = true;
      try {
        this.definir(await firstValueFrom(this.http.get<UsuarioLogado>('/api/auth/eu')));
      } catch (e) {
        // Sem internet (ou servidor fora do ar): segue com quem estava logado neste computador.
        const guardado = semConexao(e) ? lerGuardado() : null;
        this.usuarioAtual.set(guardado);
        this.semInternet.set(!!guardado);
        if (!guardado) this.definir(null);
      }
    }
    return this.logado();
  }

  // O módulo foi liberado para a empresa de quem está logado? (lê o signal: telas e menus se atualizam sozinhos)
  liberado(modulo: Modulo): boolean {
    return moduloLiberado(this.usuarioAtual()?.empresa?.modulos, modulo);
  }

  // Pergunta de novo ao servidor quem está logado (a casca chama a cada minuto e ao voltar para a aba):
  // assim os módulos que o dono liberar ou tirar aparecem/somem sem precisar sair e entrar.
  async atualizar(): Promise<void> {
    if (!this.usuarioAtual() || this.usuarioAtual()!.somenteLeitura) return;
    try {
      const novo = await firstValueFrom(this.http.get<UsuarioLogado>('/api/auth/eu'));
      if (JSON.stringify(novo) !== JSON.stringify(this.usuarioAtual())) this.definir(novo);
    } catch { /* sem internet ou sessão caiu: as outras telas cuidam disso */ }
  }

  temPerfil(...perfis: string[]): boolean {
    const meus = this.usuarioAtual()?.perfis ?? [];
    return perfis.some((p) => meus.includes(p));
  }
}

function lerGuardado(): UsuarioLogado | null {
  try {
    return JSON.parse(localStorage.getItem(CHAVE_USUARIO) ?? 'null');
  } catch {
    return null;
  }
}

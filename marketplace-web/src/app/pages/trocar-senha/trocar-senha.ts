import { Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo';

// Primeiro acesso (senha provisória → obrigatório) ou "Trocar minha senha" (voluntário).
@Component({
  selector: 'app-trocar-senha',
  imports: [FormsModule, RouterLink, Logo],
  templateUrl: './trocar-senha.html',
  styleUrl: '../login/login.scss',
})
export class TrocarSenha {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly usuario = this.auth.usuario;
  protected readonly obrigatorio = computed(() => !!this.usuario()?.trocarSenha);

  protected senhaAtual = '';
  protected readonly novaSenha = signal('');
  protected confirmacao = '';
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  // As mesmas regras da API, conferidas enquanto a pessoa digita.
  protected readonly regras = computed(() => {
    const s = this.novaSenha();
    return [
      { texto: 'Pelo menos 8 caracteres', ok: s.length >= 8 },
      { texto: 'Uma letra maiúscula', ok: /[A-Z]/.test(s) },
      { texto: 'Uma letra minúscula', ok: /[a-z]/.test(s) },
      { texto: 'Um número', ok: /\d/.test(s) },
      { texto: 'Um símbolo (ex.: ! @ # -)', ok: /[^A-Za-z0-9]/.test(s) },
    ];
  });
  protected readonly senhaValida = computed(() => this.regras().every((r) => r.ok));

  async salvar(): Promise<void> {
    this.erro.set(null);
    if (!this.senhaValida()) return this.erro.set('A nova senha não atende a todas as regras.');
    if (this.novaSenha() !== this.confirmacao) return this.erro.set('A confirmação não é igual à nova senha.');

    this.salvando.set(true);
    try {
      await this.auth.trocarSenha(this.senhaAtual, this.novaSenha());
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.erro.set(e instanceof HttpErrorResponse && e.error?.mensagem ? e.error.mensagem : 'Não foi possível trocar a senha.');
    } finally {
      this.salvando.set(false);
    }
  }

  async sair(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}

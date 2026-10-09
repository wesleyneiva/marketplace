import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AcessoInfo, AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, Logo],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // Formulário reativo: os campos e as regras de validação ficam aqui no TypeScript.
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    senha: ['', Validators.required],
  });

  protected readonly entrando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly mostrarSenha = signal(false);

  // Começa como "tudo liberado" (rede interna) e se ajusta com a resposta da API.
  protected readonly acesso = signal<AcessoInfo>({ demonstracao: false, loginComSenha: true });

  constructor() {
    this.auth.acesso().then((a) => this.acesso.set(a)).catch(() => {});
  }

  async verDemonstracao(): Promise<void> {
    this.entrando.set(true);
    this.erro.set(null);
    try {
      await this.auth.demonstracao();
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      const resposta = e instanceof HttpErrorResponse ? e : null;
      this.erro.set(resposta?.error?.mensagem ?? 'A demonstração não está disponível agora.');
    } finally {
      this.entrando.set(false);
    }
  }

  async entrar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.entrando.set(true);
    this.erro.set(null);

    try {
      const { email, senha } = this.form.getRawValue();
      await this.auth.login(email, senha);
      await this.router.navigateByUrl(this.auth.usuario()?.trocarSenha ? '/trocar-senha' : '/dashboard');
    } catch (e) {
      // A API manda { mensagem: "..." } no 401; se nem respondeu, é problema de conexão.
      const resposta = e instanceof HttpErrorResponse ? e : null;
      this.erro.set(
        resposta?.status === 401 || resposta?.status === 403 || resposta?.status === 429
          ? (resposta.error?.mensagem ?? 'E-mail ou senha inválidos.')
          : 'Não foi possível falar com o servidor. Tente de novo em instantes.',
      );
    } finally {
      this.entrando.set(false);
    }
  }
}

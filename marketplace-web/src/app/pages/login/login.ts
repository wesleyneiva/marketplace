import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
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
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      // A API manda { mensagem: "..." } no 401; se nem respondeu, é problema de conexão.
      const resposta = e instanceof HttpErrorResponse ? e : null;
      this.erro.set(
        resposta?.status === 401
          ? (resposta.error?.mensagem ?? 'E-mail ou senha inválidos.')
          : 'Não foi possível falar com o servidor. Tente de novo em instantes.',
      );
    } finally {
      this.entrando.set(false);
    }
  }
}

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Guard = "porteiro" de rota: decide se a página pode abrir.

// Páginas internas: só entra quem está logado; senão vai para /login.
export const precisaLogin: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return (await auth.garantirSessao()) ? true : router.createUrlTree(['/login']);
};

// Tela de login: quem já está logado vai direto para o dashboard.
export const somenteDeslogado: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return (await auth.garantirSessao()) ? router.createUrlTree(['/dashboard']) : true;
};

// Páginas restritas a alguns perfis. Uso: canActivate: [precisaPerfil('Administrador', 'Gerente')]
export const precisaPerfil =
  (...perfis: string[]): CanActivateFn =>
  async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!(await auth.garantirSessao())) return router.createUrlTree(['/login']);
    return auth.temPerfil(...perfis) ? true : router.createUrlTree(['/dashboard']);
  };

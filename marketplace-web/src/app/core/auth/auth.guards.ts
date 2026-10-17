import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { Modulo } from '../modulos';

// Guard = "porteiro" de rota: decide se a página pode abrir.

// Páginas internas: só entra quem está logado; senão vai para /login.
// Quem está com SENHA PROVISÓRIA vai antes para /trocar-senha (primeiro acesso).
export const precisaLogin: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!(await auth.garantirSessao())) return router.createUrlTree(['/login']);
  return auth.usuario()?.trocarSenha ? router.createUrlTree(['/trocar-senha']) : true;
};

// A tela de trocar senha: precisa estar logado (com ou sem senha provisória).
export const logadoMesmoComSenhaProvisoria: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return (await auth.garantirSessao()) ? true : router.createUrlTree(['/login']);
};

// Tela de login: quem já está logado vai direto para o dashboard.
export const somenteDeslogado: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return (await auth.garantirSessao()) ? router.createUrlTree([auth.inicio()]) : true;
};

// Páginas restritas a alguns perfis. Uso: canActivate: [precisaPerfil('Administrador', 'Gerente')]
export const precisaPerfil =
  (...perfis: string[]): CanActivateFn =>
  async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!(await auth.garantirSessao())) return router.createUrlTree(['/login']);
    if (auth.usuario()?.trocarSenha) return router.createUrlTree(['/trocar-senha']);
    return auth.temPerfil(...perfis) ? true : router.createUrlTree([auth.inicio()]);
  };

// Telas da plataforma (clientes do SaaS): só o dono, pela rede interna (a API confere de novo).
export const precisaSerDono: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!(await auth.garantirSessao())) return router.createUrlTree(['/login']);
  return auth.usuario()?.donoDaPlataforma ? true : router.createUrlTree(['/dashboard']);
};

// Telas de MERCADO (dashboard, PDV, produtos…): o login do dono da plataforma não é de um mercado → área do dono.
export const soMercado: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.garantirSessao();
  return auth.usuario()?.soPlataforma ? router.createUrlTree(['/plataforma']) : true;
};

// Telas de um módulo opcional (core/modulos.ts): só abre se o dono da plataforma liberou para este cliente.
export const precisaModulo =
  (modulo: Modulo): CanActivateFn =>
  async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    await auth.garantirSessao();
    return auth.liberado(modulo) ? true : router.createUrlTree([auth.inicio()]);
  };

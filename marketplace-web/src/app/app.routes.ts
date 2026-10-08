import { Routes } from '@angular/router';
import { logadoMesmoComSenhaProvisoria, precisaLogin, precisaPerfil, somenteDeslogado } from './core/auth/auth.guards';

// Mapa de páginas do sistema. loadComponent = a página só é baixada quando alguém abre (deixa o app leve).
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [somenteDeslogado],
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
  },
  {
    // Primeiro acesso (senha provisória) ou "trocar minha senha": tela própria, fora da casca, com o logo.
    path: 'trocar-senha',
    canActivate: [logadoMesmoComSenhaProvisoria],
    loadComponent: () => import('./pages/trocar-senha/trocar-senha').then((m) => m.TrocarSenha),
  },
  {
    // "Casca" do sistema (menu lateral + topo). As páginas internas aparecem dentro dela.
    path: '',
    canActivate: [precisaLogin],
    loadComponent: () => import('./layout/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'produtos',
        canActivate: [precisaPerfil('Administrador', 'Gerente')],
        children: [
          { path: '', loadComponent: () => import('./pages/produtos/produtos').then((m) => m.Produtos) },
          // "novo" e ":id" usam a mesma tela de formulário. O :id chega no componente como input().
          { path: 'novo', loadComponent: () => import('./pages/produto-form/produto-form').then((m) => m.ProdutoForm) },
          { path: ':id', loadComponent: () => import('./pages/produto-form/produto-form').then((m) => m.ProdutoForm) },
        ],
      },
      {
        path: 'pdv',
        loadComponent: () => import('./pages/pdv/pdv').then((m) => m.Pdv),
      },
      {
        path: 'relatorios',
        canActivate: [precisaPerfil('Administrador', 'Gerente')],
        loadComponent: () => import('./pages/relatorios/relatorios').then((m) => m.Relatorios),
      },
      {
        path: 'usuarios',
        canActivate: [precisaPerfil('Administrador')],
        loadComponent: () => import('./pages/usuarios/usuarios').then((m) => m.Usuarios),
      },
      {
        path: 'estoque',
        canActivate: [precisaPerfil('Administrador', 'Gerente')],
        loadComponent: () => import('./pages/estoque/estoque').then((m) => m.Estoque),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

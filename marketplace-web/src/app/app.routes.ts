import { Routes } from '@angular/router';
import { precisaLogin, precisaPerfil, somenteDeslogado } from './core/auth/auth.guards';

// Mapa de páginas do sistema. loadComponent = a página só é baixada quando alguém abre (deixa o app leve).
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [somenteDeslogado],
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
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
        path: 'estoque',
        canActivate: [precisaPerfil('Administrador', 'Gerente')],
        loadComponent: () => import('./pages/estoque/estoque').then((m) => m.Estoque),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

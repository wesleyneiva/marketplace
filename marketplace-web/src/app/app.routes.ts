import { Routes } from '@angular/router';
import { precisaLogin, somenteDeslogado } from './core/auth/auth.guards';

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
    ],
  },
  { path: '**', redirectTo: '' },
];

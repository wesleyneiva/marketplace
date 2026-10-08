import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

interface ItemMenu {
  rotulo: string;
  icone: string;
  rota: string;
  perfis: string[];  // quem pode ver este item
  pronto: boolean;   // false = aparece como "em breve"
}

// "Casca" do sistema: menu lateral + barra do topo. As páginas aparecem no <router-outlet>.
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly usuario = this.auth.usuario;
  protected readonly menuAberto = signal(false);

  private readonly menu: ItemMenu[] = [
    { rotulo: 'Dashboard', icone: '📊', rota: '/dashboard', perfis: ['Administrador', 'Gerente', 'Caixa'], pronto: true },
    { rotulo: 'Caixa (PDV)', icone: '🧾', rota: '/pdv', perfis: ['Administrador', 'Caixa'], pronto: false },
    { rotulo: 'Produtos', icone: '📦', rota: '/produtos', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Estoque', icone: '🏷️', rota: '/estoque', perfis: ['Administrador', 'Gerente'], pronto: false },
    { rotulo: 'Fornecedores', icone: '🚚', rota: '/fornecedores', perfis: ['Administrador', 'Gerente'], pronto: false },
    { rotulo: 'Relatórios', icone: '📈', rota: '/relatorios', perfis: ['Administrador', 'Gerente'], pronto: false },
    { rotulo: 'Usuários', icone: '👥', rota: '/usuarios', perfis: ['Administrador'], pronto: false },
  ];

  // computed = recalcula sozinho quando o usuário muda. Cada perfil vê só o que pode usar.
  protected readonly itensVisiveis = computed(() =>
    this.menu.filter((item) => this.auth.temPerfil(...item.perfis)),
  );

  protected readonly iniciais = computed(() =>
    (this.usuario()?.nome ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join(''),
  );

  async sair(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}

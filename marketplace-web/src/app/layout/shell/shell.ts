import { Component, computed, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { copiarTexto } from '../../shared/copiar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { MODULOS } from '../../core/modulos';
import { Icone } from '../../shared/icone';
import { Logo } from '../../shared/logo';

interface ItemMenu {
  rotulo: string;
  icone: string;     // nome do ícone (shared/icones.ts)
  rota: string;
  perfis: string[];  // quem pode ver este item
  soDono?: boolean;  // só o dono da plataforma (administra os clientes)
  pronto: boolean;   // false = aparece como "em breve"
  modulo?: keyof typeof MODULOS; // módulo opcional (core/modulos.ts): some do menu se estiver desligado
}

// "Casca" do sistema: menu lateral + barra do topo. As páginas aparecem no <router-outlet>.
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Logo, Icone],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly usuario = this.auth.usuario;
  protected readonly menuAberto = signal(false);

  private readonly menu: ItemMenu[] = [
    { rotulo: 'Dashboard', icone: 'layout-dashboard', rota: '/dashboard', perfis: ['Administrador', 'Gerente', 'Caixa'], pronto: true },
    { rotulo: 'Caixa (PDV)', icone: 'receipt', rota: '/pdv', perfis: ['Administrador', 'Gerente', 'Caixa'], pronto: true },
    { rotulo: 'Produtos', icone: 'package', rota: '/produtos', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Estoque', icone: 'tag', rota: '/estoque', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Compras', icone: 'shopping-bag', rota: '/compras', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Fornecedores', icone: 'truck', rota: '/fornecedores', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Financeiro', icone: 'circle-dollar-sign', rota: '/financeiro', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Contas a pagar', icone: 'calendar-clock', rota: '/contas', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Fiado', icone: 'notebook-pen', rota: '/fiado', perfis: ['Administrador', 'Gerente'], pronto: true, modulo: 'fiado' },
    { rotulo: 'Relatórios', icone: 'chart-line', rota: '/relatorios', perfis: ['Administrador', 'Gerente'], pronto: true },
    { rotulo: 'Usuários', icone: 'users', rota: '/usuarios', perfis: ['Administrador'], pronto: true },
    { rotulo: 'Configurações', icone: 'settings', rota: '/configuracoes', perfis: ['Administrador', 'Gerente'], pronto: true },
    // Enquanto a área do dono não é separada (sem login próprio do dono), o dono chega nela por aqui.
    { rotulo: 'Área do dono', icone: 'building-2', rota: '/plataforma', perfis: ['Administrador'], pronto: true, soDono: true },
  ];

  // Menu da ÁREA DO DONO da plataforma (login do dono: não é de um mercado).
  private readonly menuDono: ItemMenu[] = [
    { rotulo: 'Visão geral', icone: 'layout-dashboard', rota: '/plataforma', perfis: ['Administrador'], pronto: true },
    { rotulo: 'Clientes', icone: 'store', rota: '/plataforma/clientes', perfis: ['Administrador'], pronto: true },
    { rotulo: 'Cobranças', icone: 'receipt', rota: '/plataforma/cobrancas', perfis: ['Administrador'], pronto: true },
    { rotulo: 'Planos', icone: 'tag', rota: '/plataforma/planos', perfis: ['Administrador'], pronto: true },
    { rotulo: 'Sistema', icone: 'settings', rota: '/plataforma/sistema', perfis: ['Administrador'], pronto: true },
  ];

  protected readonly areaDoDono = computed(() => !!this.usuario()?.soPlataforma);

  // Mensalidade do sistema: aviso para o ADMINISTRADOR do mercado (vence em breve / em atraso), com a chave PIX.
  protected readonly assinatura = httpResource<{ nivel: string | null; mensagem: string | null; chavePix: string | null; nomeRecebedor: string | null; contato: string | null }>(
    () => {
      const u = this.usuario();
      return u && !u.soPlataforma && !u.somenteLeitura && !u.trocarSenha && u.perfis.includes('Administrador') ? '/api/empresa/configuracao/assinatura' : undefined;
    });
  protected readonly pixCopiado = signal(false);

  async copiarPix(chave: string): Promise<void> {
    this.pixCopiado.set(await copiarTexto(chave));
  }
  protected readonly inicio = computed(() => (this.areaDoDono() ? '/plataforma' : '/dashboard'));

  // computed = recalcula sozinho quando o usuário muda. Cada perfil vê só o que pode usar.
  protected readonly itensVisiveis = computed(() =>
    this.areaDoDono()
      ? this.menuDono
      : this.menu.filter((item) => this.auth.temPerfil(...item.perfis) && (!item.soDono || !!this.usuario()?.donoDaPlataforma) && (!item.modulo || MODULOS[item.modulo])),
  );

  protected readonly iniciais = computed(() =>
    (this.usuario()?.nome ?? '?')
      .split(' ')
      .filter((p) => /^\p{L}/u.test(p)) // só palavras que começam com letra ("(demonstração)" fica de fora)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join(''),
  );

  async sair(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}

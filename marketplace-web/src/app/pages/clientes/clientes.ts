import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Cliente, ClientesPagina } from '../../core/api/clientes.api';
import { Icone } from '../../shared/icone';
import { Paginacao } from '../../shared/paginacao';
import { ClienteForm } from './cliente-form';

// Clientes da loja: quem compra, quanto e quando. Aniversariantes do mês e "sumidos" (60 dias sem comprar) para chamar
// de volta pelo WhatsApp. A ficha (clique) mostra o histórico. O cliente também é identificado no caixa (F8).
@Component({
  selector: 'app-clientes',
  imports: [FormsModule, CurrencyPipe, DatePipe, RouterLink, Icone, Paginacao, ClienteForm],
  templateUrl: './clientes.html',
  styleUrls: ['../usuarios/usuarios.scss', './clientes.scss'],
})
export class Clientes {
  private readonly router = inject(Router);
  protected readonly ramo = inject(AuthService).ramo;
  private readonly form = viewChild.required(ClienteForm);

  protected readonly filtros = [
    { v: 'todos', t: 'Todos' }, { v: 'aniversariantes', t: 'Aniversariantes do mês' }, { v: 'sumidos', t: 'Sumidos (60+ dias)' },
    { v: 'novos', t: 'Novos (30 dias)' }, { v: 'inativos', t: 'Inativos' },
  ];
  protected readonly filtro = signal('todos');
  protected readonly busca = signal('');
  protected readonly pagina = signal(1);
  protected textoBusca = '';
  private espera?: ReturnType<typeof setTimeout>;

  protected readonly lista = httpResource<ClientesPagina>(() => ({
    url: '/api/clientes', params: { busca: this.busca(), filtro: this.filtro(), pagina: this.pagina(), tamanho: 25 },
  }));
  protected readonly paginas = computed(() => Math.max(1, Math.ceil((this.lista.value()?.total ?? 0) / 25)));

  protected digitar(t: string): void {
    this.textoBusca = t;
    clearTimeout(this.espera);
    this.espera = setTimeout(() => { this.pagina.set(1); this.busca.set(t.trim()); }, 300);
  }

  protected filtrar(v: string): void {
    this.pagina.set(1);
    this.filtro.set(v);
  }

  protected novo(): void {
    this.form().abrir();
  }

  protected aoSalvar(c: Cliente): void {
    void this.router.navigate(['/clientes', c.id]);
  }

  protected aniversario(iso: string | null): string {
    return iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : '';
  }

  protected whatsapp(tel: string | null): string | null {
    const d = (tel ?? '').replace(/\D/g, '');
    return d.length >= 10 ? `https://wa.me/${d.startsWith('55') && d.length >= 12 ? d : '55' + d}` : null;
  }
}

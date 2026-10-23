import { Component, inject, input, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ClienteDetalhe } from '../../core/api/clientes.api';
import { FORMAS_PAGAMENTO } from '../../core/api/pdv.api';
import { rotuloMotivo } from '../../core/api/trocas.api';
import { Icone } from '../../shared/icone';
import { ClienteForm } from './cliente-form';
import { Confirmacao } from '../../shared/confirmacao';

// Ficha do cliente: dados, números (quanto compra, de quanto em quanto tempo, há quantos dias não vem), o que mais
// leva, as compras, as trocas e (loja de esporte) as raquetes que passaram pela oficina.
@Component({
  selector: 'app-cliente-ficha',
  imports: [CurrencyPipe, DatePipe, RouterLink, Icone, ClienteForm],
  templateUrl: './cliente-ficha.html',
  styleUrls: ['../usuarios/usuarios.scss', './clientes.scss', './cliente-ficha.scss'],
})
export class ClienteFicha {
  private readonly confirmacao = inject(Confirmacao);
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly ramo = this.auth.ramo;
  readonly id = input.required<string>();
  private readonly form = viewChild.required(ClienteForm);
  protected readonly ficha = httpResource<ClienteDetalhe>(() => `/api/clientes/${this.id()}`);
  protected readonly rotuloMotivo = rotuloMotivo;

  protected editar(): void {
    const f = this.ficha.value();
    if (f) this.form().abrir(f.cliente);
  }

  protected async alternarAtivo(): Promise<void> {
    const c = this.ficha.value()?.cliente;
    if (!c) return;
    if (c.ativo && !(await this.confirmacao.pedir(`Desativar ${c.nome}?\nEle some da busca do caixa (o histórico fica).`, { botao: 'Desativar', perigo: true }))) return;
    try {
      await firstValueFrom(this.http.post(`/api/clientes/${c.id}/ativo`, {}, { params: { valor: !c.ativo } }));
      this.ficha.reload();
    } catch (e: unknown) {
      alert((e as { error?: { mensagem?: string } }).error?.mensagem ?? 'Não foi possível.');
    }
  }

  protected whatsapp(tel: string | null, nome: string): string | null {
    const d = (tel ?? '').replace(/\D/g, '');
    if (d.length < 10) return null;
    const texto = `Olá, ${nome.split(' ')[0]}! Aqui é da ${this.loja()}.`;
    return `https://wa.me/${d.startsWith('55') && d.length >= 12 ? d : '55' + d}?text=${encodeURIComponent(texto)}`;
  }

  protected formas(f: string): string {
    return f.split(' + ').map((x) => {
      const [forma, vezes] = x.split(' ');
      return (FORMAS_PAGAMENTO.find((p) => p.valor === forma)?.rotulo ?? forma) + (vezes ? ' ' + vezes : '');
    }).join(' + ');
  }

  protected diaBr(iso: string | null): string {
    return iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}` : '—';
  }

  protected idade(iso: string | null): number | null {
    if (!iso) return null;
    const n = new Date(`${iso}T12:00:00`), h = new Date();
    return h.getFullYear() - n.getFullYear() - (h.getMonth() < n.getMonth() || (h.getMonth() === n.getMonth() && h.getDate() < n.getDate()) ? 1 : 0);
  }

  protected rotuloStatusOs(s: string): string {
    return ({ Recebida: 'recebida', EmAndamento: 'encordoando', Pronta: 'pronta', Entregue: 'entregue', Cancelada: 'cancelada' } as Record<string, string>)[s] ?? s;
  }

  private loja(): string {
    return this.auth.usuario()?.empresa.nome ?? 'loja';
  }
}

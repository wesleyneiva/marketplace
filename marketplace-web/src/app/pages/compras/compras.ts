import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ItemSugestao, Pedido, SugestaoFornecedor } from '../../core/api/compras.api';
import { DiaPipe } from '../../shared/dia.pipe';
import { QuantidadePipe } from '../../shared/quantidade.pipe';

interface Escolha { marcado: boolean; quantidade: number; custo: number }
interface Conferencia { itemId: number; produto: string; unidade: string; perecivel: boolean; pedido: number; recebido: number; validade: string; custo: number }

@Component({
  selector: 'app-compras',
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe, DecimalPipe, DiaPipe, QuantidadePipe],
  templateUrl: './compras.html',
  styleUrl: './compras.scss',
})
export class Compras {
  private readonly http = inject(HttpClient);

  protected readonly aba = signal<'sugestao' | 'pedidos'>('sugestao');
  protected readonly aviso = signal<string | null>(null);
  protected readonly ocupado = signal(false);

  // ---------------------------------------------------------------- sugestão
  protected readonly sugestao = httpResource<SugestaoFornecedor[]>(() => (this.aba() === 'sugestao' ? '/api/compras/sugestao' : undefined));

  // Escolhas do usuário por produto (marcado? quanto? por quanto?). Começa com o sugerido.
  private readonly escolhas = new Map<number, Escolha>();
  protected escolha(i: ItemSugestao): Escolha {
    let e = this.escolhas.get(i.produtoId);
    if (!e) {
      e = { marcado: true, quantidade: i.sugerido, custo: i.custoUnitario };
      this.escolhas.set(i.produtoId, e);
    }
    return e;
  }

  protected totalSelecionado(s: SugestaoFornecedor): number {
    return s.itens.reduce((t, i) => {
      const e = this.escolha(i);
      return e.marcado ? t + (e.quantidade || 0) * (e.custo || 0) : t;
    }, 0);
  }

  protected selecionados(s: SugestaoFornecedor): number {
    return s.itens.filter((i) => this.escolha(i).marcado && this.escolha(i).quantidade > 0).length;
  }

  async criarPedido(s: SugestaoFornecedor, enviar: boolean): Promise<void> {
    const itens = s.itens
      .filter((i) => this.escolha(i).marcado && this.escolha(i).quantidade > 0)
      .map((i) => ({ produtoId: i.produtoId, quantidade: this.escolha(i).quantidade, custoUnitario: this.escolha(i).custo }));
    if (!itens.length) return alert('Marque pelo menos um item.');

    await this.executar(async () => {
      const p = await firstValueFrom(this.http.post<Pedido>('/api/compras/pedidos', { fornecedorId: s.fornecedorId, itens, enviar }));
      s.itens.forEach((i) => this.escolhas.delete(i.produtoId));
      this.mostrarAviso(enviar
        ? `Pedido #${p.id} enviado para ${p.fornecedor}: entrega prevista em ${this.dia(p.previsaoEntrega)}.`
        : `Pedido #${p.id} salvo como rascunho.`);
      this.sugestao.reload();
    });
  }

  // ---------------------------------------------------------------- pedidos
  protected readonly filtro = signal<'abertos' | 'Recebido' | 'Cancelado' | 'todos'>('abertos');
  protected readonly pedidos = httpResource<Pedido[]>(() => {
    if (this.aba() !== 'pedidos') return undefined;
    const filtro = this.filtro();
    const params: Record<string, string> = filtro === 'Recebido' || filtro === 'Cancelado' ? { status: filtro } : {};
    return { url: '/api/compras/pedidos', params };
  });
  protected readonly pedidosVisiveis = computed(() => {
    const lista = this.pedidos.value() ?? [];
    return this.filtro() === 'abertos' ? lista.filter((p) => p.status === 'Rascunho' || p.status === 'Enviado') : lista;
  });
  protected readonly aberto = signal<number | null>(null);

  async enviar(p: Pedido): Promise<void> {
    await this.executar(async () => {
      const r = await firstValueFrom(this.http.post<Pedido>(`/api/compras/pedidos/${p.id}/enviar`, {}));
      this.mostrarAviso(`Pedido #${r.id} enviado: entrega prevista em ${this.dia(r.previsaoEntrega)}.`);
      this.pedidos.reload();
    });
  }

  async cancelar(p: Pedido): Promise<void> {
    if (!confirm(`Cancelar o pedido #${p.id} (${p.fornecedor})?`)) return;
    await this.executar(async () => {
      await firstValueFrom(this.http.post(`/api/compras/pedidos/${p.id}/cancelar`, {}));
      this.mostrarAviso(`Pedido #${p.id} cancelado.`);
      this.pedidos.reload();
    });
  }

  // ----- Conferência na entrega -----
  private readonly dlgReceber = viewChild<ElementRef<HTMLDialogElement>>('dlgReceber');
  protected readonly recebendo = signal<Pedido | null>(null);
  protected conferencia: Conferencia[] = [];
  protected readonly erroReceber = signal<string | null>(null);

  abrirRecebimento(p: Pedido): void {
    this.recebendo.set(p);
    this.conferencia = p.itens.map((i) => ({
      itemId: i.id, produto: i.produto, unidade: i.unidade, perecivel: i.perecivel,
      pedido: i.quantidade, recebido: i.quantidade, validade: '', custo: i.custoUnitario,
    }));
    this.erroReceber.set(null);
    this.dlgReceber()?.nativeElement.showModal();
  }

  async confirmarRecebimento(): Promise<void> {
    const p = this.recebendo();
    if (!p) return;
    const semValidade = this.conferencia.find((c) => c.perecivel && c.recebido > 0 && !c.validade);
    if (semValidade) return this.erroReceber.set(`Informe a validade de "${semValidade.produto}".`);

    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post(`/api/compras/pedidos/${p.id}/receber`, {
        itens: this.conferencia.map((c) => ({ itemId: c.itemId, quantidadeRecebida: c.recebido, validade: c.validade || null, custoUnitario: c.custo })),
      }));
      this.dlgReceber()?.nativeElement.close();
      this.mostrarAviso(`Pedido #${p.id} recebido: a mercadoria entrou no estoque.`);
      this.pedidos.reload();
    } catch (e) {
      this.erroReceber.set(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  protected faltou(c: Conferencia): boolean {
    return c.recebido < c.pedido;
  }

  // ---------------------------------------------------------------- utilidades
  protected dia(iso: string | null): string {
    return iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : '—';
  }

  private async executar(acao: () => Promise<void>): Promise<void> {
    this.ocupado.set(true);
    try {
      await acao();
    } catch (e) {
      alert(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  private mostrarAviso(texto: string): void {
    this.aviso.set(texto);
    setTimeout(() => this.aviso.set(null), 6000);
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      if (e.error?.errors) return Object.values<string[]>(e.error.errors).flat().join(' ');
    }
    return 'Não foi possível concluir.';
  }
}

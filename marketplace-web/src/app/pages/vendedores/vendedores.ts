import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ComissaoMes, Vendedor } from '../../core/api/clientes.api';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';
import { Icone } from '../../shared/icone';

interface CategoriaComissao { id: number; nome: string; comissaoPercentual: number | null }

// Vendedores (quem atende; escolhido no caixa) e a comissão de cada mês. Fechado o mês, "Lançar em Contas a pagar"
// cria uma conta por vendedor (vence dia 5 do mês seguinte). Categoria com % próprio vale no lugar do % do vendedor.
@Component({
  selector: 'app-vendedores',
  imports: [FormsModule, CurrencyPipe, DecimalPipe, RouterLink, FecharAoClicarFora, Icone],
  templateUrl: './vendedores.html',
  styleUrls: ['../usuarios/usuarios.scss', '../clientes/clientes.scss', './vendedores.scss'],
})
export class Vendedores {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly ehAdmin = computed(() => this.auth.temPerfil('Administrador'));

  protected readonly vendedores = httpResource<Vendedor[]>(() => ({ url: '/api/vendedores', params: { todos: true } }));
  protected readonly mes = signal(this.mesAtual());
  protected readonly comissao = httpResource<ComissaoMes>(() => ({ url: '/api/vendedores/comissao', params: { mes: this.mes() } }));
  protected readonly categorias = httpResource<CategoriaComissao[]>(() => '/api/vendedores/categorias');
  protected readonly comPercentual = computed(() => (this.categorias.value() ?? []).filter((c) => c.comissaoPercentual !== null));

  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected form = { id: 0, nome: '', comissaoPercentual: 3, ativo: true };
  protected readonly erro = signal<string | null>(null);
  protected readonly aviso = signal<{ ok: boolean; texto: string } | null>(null);
  protected readonly ocupado = signal(false);
  protected categoriaEscolhida: number | null = null;
  protected percentualCategoria: number | null = null;

  protected abrir(v?: Vendedor): void {
    this.form = v ? { ...v } : { id: 0, nome: '', comissaoPercentual: 3, ativo: true };
    this.erro.set(null);
    this.dlg().nativeElement.showModal();
  }

  protected async salvar(): Promise<void> {
    this.ocupado.set(true);
    this.erro.set(null);
    try {
      const corpo = { nome: this.form.nome, comissaoPercentual: Number(this.form.comissaoPercentual) || 0, ativo: this.form.ativo };
      await firstValueFrom(this.form.id ? this.http.put(`/api/vendedores/${this.form.id}`, corpo) : this.http.post('/api/vendedores', corpo));
      this.dlg().nativeElement.close();
      this.vendedores.reload();
      this.comissao.reload();
    } catch (e) {
      this.erro.set(this.mensagem(e));
    } finally {
      this.ocupado.set(false);
    }
  }

  protected mudarMes(delta: number): void {
    const [a, m] = this.mes().split('-').map(Number);
    const d = new Date(a, m - 1 + delta, 1);
    this.mes.set(`${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`);
    this.aviso.set(null);
  }

  protected nomeMes(): string {
    const [a, m] = this.mes().split('-').map(Number);
    const t = new Date(a, m - 1, 1).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' });
    return t.charAt(0).toUpperCase() + t.slice(1);
  }

  protected async lancar(): Promise<void> {
    if (!confirm(`Lançar as comissões de ${this.nomeMes()} em Contas a pagar?`)) return;
    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post('/api/vendedores/comissao/lancar', {}, { params: { mes: this.mes() } }));
      this.aviso.set({ ok: true, texto: 'Comissões lançadas em Contas a pagar (vencem dia 5).' });
      this.comissao.reload();
    } catch (e) {
      this.aviso.set({ ok: false, texto: this.mensagem(e) });
    } finally {
      this.ocupado.set(false);
    }
  }

  protected async salvarCategoria(id: number | null, percentual: number | null): Promise<void> {
    if (!id) return;
    try {
      await firstValueFrom(this.http.put('/api/vendedores/categorias', { categoriaId: id, comissaoPercentual: percentual === null || (percentual as unknown) === '' ? null : Number(percentual) }));
      this.categoriaEscolhida = null;
      this.percentualCategoria = null;
      this.categorias.reload();
      this.comissao.reload();
    } catch (e) {
      alert(this.mensagem(e));
    }
  }

  protected pendentes(c: ComissaoMes): number {
    return c.vendedores.filter((v) => !v.lancada && v.total > 0).length;
  }

  private mesAtual(): string {
    const h = new Date();
    return `${h.getFullYear()}-${String(h.getMonth() + 1).padStart(2, '0')}`;
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) return e.error?.mensagem ?? Object.values<string[]>(e.error?.errors ?? {}).flat()[0] ?? 'Não foi possível.';
    return 'Não foi possível.';
  }
}

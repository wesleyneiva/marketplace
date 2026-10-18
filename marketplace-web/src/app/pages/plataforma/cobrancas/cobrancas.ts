import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Icone } from '../../../shared/icone';
import { DiaPipe } from '../../../shared/dia.pipe';
import { API, ConfiguracaoCobranca, FORMAS, Fatura, hojeIso, linkCobranca, mensagemErro, mesAno } from '../comum';
import { CampoData } from '../../../shared/campo-data';
import { FecharAoClicarFora } from '../../../shared/fechar-ao-clicar-fora';

type Aba = 'abertas' | 'atrasadas' | 'pagas' | 'todas';
interface ClienteLista { id: number; nome: string; cortesia: boolean }

// Mensalidades dos clientes: receber, cobrar no WhatsApp, cancelar, gerar agora e as regras de cobrança/bloqueio.
@Component({
  selector: 'app-cobrancas',
  imports: [FecharAoClicarFora, Icone, FormsModule, CurrencyPipe, DiaPipe, RouterLink, CampoData],
  templateUrl: './cobrancas.html',
  styleUrl: './cobrancas.scss',
})
export class Cobrancas {
  private readonly http = inject(HttpClient);
  protected readonly mesAno = mesAno;
  protected readonly formas = FORMAS;

  protected readonly aba = signal<Aba>('abertas');
  protected readonly faturas = httpResource<Fatura[]>(() => `${API}/faturas?situacao=${this.aba()}`);
  protected readonly config = httpResource<ConfiguracaoCobranca>(() => `${API}/cobranca/configuracao`);
  protected readonly clientes = httpResource<ClienteLista[]>(() => `${API}/empresas`);

  protected readonly totalAberto = computed(() => (this.faturas.value() ?? []).filter((f) => f.status === 'Aberta').reduce((s, f) => s + f.valor, 0));
  protected readonly aviso = signal<{ ok: boolean; texto: string } | null>(null);
  protected readonly processando = signal<number | null>(null);

  // ----- Receber -----
  private readonly dlgPagar = viewChild<ElementRef<HTMLDialogElement>>('dlgPagar');
  protected pagando: Fatura | null = null;
  protected pagamento = { pagaEm: hojeIso(), valorPago: 0, forma: 'Pix', observacao: '' };
  protected readonly erroPagar = signal<string | null>(null);

  abrirPagar(f: Fatura): void {
    this.pagando = f;
    this.pagamento = { pagaEm: hojeIso(), valorPago: f.valor, forma: 'Pix', observacao: '' };
    this.erroPagar.set(null);
    this.dlgPagar()?.nativeElement.showModal();
  }

  async confirmarPagamento(): Promise<void> {
    const f = this.pagando;
    if (!f) return;
    this.erroPagar.set(null);
    try {
      const r = await firstValueFrom(this.http.post<{ liberado: boolean }>(`${API}/faturas/${f.id}/pagar`, this.pagamento));
      this.aviso.set({ ok: true, texto: `${f.cliente}: ${mesAno(f.competencia)} recebida.` + (r.liberado ? ' O cliente estava bloqueado por atraso e foi liberado.' : '') });
      this.dlgPagar()?.nativeElement.close();
      this.faturas.reload();
    } catch (e) {
      this.erroPagar.set(mensagemErro(e));
    }
  }

  // ----- Outras ações -----
  async desfazer(f: Fatura): Promise<void> {
    if (!confirm(`Desfazer o recebimento de ${f.cliente} (${mesAno(f.competencia)})? Ela volta a ficar em aberto.`)) return;
    await this.acao(f, `${API}/faturas/${f.id}/desfazer-pagamento`, {}, 'Recebimento desfeito.');
  }

  async cancelar(f: Fatura): Promise<void> {
    const motivo = prompt(`Cancelar a mensalidade de ${f.cliente} (${mesAno(f.competencia)})?\n\nMotivo (opcional):`);
    if (motivo === null) return;
    await this.acao(f, `${API}/faturas/${f.id}/cancelar`, { motivo }, 'Mensalidade cancelada.');
  }

  async gerarAgora(): Promise<void> {
    this.aviso.set(null);
    try {
      const r = await firstValueFrom(this.http.post<{ novas: number; bloqueados: number }>(`${API}/faturas/gerar`, {}));
      this.aviso.set({ ok: true, texto: `${r.novas} mensalidade(s) nova(s)` + (r.bloqueados ? `; ${r.bloqueados} cliente(s) bloqueado(s) por atraso.` : '.') });
      this.faturas.reload();
    } catch (e) {
      this.aviso.set({ ok: false, texto: mensagemErro(e) });
    }
  }

  private async acao(f: Fatura, url: string, corpo: unknown, ok: string): Promise<void> {
    this.processando.set(f.id);
    this.aviso.set(null);
    try {
      await firstValueFrom(this.http.post(url, corpo));
      this.aviso.set({ ok: true, texto: `${f.cliente}: ${ok}` });
      this.faturas.reload();
    } catch (e) {
      this.aviso.set({ ok: false, texto: mensagemErro(e) });
    } finally {
      this.processando.set(null);
    }
  }

  protected whatsapp(f: Fatura): string | null {
    return linkCobranca(f, this.config.value());
  }

  // ----- Cobrança avulsa -----
  private readonly dlgNova = viewChild<ElementRef<HTMLDialogElement>>('dlgNova');
  protected nova = { clienteId: 0, mes: hojeIso().slice(0, 7), valor: null as number | null, descricao: '' };
  protected readonly erroNova = signal<string | null>(null);

  abrirNova(): void {
    this.nova = { clienteId: 0, mes: hojeIso().slice(0, 7), valor: null, descricao: '' };
    this.erroNova.set(null);
    this.dlgNova()?.nativeElement.showModal();
  }

  async criarNova(): Promise<void> {
    this.erroNova.set(null);
    try {
      await firstValueFrom(this.http.post(`${API}/faturas`, {
        clienteId: this.nova.clienteId, competencia: `${this.nova.mes}-01`, valor: this.nova.valor || null, descricao: this.nova.descricao || null,
      }));
      this.dlgNova()?.nativeElement.close();
      this.aviso.set({ ok: true, texto: 'Cobrança criada.' });
      this.faturas.reload();
    } catch (e) {
      this.erroNova.set(mensagemErro(e));
    }
  }

  // ----- Regras -----
  protected regras: ConfiguracaoCobranca | null = null;
  protected readonly avisoRegras = signal<{ ok: boolean; texto: string } | null>(null);

  protected preencherRegras(c: ConfiguracaoCobranca): boolean {
    if (!this.regras) this.regras = { ...c };
    return true;
  }

  async salvarRegras(): Promise<void> {
    this.avisoRegras.set(null);
    try {
      this.regras = { ...(await firstValueFrom(this.http.put<ConfiguracaoCobranca>(`${API}/cobranca/configuracao`, this.regras))) };
      this.config.reload();
      this.avisoRegras.set({ ok: true, texto: 'Regras salvas.' });
    } catch (e) {
      this.avisoRegras.set({ ok: false, texto: mensagemErro(e) });
    }
  }
}

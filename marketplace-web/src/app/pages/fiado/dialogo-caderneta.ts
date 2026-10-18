import { Component, ElementRef, computed, inject, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { DiaPipe } from '../../shared/dia.pipe';
import { Caderneta, LancamentoFiado, URL_FIADO, linkCobranca, telefoneBonito } from '../../core/api/fiado.api';
import { ContaFinanceira, URL_FINANCEIRO, hojeIso, mensagemDeErro } from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';
import { CampoData } from '../../shared/campo-data';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

// A caderneta de UM cliente: o extrato, receber fora do caixa, desfazer, dar baixa (Administrador) e cobrar no WhatsApp.
@Component({
  selector: 'app-dialogo-caderneta',
  imports: [FecharAoClicarFora, Icone, CurrencyPipe, DatePipe, FormsModule, DiaPipe, CampoData],
  templateUrl: './dialogo-caderneta.html',
  styleUrls: ['../financeiro/financeiro.scss', './fiado.scss'],
})
export class DialogoCaderneta {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  readonly mudou = output<void>(); // avisa a lista para recarregar (saldo mudou)
  readonly editar = output<number>();

  protected readonly telefoneBonito = telefoneBonito;
  protected readonly ehAdmin = computed(() => this.auth.temPerfil('Administrador'));
  protected readonly hoje = hojeIso();

  protected readonly caderneta = signal<Caderneta | null>(null);
  protected readonly carregando = signal(false);
  protected readonly contas = signal<ContaFinanceira[]>([]);
  protected readonly painel = signal<'nada' | 'receber' | 'baixa'>('nada');
  protected readonly retorno = signal<{ texto: string; ruim?: boolean } | null>(null);
  protected readonly salvando = signal(false);
  protected receber = { valor: null as number | null, data: '', forma: 'Pix', contaFinanceiraId: 0, observacao: '' };
  protected baixa = { valor: null as number | null, motivo: '' };

  protected readonly whatsapp = computed(() => {
    const c = this.caderneta()?.cliente;
    return c && c.saldo > 0 ? linkCobranca(c, this.auth.usuario()?.empresa.nome ?? 'mercado') : null;
  });

  async abrir(id: number): Promise<void> {
    this.painel.set('nada');
    this.retorno.set(null);
    this.caderneta.set(null);
    this.dlg().nativeElement.showModal();
    await this.carregar(id);
  }

  private async carregar(id: number): Promise<void> {
    this.carregando.set(true);
    try {
      this.caderneta.set(await firstValueFrom(this.http.get<Caderneta>(`${URL_FIADO}/clientes/${id}`)));
    } catch (e) {
      this.retorno.set({ texto: mensagemDeErro(e), ruim: true });
    } finally {
      this.carregando.set(false);
    }
  }

  protected async abrirReceber(): Promise<void> {
    const c = this.caderneta()!.cliente;
    if (!this.contas().length) this.contas.set(await firstValueFrom(this.http.get<ContaFinanceira[]>(`${URL_FINANCEIRO}/contas`)));
    const banco = this.contas().find((x) => x.tipo === 'Banco') ?? this.contas()[0];
    this.receber = { valor: Math.max(0, c.saldo), data: this.hoje, forma: 'Pix', contaFinanceiraId: banco?.id ?? 0, observacao: '' };
    this.retorno.set(null);
    this.painel.set('receber');
  }

  // Forma escolhida → sugere a conta (dinheiro = gaveta; o resto = banco).
  protected formaMudou(forma: string): void {
    this.receber.forma = forma;
    const tipo = forma === 'Dinheiro' ? 'Dinheiro' : 'Banco';
    const conta = this.contas().find((x) => x.tipo === tipo);
    if (conta) this.receber.contaFinanceiraId = conta.id;
  }

  protected async confirmarReceber(): Promise<void> {
    const id = this.caderneta()!.cliente.id;
    await this.fazer(async () => {
      await firstValueFrom(this.http.post(`${URL_FIADO}/clientes/${id}/pagamentos`, { ...this.receber, observacao: this.receber.observacao || null }));
      return `Recebido ${this.real(this.receber.valor ?? 0)}. Entrou no Financeiro.`;
    });
  }

  protected abrirBaixa(): void {
    this.baixa = { valor: Math.max(0, this.caderneta()!.cliente.saldo), motivo: '' };
    this.retorno.set(null);
    this.painel.set('baixa');
  }

  protected async confirmarBaixa(): Promise<void> {
    const c = this.caderneta()!.cliente;
    if (!confirm(`Dar baixa de ${this.real(this.baixa.valor ?? 0)} na dívida de ${c.nome}? Vira despesa (Inadimplência) na DRE.`)) return;
    await this.fazer(async () => {
      await firstValueFrom(this.http.post(`${URL_FIADO}/clientes/${c.id}/baixa`, this.baixa));
      return `Baixa de ${this.real(this.baixa.valor ?? 0)} registrada.`;
    });
  }

  protected async desfazer(l: LancamentoFiado): Promise<void> {
    if (!confirm(`Desfazer o recebimento de ${this.real(l.valor)}? Ele sai também do Financeiro.`)) return;
    await this.fazer(async () => {
      await firstValueFrom(this.http.delete(`${URL_FIADO}/pagamentos/${l.id}`));
      return 'Recebimento desfeito.';
    });
  }

  protected async alternarBloqueio(): Promise<void> {
    const c = this.caderneta()!.cliente;
    await this.fazer(async () => {
      await firstValueFrom(this.http.put(`${URL_FIADO}/clientes/${c.id}`, { ...c, bloqueado: !c.bloqueado }));
      return c.bloqueado ? 'Desbloqueado: volta a comprar no fiado (dentro do limite).' : 'Bloqueado: só um gerente vende fiado para ele(a).';
    });
  }

  protected cobrar(): void {
    const link = this.whatsapp();
    if (link) window.open(link, '_blank', 'noopener');
  }

  private async fazer(acao: () => Promise<string>): Promise<void> {
    this.salvando.set(true);
    this.retorno.set(null);
    try {
      const texto = await acao();
      this.painel.set('nada');
      this.retorno.set({ texto });
      await this.carregar(this.caderneta()!.cliente.id);
      this.mudou.emit();
    } catch (e) {
      this.retorno.set({ texto: mensagemDeErro(e), ruim: true });
    } finally {
      this.salvando.set(false);
    }
  }

  protected rotuloTipo(l: LancamentoFiado): string {
    return { Compra: 'Compra', Pagamento: 'Pagamento', Estorno: 'Estorno', Baixa: 'Baixa' }[l.tipo];
  }

  protected iconeTipo(l: LancamentoFiado): string {
    return { Compra: 'shopping-cart', Pagamento: 'banknote', Estorno: 'undo-2', Baixa: 'receipt' }[l.tipo];
  }

  protected real(v: number): string {
    return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }

  protected fechar(): void {
    this.dlg().nativeElement.close();
  }
}

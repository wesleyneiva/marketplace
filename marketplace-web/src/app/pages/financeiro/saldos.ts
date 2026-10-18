import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import {
  CaixaAberto, ContaFinanceira, ICONE_CONTA, ICONE_FORMA, NOME_FORMA, NOME_TIPO_CONTA, TipoConta, URL_FINANCEIRO, hojeIso, mensagemDeErro,
} from '../../core/api/financeiro.api';
import { DialogoTransferencia } from './dialogo-transferencia';
import { Icone } from '../../shared/icone';
import { CampoData } from '../../shared/campo-data';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

// Aba "Saldos": um cartão por conta (gaveta, cofre, banco...) com o saldo de hoje.
@Component({
  selector: 'app-saldos',
  imports: [FecharAoClicarFora, Icone, CurrencyPipe, FormsModule, DiaPipe, DialogoTransferencia, CampoData],
  templateUrl: './saldos.html',
  styleUrls: ['./financeiro.scss', './saldos.scss'],
})
export class Saldos {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly icone = ICONE_CONTA;
  protected readonly nomeTipo = NOME_TIPO_CONTA;
  protected readonly tipos: TipoConta[] = ['Dinheiro', 'Banco', 'Maquininha'];
  protected readonly hoje = hojeIso();

  protected readonly mostrarInativas = signal(false);
  protected readonly contas = httpResource<ContaFinanceira[]>(() => ({
    url: `${URL_FINANCEIRO}/contas`, params: { incluirInativas: this.mostrarInativas() },
  }));
  // Caixas abertos agora: as vendas deles entram no Financeiro só no fechamento ("em andamento").
  protected readonly caixasAbertos = httpResource<CaixaAberto[]>(() => `${URL_FINANCEIRO}/caixas-abertos`);
  protected readonly totalAbertos = computed(() => (this.caixasAbertos.value() ?? []).reduce((s, c) => s + c.total, 0));
  protected readonly nomeForma = NOME_FORMA;
  protected readonly iconeForma = ICONE_FORMA;

  protected readonly ativas = computed(() => (this.contas.value() ?? []).filter((c) => c.ativa));
  protected readonly total = computed(() => this.ativas().reduce((s, c) => s + c.saldo, 0));
  protected readonly emDinheiro = computed(() => this.ativas().filter((c) => c.tipo === 'Dinheiro').reduce((s, c) => s + c.saldo, 0));
  protected readonly noBanco = computed(() => this.ativas().filter((c) => c.tipo !== 'Dinheiro').reduce((s, c) => s + c.saldo, 0));

  // Retorno junto da ação: no topo (transferência / conta nova) ou dentro do cartão editado.
  protected readonly retorno = signal<string | null>(null);
  protected readonly retornoConta = signal<{ id: number; texto: string } | null>(null);

  // Janela de conta (nova / editar).
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlgConta');
  private readonly transferencia = viewChild.required(DialogoTransferencia);
  protected readonly editando = signal<ContaFinanceira | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected form = this.vazio();

  protected abrirExtrato(c: ContaFinanceira): void {
    this.router.navigate(['/financeiro'], { queryParams: { aba: 'lancamentos', conta: c.id } });
  }

  private transferenciaDoCartao: number | null = null; // a transferência saiu de um cartão: o retorno aparece nele

  protected transferir(deContaId?: number): void {
    this.retorno.set(null);
    this.transferenciaDoCartao = deContaId ?? null;
    this.transferencia().abrir(deContaId);
  }

  protected transferiu(texto: string): void {
    if (this.transferenciaDoCartao) this.retornoConta.set({ id: this.transferenciaDoCartao, texto });
    else this.retorno.set(texto);
    this.contas.reload();
  }

  // Gaveta negativa: o caixa abriu com mais troco do que a conta tinha (o troco veio de outro lugar, quase sempre do cofre).
  protected corrigirGaveta(c: ContaFinanceira): void {
    this.retorno.set(null);
    this.transferenciaDoCartao = c.id;
    this.transferencia().abrir(undefined, c.id, -c.saldo);
  }

  protected abrirNova(): void {
    this.editando.set(null);
    this.form = this.vazio();
    this.erro.set(null);
    this.dlg().nativeElement.showModal();
  }

  protected abrirEdicao(c: ContaFinanceira): void {
    this.editando.set(c);
    this.form = {
      nome: c.nome, tipo: c.tipo, banco: c.banco ?? '', agencia: c.agencia ?? '', numero: c.numero ?? '',
      saldoInicial: c.saldoInicial, saldoInicialEm: c.saldoInicialEm,
    };
    this.erro.set(null);
    this.dlg().nativeElement.showModal();
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const dados = {
        ...this.form,
        banco: this.form.tipo === 'Banco' ? this.form.banco || null : null,
        agencia: this.form.tipo === 'Banco' ? this.form.agencia || null : null,
        numero: this.form.tipo === 'Banco' ? this.form.numero || null : null,
        saldoInicial: this.form.saldoInicial ?? 0,
      };
      const e = this.editando();
      const salva = e
        ? await firstValueFrom(this.http.put<ContaFinanceira>(`${URL_FINANCEIRO}/contas/${e.id}`, dados))
        : await firstValueFrom(this.http.post<ContaFinanceira>(`${URL_FINANCEIRO}/contas`, dados));
      this.dlg().nativeElement.close();
      this.retorno.set(null);
      this.retornoConta.set({ id: salva.id, texto: e ? 'Conta atualizada.' : 'Conta criada.' });
      this.contas.reload();
    } catch (err) {
      this.erro.set(mensagemDeErro(err));
    } finally {
      this.salvando.set(false);
    }
  }

  protected async ativar(valor: boolean): Promise<void> {
    const c = this.editando();
    if (!c) return;
    this.salvando.set(true);
    this.erro.set(null);
    try {
      await firstValueFrom(this.http.post(`${URL_FINANCEIRO}/contas/${c.id}/ativa`, {}, { params: { valor } }));
      this.dlg().nativeElement.close();
      if (!valor) this.mostrarInativas.set(true); // para a conta desativada continuar à vista (e poder reativar)
      this.retornoConta.set({ id: c.id, texto: valor ? 'Conta reativada.' : 'Conta desativada.' });
      this.contas.reload();
    } catch (err) {
      this.erro.set(mensagemDeErro(err));
    } finally {
      this.salvando.set(false);
    }
  }

  private vazio() {
    return {
      nome: '', tipo: 'Banco' as TipoConta, banco: '', agencia: '', numero: '',
      saldoInicial: 0 as number | null, saldoInicialEm: hojeIso(),
    };
  }
}

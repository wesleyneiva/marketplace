import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { DiaPipe } from '../../shared/dia.pipe';
import { ClienteFiado, DIAS_TOLERANCIA, ResumoFiado, URL_FIADO, linkCobranca, telefoneBonito } from '../../core/api/fiado.api';
import { mensagemDeErro } from '../../core/api/financeiro.api';
import { DialogoCaderneta } from './dialogo-caderneta';

type Situacao = 'devendo' | 'atrasados' | 'todos' | 'inativos';

// 📒 Fiado: o painel da caderneta, os clientes da loja e o cadastro. A venda e o recebimento no balcão são no PDV.
@Component({
  selector: 'app-fiado',
  imports: [CurrencyPipe, DecimalPipe, FormsModule, DiaPipe, DialogoCaderneta],
  templateUrl: './fiado.html',
  styleUrls: ['../financeiro/financeiro.scss', './fiado.scss'],
})
export class Fiado {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly telefoneBonito = telefoneBonito;
  protected readonly tolerancia = DIAS_TOLERANCIA;

  protected readonly situacao = signal<Situacao>('devendo');
  protected readonly busca = signal('');
  private readonly buscaAplicada = signal('');
  private espera?: ReturnType<typeof setTimeout>;

  protected readonly resumo = httpResource<ResumoFiado>(() => `${URL_FIADO}/resumo`);
  protected readonly clientes = httpResource<ClienteFiado[]>(() => ({
    url: `${URL_FIADO}/clientes`,
    params: { situacao: this.situacao(), ...(this.buscaAplicada() ? { busca: this.buscaAplicada() } : {}) },
  }));

  // ----- Cadastro (novo / editar) -----
  private readonly dlgCliente = viewChild.required<ElementRef<HTMLDialogElement>>('dlgCliente');
  private readonly caderneta = viewChild.required(DialogoCaderneta);
  protected readonly editando = signal<ClienteFiado | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected readonly retorno = signal<string | null>(null);
  protected form = this.vazio();

  protected digitou(texto: string): void {
    this.busca.set(texto);
    clearTimeout(this.espera);
    this.espera = setTimeout(() => this.buscaAplicada.set(texto.trim()), 350);
  }

  protected abrirCaderneta(c: ClienteFiado): void {
    this.caderneta().abrir(c.id);
  }

  protected cobrar(c: ClienteFiado): void {
    const link = linkCobranca(c, this.auth.usuario()?.empresa.nome ?? 'mercado');
    if (link) window.open(link, '_blank', 'noopener');
  }

  protected temWhats(c: ClienteFiado): boolean {
    return (c.telefone ?? '').replace(/\D/g, '').length >= 10;
  }

  protected novo(): void {
    this.editando.set(null);
    this.form = this.vazio();
    this.erro.set(null);
    this.dlgCliente().nativeElement.showModal();
  }

  // Vem da lista ou da caderneta (botão Editar).
  protected async editar(id: number): Promise<void> {
    const c = this.clientes.value()?.find((x) => x.id === id)
      ?? (await firstValueFrom(this.http.get<{ cliente: ClienteFiado }>(`${URL_FIADO}/clientes/${id}`))).cliente;
    this.editando.set(c);
    this.form = {
      nome: c.nome, apelido: c.apelido ?? '', telefone: telefoneBonito(c.telefone), cpf: c.cpf ?? '', endereco: c.endereco ?? '',
      limiteCredito: c.limiteCredito, diaPagamento: c.diaPagamento, bloqueado: c.bloqueado, observacao: c.observacao ?? '',
    };
    this.erro.set(null);
    this.dlgCliente().nativeElement.showModal();
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const dados = {
        ...this.form, apelido: this.form.apelido || null, telefone: this.form.telefone || null, cpf: this.form.cpf || null,
        endereco: this.form.endereco || null, observacao: this.form.observacao || null, limiteCredito: this.form.limiteCredito ?? 0,
      };
      const e = this.editando();
      const c = e
        ? await firstValueFrom(this.http.put<ClienteFiado>(`${URL_FIADO}/clientes/${e.id}`, dados))
        : await firstValueFrom(this.http.post<ClienteFiado>(`${URL_FIADO}/clientes`, dados));
      this.dlgCliente().nativeElement.close();
      this.retorno.set(e ? `${c.nome}: cadastro atualizado.` : `${c.nome} cadastrado(a). ${c.limiteCredito > 0 ? 'Já pode comprar no fiado.' : 'Sem limite: só gerente vende fiado.'}`);
      this.recarregar();
    } catch (err) {
      this.erro.set(mensagemDeErro(err));
    } finally {
      this.salvando.set(false);
    }
  }

  protected async ativar(valor: boolean): Promise<void> {
    const e = this.editando();
    if (!e) return;
    this.salvando.set(true);
    this.erro.set(null);
    try {
      await firstValueFrom(this.http.post(`${URL_FIADO}/clientes/${e.id}/ativo`, {}, { params: { valor } }));
      this.dlgCliente().nativeElement.close();
      this.retorno.set(valor ? `${e.nome} reativado(a).` : `${e.nome} desativado(a).`);
      this.recarregar();
    } catch (err) {
      this.erro.set(mensagemDeErro(err));
    } finally {
      this.salvando.set(false);
    }
  }

  protected recarregar(): void {
    this.clientes.reload();
    this.resumo.reload();
  }

  // Quanto do limite já está usado (para a barrinha).
  protected uso(c: ClienteFiado): number {
    return c.limiteCredito > 0 ? Math.min(100, Math.max(0, (c.saldo / c.limiteCredito) * 100)) : c.saldo > 0 ? 100 : 0;
  }

  private vazio() {
    return {
      nome: '', apelido: '', telefone: '', cpf: '', endereco: '', limiteCredito: 200 as number | null, diaPagamento: 10,
      bloqueado: false, observacao: '',
    };
  }
}

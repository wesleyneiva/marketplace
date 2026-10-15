import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import {
  CategoriaFinanceira, ContaFinanceira, URL_FINANCEIRO, categoriasPorGrupo,
} from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';
import { CampoData } from '../../shared/campo-data';

// Formatos iguais a Contracts/ContasDtos.cs.
interface Conta {
  id: number;
  descricao: string;
  documento: string | null;
  vencimento: string;
  valor: number;
  situacao: 'Aberta' | 'Vencida' | 'VenceHoje' | 'Paga';
  diasParaVencer: number;
  fornecedorId: number | null;
  fornecedor: string | null;
  notaEntradaId: number | null;
  notaNumero: string | null;
  pagaEm: string | null;
  valorPago: number | null;
  formaPagamento: string | null;
  pagaPor: string | null;
  observacao: string | null;
  categoriaId: number | null;
  categoria: string | null;
  contaFinanceiraId: number | null;
  contaFinanceira: string | null;
}

interface Resumo {
  vencidas: number;
  valorVencidas: number;
  vencemHoje: number;
  valorHoje: number;
  proximos7Dias: number;
  valorProximos7Dias: number;
  abertas: number;
  valorAbertas: number;
  pagoNoMes: number;
}

type Filtro = 'abertas' | 'vencidas' | 'pagas' | 'todas';
const URL = '/api/contas';
const FORMAS = ['PIX', 'Boleto', 'Dinheiro', 'Transferência', 'Cartão de débito', 'Cartão de crédito', 'Cheque', 'Outros'];

// Contas a pagar: as parcelas das notas (entram sozinhas pela NF-e) e as lançadas à mão (aluguel, luz, internet...).
@Component({
  selector: 'app-contas',
  imports: [Icone, FormsModule, CurrencyPipe, DiaPipe, RouterLink, CampoData],
  templateUrl: './contas.html',
  styleUrl: './contas.scss',
})
export class Contas {
  private readonly http = inject(HttpClient);

  protected readonly formas = FORMAS;
  protected readonly hoje = new Date().toLocaleDateString('sv-SE');
  protected readonly filtro = signal<Filtro>('abertas');
  protected readonly contas = httpResource<Conta[]>(() => ({ url: URL, params: { situacao: this.filtro() } }));
  protected readonly resumo = httpResource<Resumo>(() => `${URL}/resumo`);
  protected readonly fornecedores = httpResource<{ id: number; nome: string; ativo: boolean }[]>(() => '/api/fornecedores');
  protected readonly categorias = httpResource<CategoriaFinanceira[]>(() => ({ url: `${URL_FINANCEIRO}/categorias`, params: { incluirInativas: true } }));
  protected readonly contasFinanceiras = httpResource<ContaFinanceira[]>(() => `${URL_FINANCEIRO}/contas`);
  protected readonly grupos = computed(() => categoriasPorGrupo(this.categorias.value() ?? [], 'Saida', this.editando()?.categoriaId));

  // ?situacao=pagas (ex.: veio do "Ver conta" do Financeiro).
  readonly situacao = input<string>();

  protected readonly aviso = signal<string | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);

  // Janela de lançar / editar.
  private readonly dlgConta = viewChild<ElementRef<HTMLDialogElement>>('dlgConta');
  protected readonly editando = signal<Conta | null>(null);
  protected form = this.formVazio();
  protected readonly sugerida = signal(false);
  protected readonly escolheuCategoria = signal(false); // escolheu à mão: a sugestão pela descrição não mexe mais
  private espera?: ReturnType<typeof setTimeout>;

  // Janela de pagar.
  private readonly dlgPagar = viewChild<ElementRef<HTMLDialogElement>>('dlgPagar');
  protected readonly pagando = signal<Conta | null>(null);
  protected pagamento = { pagaEm: '', valorPago: null as number | null, formaPagamento: 'PIX', contaFinanceiraId: 0 };
  private escolheuConta = false; // escolheu "saiu de" à mão: trocar a forma não muda mais a conta

  constructor() {
    effect(() => {
      const s = this.situacao();
      if (s === 'abertas' || s === 'vencidas' || s === 'pagas' || s === 'todas') untracked(() => this.filtro.set(s));
    });
  }

  mudarFiltro(f: Filtro): void {
    this.filtro.set(f);
    this.aviso.set(null);
  }

  abrirNova(): void {
    this.editando.set(null);
    this.form = this.formVazio();
    this.sugerida.set(false);
    this.escolheuCategoria.set(false);
    this.erro.set(null);
    this.dlgConta()?.nativeElement.showModal();
  }

  abrirEdicao(c: Conta): void {
    this.editando.set(c);
    this.form = {
      descricao: c.descricao, documento: c.documento ?? '', vencimento: c.vencimento, valor: c.valor, fornecedorId: c.fornecedorId,
      observacao: c.observacao ?? '', repetirMeses: 1, categoriaId: c.categoriaId,
    };
    this.sugerida.set(false);
    this.escolheuCategoria.set(true);
    this.erro.set(null);
    this.dlgConta()?.nativeElement.showModal();
  }

  // Enquanto digita a descrição: 0,4 s parado → a API sugere a categoria ("Conta de luz" → Energia elétrica).
  protected aoDigitar(): void {
    if (this.escolheuCategoria()) return;
    clearTimeout(this.espera);
    this.espera = setTimeout(async () => {
      const descricao = this.form.descricao.trim();
      if (descricao.length < 3) return;
      const r = await firstValueFrom(this.http.get<{ categoriaId: number | null }>(`${URL_FINANCEIRO}/categorias/sugerir`,
        { params: { descricao, natureza: 'Saida' } })).catch(() => null);
      if (r?.categoriaId && !this.escolheuCategoria()) {
        this.form.categoriaId = r.categoriaId;
        this.sugerida.set(false); // desliga e liga: redesenha mesmo se já estava "sugerida"
        this.sugerida.set(true);
      }
    }, 400);
  }

  async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const dados = { ...this.form, documento: this.form.documento || null, observacao: this.form.observacao || null };
      const e = this.editando();
      if (e) {
        await firstValueFrom(this.http.put(`${URL}/${e.id}`, dados));
        this.aviso.set('Conta atualizada.');
      } else {
        const criadas = await firstValueFrom(this.http.post<Conta[]>(URL, dados));
        this.aviso.set(criadas.length > 1 ? `${criadas.length} contas lançadas (uma por mês).` : 'Conta lançada.');
      }
      this.dlgConta()?.nativeElement.close();
      this.recarregar();
    } catch (err) {
      this.erro.set(this.mensagem(err));
    } finally {
      this.salvando.set(false);
    }
  }

  abrirPagamento(c: Conta): void {
    this.pagando.set(c);
    this.pagamento = { pagaEm: this.hoje, valorPago: c.valor, formaPagamento: c.notaEntradaId ? 'Boleto' : 'PIX', contaFinanceiraId: 0 };
    this.escolheuConta = false;
    this.contaPelaForma();
    this.erro.set(null);
    this.contasFinanceiras.reload(); // saldos atualizados na janela
    this.dlgPagar()?.nativeElement.showModal();
  }

  // Forma "Dinheiro" → sai da gaveta; as outras → do banco (a pessoa pode trocar).
  protected contaPelaForma(): void {
    if (this.escolheuConta) return;
    const contas = this.contasFinanceiras.value() ?? [];
    const tipo = this.pagamento.formaPagamento === 'Dinheiro' ? 'Dinheiro' : 'Banco';
    this.pagamento.contaFinanceiraId = (contas.find((c) => c.tipo === tipo) ?? contas[0])?.id ?? 0;
  }

  protected escolheuContaManual(): void {
    this.escolheuConta = true;
  }

  async pagar(): Promise<void> {
    const c = this.pagando();
    if (!c) return;
    this.salvando.set(true);
    this.erro.set(null);
    try {
      await firstValueFrom(this.http.post(`${URL}/${c.id}/pagar`, this.pagamento));
      this.aviso.set(`"${c.descricao}" marcada como paga.`);
      this.dlgPagar()?.nativeElement.close();
      this.recarregar();
    } catch (err) {
      this.erro.set(this.mensagem(err));
    } finally {
      this.salvando.set(false);
    }
  }

  async desfazer(c: Conta): Promise<void> {
    if (!confirm(`Desfazer o pagamento de "${c.descricao}"? Ela volta a ficar em aberto.`)) return;
    await this.acao(() => firstValueFrom(this.http.post(`${URL}/${c.id}/desfazer-pagamento`, {})), 'Pagamento desfeito.');
  }

  async excluir(c: Conta): Promise<void> {
    if (!confirm(`Excluir "${c.descricao}" (${c.valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })})?`)) return;
    await this.acao(() => firstValueFrom(this.http.delete(`${URL}/${c.id}`)), 'Conta excluída.');
  }

  protected quando(c: Conta): string {
    if (c.situacao === 'Paga') return '';
    const d = c.diasParaVencer;
    if (d < 0) return d === -1 ? 'venceu ontem' : `venceu há ${-d} dias`;
    if (d === 0) return 'vence hoje';
    if (d === 1) return 'vence amanhã';
    return `em ${d} dias`;
  }

  private async acao(fazer: () => Promise<unknown>, ok: string): Promise<void> {
    try {
      await fazer();
      this.aviso.set(ok);
      this.recarregar();
    } catch (err) {
      alert(this.mensagem(err));
    }
  }

  private recarregar(): void {
    this.contas.reload();
    this.resumo.reload();
    this.contasFinanceiras.reload();
  }

  private formVazio() {
    return {
      descricao: '', documento: '', vencimento: this.hoje, valor: null as number | null, fornecedorId: null as number | null,
      observacao: '', repetirMeses: 1, categoriaId: null as number | null,
    };
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      const erros = e.error?.errors as Record<string, string[]> | undefined;
      if (erros) return Object.values(erros).flat().join(' ');
    }
    return 'Não foi possível concluir. Tente de novo.';
  }
}

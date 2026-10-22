import { Component, ElementRef, computed, inject, linkedSignal, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Pagina, Produto } from '../../core/api/produtos.api';
import { HistoricoCliente, MOTIVOS_GARANTIA, NOME_STATUS, Ordem, ResumoOrdens, StatusOrdem, URL_ORDENS } from '../../core/api/ordens.api';
import { Icone } from '../../shared/icone';
import { Paginacao } from '../../shared/paginacao';
import { FecharAoClicarFora } from '../../shared/fechar-ao-clicar-fora';

type Aba = 'abertas' | 'prontas' | 'entregues' | 'todas';
const TAMANHO = 25;

// Oficina: o cliente deixa a raquete (encordoamento, troca de grip), a loja faz, avisa pelo WhatsApp e entrega.
// A cobrança é no caixa (serviço + corda são produtos); aqui fica o que fazer, o prazo e a situação.
@Component({
  selector: 'app-oficina',
  imports: [Icone, FormsModule, CurrencyPipe, DatePipe, RouterLink, Paginacao, FecharAoClicarFora],
  templateUrl: './oficina.html',
  styleUrl: './oficina.scss',
})
export class Oficina {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly nomeStatus = NOME_STATUS;
  protected readonly Math = Math;

  protected readonly aba = signal<Aba>('abertas');
  protected readonly busca = signal('');
  protected readonly pagina = signal(1);
  protected readonly ordens = httpResource<Pagina<Ordem>>(() => ({
    url: URL_ORDENS, params: { situacao: this.aba(), busca: this.busca(), pagina: this.pagina(), tamanho: TAMANHO },
  }));
  protected readonly lista = linkedSignal<Pagina<Ordem> | undefined, Pagina<Ordem> | undefined>({
    source: () => this.ordens.value(), computation: (a, anterior) => a ?? anterior?.value,
  });
  protected readonly paginas = computed(() => Math.max(1, Math.ceil((this.lista()?.total ?? 0) / TAMANHO)));
  protected readonly resumo = httpResource<ResumoOrdens>(() => `${URL_ORDENS}/resumo`);

  // Serviços e cordas para preencher a ordem (produtos da loja).
  protected readonly servicos = httpResource<Pagina<Produto>>(() => ({ url: '/api/produtos', params: { apenasServicos: true, tamanho: 100 } }));
  protected readonly opcoesServico = computed(() => (this.servicos.value()?.itens ?? []).filter((p) => p.servico && p.ativo));
  protected readonly cordas = httpResource<Pagina<Produto>>(() => ({ url: '/api/produtos', params: { busca: 'corda', tamanho: 50 } }));
  protected readonly opcoesCorda = computed(() => (this.cordas.value()?.itens ?? []).filter((p) => !p.servico && p.ativo && p.nome.startsWith('Corda')));

  protected readonly aviso = signal<string | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);

  // Janela da ordem (nova ou editar).
  private readonly dlg = viewChild<ElementRef<HTMLDialogElement>>('dlgOrdem');
  protected readonly editando = signal<Ordem | null>(null);
  protected form = this.formVazio();
  protected readonly historico = signal<HistoricoCliente[]>([]);

  private espera?: ReturnType<typeof setTimeout>;
  protected digitar(texto: string): void {
    clearTimeout(this.espera);
    this.espera = setTimeout(() => { this.busca.set(texto.trim()); this.pagina.set(1); }, 300);
  }

  protected mudarAba(a: Aba): void {
    this.aba.set(a);
    this.pagina.set(1);
  }

  private formVazio() {
    const amanha = new Date(Date.now() + 24 * 3600_000);
    amanha.setHours(17, 0, 0, 0);
    return {
      cliente: '', telefone: '', raquete: '', corda: '', tensao: '', servicoId: null as number | null, servico: '',
      valorServico: 0, cordaId: null as number | null, valor: 0, paga: false, prazo: paraCampo(amanha), observacao: '',
    };
  }

  protected abrirNova(): void {
    this.editando.set(null);
    this.form = this.formVazio();
    const primeiro = this.opcoesServico().find((s) => /encordoamento/i.test(s.nome));
    if (primeiro) this.escolherServico(primeiro.id);
    this.historico.set([]);
    this.erro.set(null);
    this.dlg()?.nativeElement.showModal();
  }

  protected abrirEdicao(o: Ordem): void {
    this.editando.set(o);
    this.form = {
      cliente: o.cliente, telefone: o.telefone ?? '', raquete: o.raquete, corda: o.corda ?? '', tensao: o.tensao ?? '',
      servicoId: null, servico: o.servico, valorServico: 0, cordaId: null, valor: o.valor, paga: o.paga,
      prazo: paraCampo(new Date(o.prazo)), observacao: o.observacao ?? '',
    };
    this.historico.set([]);
    this.erro.set(null);
    this.dlg()?.nativeElement.showModal();
  }

  protected escolherServico(id: number | null): void {
    const s = this.opcoesServico().find((x) => x.id === id);
    this.form.servicoId = id;
    if (s) {
      this.form.servico = s.nome;
      this.form.valorServico = s.precoVenda;
      if (/expresso/i.test(s.nome)) this.form.prazo = paraCampo(new Date(Date.now() + 3600_000));
    }
    this.recalcular();
  }

  protected escolherCorda(id: number | null): void {
    const c = this.opcoesCorda().find((x) => x.id === id);
    this.form.cordaId = id;
    this.form.corda = c ? c.nome.replace(/\s*\(set.*\)$/i, '') : this.form.corda;
    this.recalcular();
  }

  private recalcular(): void {
    const corda = this.opcoesCorda().find((x) => x.id === this.form.cordaId);
    this.form.valor = (this.form.valorServico || 0) + (corda?.precoVenda ?? 0);
  }

  // Cliente que já veio: mostra o que usou da última vez (raquete, corda, tensão).
  protected async buscarHistorico(): Promise<void> {
    const tel = this.form.telefone.replace(/\D/g, '');
    if (tel.length < 10 || this.editando()) return;
    try {
      const h = await firstValueFrom(this.http.get<HistoricoCliente[]>(`${URL_ORDENS}/historico`, { params: { telefone: tel } }));
      this.historico.set(h);
      if (h.length && !this.form.cliente) this.form.cliente = h[0].cliente;
    } catch { /* sem histórico */ }
  }

  protected usarDaUltimaVez(h: HistoricoCliente): void {
    this.form.raquete = h.raquete ?? this.form.raquete;
    this.form.corda = h.corda ?? this.form.corda;
    this.form.tensao = h.tensao ?? this.form.tensao;
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    const corpo = {
      cliente: this.form.cliente, telefone: this.form.telefone || null, raquete: this.form.raquete, corda: this.form.corda || null,
      tensao: this.form.tensao || null, servico: this.form.servico, valor: this.form.valor || 0, paga: this.form.paga,
      prazo: new Date(this.form.prazo).toISOString(), observacao: this.form.observacao || null,
    };
    try {
      const e = this.editando();
      const o = e
        ? await firstValueFrom(this.http.put<Ordem>(`${URL_ORDENS}/${e.id}`, corpo))
        : await firstValueFrom(this.http.post<Ordem>(URL_ORDENS, corpo));
      this.dlg()?.nativeElement.close();
      this.aviso.set(e ? `OS ${o.numero} atualizada.` : `OS ${o.numero} aberta para ${o.cliente}.`);
      this.recarregar();
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  protected async mudar(o: Ordem, status: StatusOrdem): Promise<void> {
    if (status === 'Cancelada' && !confirm(`Cancelar a OS ${o.numero} de ${o.cliente}?`)) return;
    try {
      await firstValueFrom(this.http.post<Ordem>(`${URL_ORDENS}/${o.id}/status`, { status }));
      this.aviso.set(`OS ${o.numero}: ${NOME_STATUS[status]}.`);
      this.recarregar();
    } catch (e) {
      this.erro.set(mensagem(e));
    }
  }

  protected async marcarPaga(o: Ordem): Promise<void> {
    try {
      await firstValueFrom(this.http.post<Ordem>(`${URL_ORDENS}/${o.id}/paga`, {}));
      this.aviso.set(`OS ${o.numero} marcada como paga.`);
      this.recarregar();
    } catch (e) {
      this.erro.set(mensagem(e));
    }
  }

  // ----- Refazer (garantia): a raquete voltou porque o serviço ficou ruim -----
  private readonly dlgGarantia = viewChild<ElementRef<HTMLDialogElement>>('dlgGarantia');
  protected readonly motivosGarantia = MOTIVOS_GARANTIA;
  protected readonly garantiaDe = signal<Ordem | null>(null);
  protected garantia = { motivo: '', cordaId: null as number | null, prazo: '', observacao: '' };

  protected abrirGarantia(o: Ordem): void {
    this.garantiaDe.set(o);
    // A mesma corda da primeira vez, se a loja tem em estoque; o encordoador troca se quiser.
    const mesma = this.opcoesCorda().find((c) => o.corda && c.nome.toLowerCase().includes(o.corda.toLowerCase().replace(/\s*\(.*\)$/, '')) && c.estoqueAtual > 0);
    this.garantia = { motivo: '', cordaId: mesma?.id ?? null, prazo: paraCampo(new Date(Date.now() + 3 * 3600_000)), observacao: '' };
    this.erro.set(null);
    this.dlgGarantia()?.nativeElement.showModal();
  }

  protected custoCorda(): number {
    return this.opcoesCorda().find((c) => c.id === this.garantia.cordaId)?.precoCusto ?? 0;
  }

  protected async salvarGarantia(): Promise<void> {
    const o = this.garantiaDe();
    if (!o) return;
    if (!this.garantia.motivo) { this.erro.set('Escolha o motivo do retrabalho.'); return; }
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const nova = await firstValueFrom(this.http.post<Ordem>(`${URL_ORDENS}/${o.id}/garantia`, {
        motivo: this.garantia.motivo, cordaProdutoId: this.garantia.cordaId,
        prazo: new Date(this.garantia.prazo).toISOString(), observacao: this.garantia.observacao || null,
      }));
      this.dlgGarantia()?.nativeElement.close();
      this.aviso.set(`OS ${nova.numero} aberta (garantia da OS ${o.numero}), sem cobrança` + (this.garantia.cordaId ? '; a corda saiu do estoque como garantia.' : '.'));
      this.mudarAba('abertas');
      this.recarregar();
      this.cordas.reload();
    } catch (e) {
      this.erro.set(mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  // "Sua raquete está pronta" pelo WhatsApp (abre o WhatsApp com a mensagem; a pessoa só envia).
  protected linkWhatsApp(o: Ordem): string | null {
    if (!o.telefone) return null;
    const tel = o.telefone.length <= 11 ? '55' + o.telefone : o.telefone;
    const loja = this.auth.usuario()?.empresa.nome ?? 'a loja';
    const texto = `Olá, ${o.cliente.split(' ')[0]}! Aqui é da ${loja}. Sua raquete ${o.raquete} já está pronta`
      + (o.corda ? ` (${o.corda}${o.tensao ? ', ' + o.tensao : ''})` : '') + `. ${o.paga ? 'Já está paga, é só retirar.' : `Valor: R$ ${o.valor.toFixed(2).replace('.', ',')}.`} OS nº ${o.numero}.`;
    return `https://wa.me/${tel}?text=${encodeURIComponent(texto)}`;
  }

  protected telefone(t: string | null): string {
    if (!t) return '';
    return t.length === 11 ? `(${t.slice(0, 2)}) ${t.slice(2, 7)}-${t.slice(7)}` : t;
  }

  private recarregar(): void {
    this.ordens.reload();
    this.resumo.reload();
  }
}

// "2026-10-22T17:00" (o formato do <input type="datetime-local">, na hora local).
function paraCampo(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

function mensagem(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.error?.mensagem) return e.error.mensagem;
    if (e.error?.errors) return Object.values(e.error.errors as Record<string, string[]>).flat().join(' ');
  }
  return 'Não foi possível concluir. Tente de novo.';
}

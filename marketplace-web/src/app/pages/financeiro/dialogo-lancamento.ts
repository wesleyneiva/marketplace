import { Component, ElementRef, computed, inject, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  CategoriaFinanceira, ContaFinanceira, Movimento, TipoMovimento, URL_FINANCEIRO,
  categoriasPorGrupo, hojeIso, mensagemDeErro,
} from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';
import { CampoData } from '../../shared/campo-data';

// Receita ou despesa avulsa, já paga/recebida (ex.: comprou material de limpeza no PIX, rendimento do banco).
// Para algo a pagar DEPOIS, o lugar é Contas a pagar.
@Component({
  selector: 'app-dialogo-lancamento',
  imports: [Icone, FormsModule, CurrencyPipe, CampoData],
  styleUrl: './financeiro.scss',
  template: `
    <dialog #dlg class="janela" (close)="erro.set(null)">
      <form (ngSubmit)="salvar()" #f="ngForm">
        <header>
          <h2>@if (!editando()) { <app-icone [nome]="tipo() === 'Entrada' ? 'plus' : 'minus'" /> }{{ editando() ? 'Editar lançamento' : tipo() === 'Entrada' ? 'Nova receita' : 'Nova despesa' }}</h2>
          <button type="button" class="fechar" (click)="dlg.close()"><app-icone nome="x" /></button>
        </header>
        <div class="corpo">
          <label>Descrição
            <input #campoDescricao name="descricao" [(ngModel)]="form.descricao" (ngModelChange)="aoDigitar()" required minlength="2" maxlength="200"
              [placeholder]="tipo() === 'Entrada' ? 'Ex.: Rendimento da poupança' : 'Ex.: Conta de luz, material de limpeza…'" />
          </label>
          <div class="duas">
            <label>Valor (R$)<input name="valor" type="number" min="0.01" step="0.01" [(ngModel)]="form.valor" required /></label>
            <label>Data<app-campo-data name="data" [max]="hoje" [(ngModel)]="form.data" required /></label>
          </div>
          <label>Categoria
            <select name="categoria" [(ngModel)]="form.categoriaId" (ngModelChange)="escolheuCategoria.set(true)" required>
              <option [ngValue]="null" disabled>Escolha…</option>
              @for (g of grupos(); track g.grupo) {
                <optgroup [label]="g.grupo">
                  @for (c of g.itens; track c.id) { <option [ngValue]="c.id">{{ c.codigo }} · {{ c.nome }}</option> }
                </optgroup>
              }
            </select>
            @if (sugerida()) { <span class="sugerida"><app-icone nome="lightbulb" /> Sugerida pela descrição. Pode trocar.</span> }
          </label>
          <label>{{ tipo() === 'Entrada' ? 'Entrou em' : 'Saiu de' }}
            <select name="conta" [(ngModel)]="form.contaId" required>
              @for (c of contas(); track c.id) {
                <option [ngValue]="c.id">{{ c.nome }} ({{ c.saldo | currency: 'BRL' }})</option>
              }
            </select>
          </label>
          <label>Documento <small>(opcional)</small><input name="documento" [(ngModel)]="form.documento" maxlength="60" placeholder="nº da nota, recibo…" /></label>
          @if (erro()) { <div class="erro" role="alert">{{ erro() }}</div> }
        </div>
        <footer>
          <button type="button" class="botao" (click)="dlg.close()">Cancelar</button>
          <button type="submit" class="botao primario" [disabled]="f.invalid || salvando()">
            {{ salvando() ? 'Salvando…' : editando() ? 'Salvar' : 'Lançar' }}
          </button>
        </footer>
      </form>
    </dialog>
  `,
})
export class DialogoLancamento {
  private readonly http = inject(HttpClient);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campoDescricao = viewChild.required<ElementRef<HTMLInputElement>>('campoDescricao');

  // id do lançamento salvo + texto do retorno; editou = veio de uma linha (o retorno aparece nela).
  readonly salvo = output<{ id: number; texto: string; editou: boolean }>();

  protected readonly hoje = hojeIso();
  protected readonly tipo = signal<TipoMovimento>('Saida');
  protected readonly editando = signal<Movimento | null>(null);
  protected readonly contas = signal<ContaFinanceira[]>([]);
  private readonly categorias = signal<CategoriaFinanceira[]>([]);
  protected readonly grupos = computed(() =>
    categoriasPorGrupo(this.categorias(), this.tipo(), this.editando()?.categoriaId));
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected readonly sugerida = signal(false);
  protected readonly escolheuCategoria = signal(false); // a pessoa escolheu à mão: a sugestão não mexe mais
  protected form = { descricao: '', valor: null as number | null, data: '', categoriaId: null as number | null, contaId: 0, documento: '' };
  private espera?: ReturnType<typeof setTimeout>;

  async abrir(tipo: TipoMovimento, mov?: Movimento, contaId?: number | null): Promise<void> {
    this.erro.set(null);
    this.sugerida.set(false);
    this.escolheuCategoria.set(!!mov);
    this.tipo.set(mov?.tipo ?? tipo);
    this.editando.set(mov ?? null);
    const [contas, categorias] = await Promise.all([
      firstValueFrom(this.http.get<ContaFinanceira[]>(`${URL_FINANCEIRO}/contas`)),
      firstValueFrom(this.http.get<CategoriaFinanceira[]>(`${URL_FINANCEIRO}/categorias`, { params: { incluirInativas: true } })),
    ]);
    this.contas.set(contas);
    this.categorias.set(categorias);
    // Conta padrão: a do filtro; senão banco (o mais comum para despesa avulsa).
    const padrao = contas.find((c) => c.id === contaId) ?? contas.find((c) => c.tipo === 'Banco') ?? contas[0];
    this.form = mov
      ? { descricao: mov.descricao, valor: mov.valor, data: mov.data, categoriaId: mov.categoriaId, contaId: mov.contaId, documento: mov.documento ?? '' }
      : { descricao: '', valor: null, data: this.hoje, categoriaId: null, contaId: padrao?.id ?? 0, documento: '' };
    this.dlg().nativeElement.showModal();
    setTimeout(() => this.campoDescricao().nativeElement.focus()); // cursor já na descrição
  }

  // Enquanto digita a descrição: depois de 0,4 s parado, pergunta à API qual categoria combina.
  protected aoDigitar(): void {
    if (this.escolheuCategoria()) return;
    clearTimeout(this.espera);
    this.espera = setTimeout(async () => {
      const descricao = this.form.descricao.trim();
      if (descricao.length < 3) return;
      const r = await firstValueFrom(this.http.get<{ categoriaId: number | null }>(`${URL_FINANCEIRO}/categorias/sugerir`,
        { params: { descricao, natureza: this.tipo() } })).catch(() => null);
      if (r?.categoriaId && !this.escolheuCategoria()) {
        this.form.categoriaId = r.categoriaId;
        this.sugerida.set(false); // desliga e liga: avisa o Angular para redesenhar mesmo se já estava "sugerida"
        this.sugerida.set(true);
      }
    }, 400);
  }

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const dados = { ...this.form, tipo: this.tipo(), documento: this.form.documento || null };
      const m = this.editando();
      const r = m
        ? await firstValueFrom(this.http.put<Movimento>(`${URL_FINANCEIRO}/movimentos/${m.id}`, dados))
        : await firstValueFrom(this.http.post<Movimento>(`${URL_FINANCEIRO}/movimentos`, dados));
      this.dlg().nativeElement.close();
      const valor = r.valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
      this.salvo.emit({
        id: r.id, editou: !!m,
        texto: m ? 'Lançamento atualizado.' : `${r.tipo === 'Entrada' ? 'Receita' : 'Despesa'} de ${valor} lançada em ${r.conta}.`,
      });
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}

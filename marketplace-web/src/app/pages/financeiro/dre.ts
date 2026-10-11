import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import { GraficoBarras, PontoBarra } from '../../shared/graficos/grafico-barras';
import { DetalheDre, Dre, LinhaDre, MesDre, URL_FINANCEIRO, hojeIso, mensagemDeErro } from '../../core/api/financeiro.api';
import { Icone } from '../../shared/icone';

const URL_DRE = `${URL_FINANCEIRO}/dre`;

// Aba "DRE": o resultado do mês por COMPETÊNCIA ("dei lucro?"). Abre no último mês FECHADO: no mês em andamento as
// despesas fixas já entram inteiras e a receita ainda não, e isso assusta sem explicar.
@Component({
  selector: 'app-dre',
  imports: [Icone, CurrencyPipe, DecimalPipe, FormsModule, DiaPipe, GraficoBarras],
  templateUrl: './dre.html',
  styleUrls: ['./financeiro.scss', './dre.scss'],
})
export class DreAba {
  private readonly http = inject(HttpClient);

  private readonly mesAtual = hojeIso().slice(0, 7);
  private readonly ultimoFechado = (() => {
    const [a, m] = this.mesAtual.split('-').map(Number);
    return m === 1 ? `${a - 1}-12` : `${a}-${String(m - 1).padStart(2, '0')}`;
  })();

  protected readonly meses = signal(3);
  protected readonly incluirAtual = signal(false);
  protected readonly ate = computed(() => (this.incluirAtual() ? this.mesAtual : this.ultimoFechado));
  protected readonly dre = httpResource<Dre>(() => ({ url: URL_DRE, params: { ate: this.ate(), meses: this.meses() } }));

  // Grupos abertos (mostram as categorias). Começam fechados: a DRE fica curta e legível.
  protected readonly abertos = signal<Set<string>>(new Set());

  protected readonly linhasVisiveis = computed(() => {
    const linhas = this.dre.value()?.linhas ?? [];
    let grupoAtual = '';
    return linhas.filter((l) => {
      if (l.nivel < 2) { grupoAtual = l.chave; return true; }
      return this.abertos().has(grupoAtual);
    });
  });
  protected readonly temCategorias = computed(() => {
    const linhas = this.dre.value()?.linhas ?? [];
    return new Set(linhas.filter((l, i) => l.nivel === 1 && linhas[i + 1]?.nivel === 2).map((l) => l.chave));
  });

  // "De cada R$ 100 vendidos": do último mês FECHADO que está na tela.
  protected readonly mesCem = computed(() => {
    const d = this.dre.value();
    if (!d) return null;
    for (let i = d.meses.length - 1; i >= 0; i--) if (!d.meses[i].parcial) return i;
    return null;
  });
  protected readonly cem = computed<{ mes: MesDre; barras: PontoBarra[]; lucro: number } | null>(() => {
    const d = this.dre.value(), i = this.mesCem();
    if (!d || i === null) return null;
    const v = (chave: string) => d.linhas.find((l) => l.chave === chave)?.valores[i] ?? 0;
    const receita = v('receita-bruta');
    if (receita <= 0) return null;
    const por100 = (x: number) => Math.round((Math.abs(x) / receita) * 10000) / 100;
    const partes: [string, number][] = [
      ['Mercadoria (custo)', v('cmv') + v('perdas')],
      ['Impostos', v('impostos') + v('descontos')],
      ['Pessoal', v('grupo:Pessoal')],
      ['Ocupação', v('grupo:Ocupacao')],
      ['Administrativas', v('grupo:Administrativas')],
      ['Vendas (taxas, sacolas…)', v('grupo:DespesasVendas')],
      ['Financeiras (líquido)', v('grupo:Financeiras') + v('grupo:OutrasReceitas')],
    ];
    const lucro = v('lucro-liquido');
    const barras: PontoBarra[] = partes.filter(([, x]) => x < 0 && por100(x) >= 0.05).map(([rotulo, x]) => ({ rotulo, valor: por100(x) }));
    if (lucro > 0) barras.push({ rotulo: 'Lucro', valor: por100(lucro), destaque: true });
    return { mes: d.meses[i], barras, lucro: lucro / receita * 100 };
  });

  // ----- Detalhe (clicou num valor) -----
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlgDetalhe');
  protected readonly detalhe = signal<DetalheDre | null>(null);
  protected readonly carregandoDetalhe = signal(false);
  protected readonly totalDetalhe = computed(() => (this.detalhe()?.itens ?? []).reduce((s, i) => s + i.valor, 0));

  // ----- Alíquota do Simples (editada aqui mesmo) -----
  protected readonly editandoAliquota = signal(false);
  protected novaAliquota: number | null = null;
  protected readonly retornoAliquota = signal<{ texto: string; ruim?: boolean } | null>(null);

  protected alternar(chave: string): void {
    const s = new Set(this.abertos());
    if (s.has(chave)) s.delete(chave); else s.add(chave);
    this.abertos.set(s);
  }

  protected abrirTudo(abrir: boolean): void {
    this.abertos.set(abrir ? new Set(this.temCategorias()) : new Set());
  }

  // % sobre a receita bruta do mês.
  protected pct(valor: number, i: number): number | null {
    const receita = this.dre.value()?.linhas.find((l) => l.chave === 'receita-bruta')?.valores[i] ?? 0;
    return receita > 0 ? (valor / receita) * 100 : null;
  }

  // Variação do último mês contra o anterior. "Melhor" = aumentou o resultado (receita subiu ou custo caiu).
  protected variacao(l: LinhaDre): { texto: string; melhor: boolean } | null {
    const n = l.valores.length;
    if (n < 2) return null;
    const [antes, agora] = [l.valores[n - 2], l.valores[n - 1]];
    if (antes === 0 || agora === antes) return null;
    const pct = ((Math.abs(agora) - Math.abs(antes)) / Math.abs(antes)) * 100;
    if (Math.abs(pct) < 0.5) return null;
    return { texto: `${pct > 0 ? '▲' : '▼'} ${Math.abs(pct).toFixed(0)}%`, melhor: agora > antes };
  }

  protected async detalhar(l: LinhaDre, m: MesDre): Promise<void> {
    if (!l.detalhavel) return;
    this.detalhe.set(null);
    this.carregandoDetalhe.set(true);
    this.dlg().nativeElement.showModal();
    try {
      this.detalhe.set(await firstValueFrom(this.http.get<DetalheDre>(`${URL_DRE}/detalhe`, { params: { mes: m.mes, linha: l.chave } })));
    } catch (e) {
      this.detalhe.set({ titulo: 'Não foi possível abrir', observacao: mensagemDeErro(e), itens: [] });
    } finally {
      this.carregandoDetalhe.set(false);
    }
  }

  protected editarAliquota(atual: number): void {
    this.novaAliquota = atual || null;
    this.retornoAliquota.set(null);
    this.editandoAliquota.set(true);
  }

  protected async salvarAliquota(): Promise<void> {
    try {
      await firstValueFrom(this.http.put(`${URL_DRE}/aliquota`, { valor: this.novaAliquota ?? 0 }));
      this.editandoAliquota.set(false);
      this.retornoAliquota.set({ texto: 'Salvo. A DRE foi recalculada.' });
      this.dre.reload();
    } catch (e) {
      this.retornoAliquota.set({ texto: mensagemDeErro(e), ruim: true });
    }
  }

  protected imprimir(): void {
    this.abrirTudo(true); // no papel, com as categorias
    setTimeout(() => window.print(), 50);
  }
}

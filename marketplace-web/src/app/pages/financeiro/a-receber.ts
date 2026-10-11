import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import { GraficoBarras, PontoBarra } from '../../shared/graficos/grafico-barras';
import {
  ICONE_FORMA, NOME_FORMA, Recebivel, ResumoRecebiveis, URL_FINANCEIRO, mensagemDeErro,
} from '../../core/api/financeiro.api';
import { DialogoMaquininha } from './dialogo-maquininha';
import { Icone } from '../../shared/icone';

const DIAS = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];

// Aba "A receber": o que a maquininha ainda vai depositar (débito D+1, crédito D+30) e quanto ela custa.
@Component({
  selector: 'app-a-receber',
  imports: [Icone, CurrencyPipe, DecimalPipe, DiaPipe, GraficoBarras, DialogoMaquininha],
  templateUrl: './a-receber.html',
  styleUrls: ['./financeiro.scss', './a-receber.scss'],
})
export class AReceber {
  private readonly http = inject(HttpClient);

  protected readonly nomeForma = NOME_FORMA;
  protected readonly iconeForma = ICONE_FORMA;

  protected readonly situacao = signal<'previstos' | 'recebidos'>('previstos');
  protected readonly resumo = httpResource<ResumoRecebiveis>(() => `${URL_FINANCEIRO}/recebiveis/resumo`);
  protected readonly lista = httpResource<Recebivel[]>(() => ({
    url: `${URL_FINANCEIRO}/recebiveis`, params: { situacao: this.situacao() },
  }));

  // Quanto a maquininha "come" das vendas eletrônicas do mês (a pergunta que o dono nunca sabe responder).
  protected readonly percentualTaxas = computed(() => {
    const r = this.resumo.value();
    return r && r.vendasEletronicasNoMes > 0 ? (r.taxasNoMes / r.vendasEletronicasNoMes) * 100 : 0;
  });

  // Próximos depósitos: uma barra por dia (até 10 dias com dinheiro a cair).
  protected readonly barras = computed<PontoBarra[]>(() =>
    (this.resumo.value()?.proximosDias ?? []).slice(0, 10).map((d) => {
      const [a, m, dd] = d.data.split('-').map(Number);
      const semana = DIAS[new Date(a, m - 1, dd).getDay()];
      return { rotulo: `${semana} ${String(dd).padStart(2, '0')}/${String(m).padStart(2, '0')}`, valor: d.liquido };
    }));

  protected readonly retorno = signal<{ texto: string; ruim?: boolean } | null>(null);
  protected readonly atualizando = signal(false);
  private readonly maquininha = viewChild.required(DialogoMaquininha);

  protected configurar(): void {
    this.retorno.set(null);
    this.maquininha().abrir();
  }

  protected configurou(texto: string): void {
    this.retorno.set({ texto });
  }

  // "Pôr em dia agora": o sistema já faz sozinho de hora em hora.
  protected async atualizar(): Promise<void> {
    this.atualizando.set(true);
    this.retorno.set(null);
    try {
      const r = await firstValueFrom(this.http.post<{ caixas: number; recebiveis: number }>(`${URL_FINANCEIRO}/recebiveis/atualizar`, {}));
      this.retorno.set({
        texto: r.caixas + r.recebiveis === 0 ? 'Tudo em dia: nada novo para lançar.'
          : `${r.caixas} caixa(s) lançado(s) e ${r.recebiveis} recebível(is) depositado(s).`,
      });
      this.resumo.reload();
      this.lista.reload();
    } catch (e) {
      this.retorno.set({ texto: mensagemDeErro(e), ruim: true });
    } finally {
      this.atualizando.set(false);
    }
  }
}

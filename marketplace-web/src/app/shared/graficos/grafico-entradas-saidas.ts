import { Component, computed, input, signal } from '@angular/core';
import { MARGEM_ESQ } from './grafico-colunas';
import { caminhoColuna, caminhoColunaAbaixo, escalaIntervalo, formatar, larguraDoComponente } from './largura';

export interface PontoEntradaSaida {
  rotulo: string;
  entradas: number;           // confirmadas (maquininha, ou realizado)
  entradasEstimadas?: number; // vendas estimadas: empilhadas em cima, verde mais claro
  saidas: number;
  detalhes?: string[];
}

// Entradas para CIMA (verde; a parte estimada em verde claro, empilhada) e saídas para BAIXO (violeta) da linha do zero.
// Um eixo só (R$). Duas séries → legenda sempre; a posição (em cima/embaixo) também diz quem é quem, não só a cor.
@Component({
  selector: 'app-grafico-entradas-saidas',
  template: `
    <div class="grafico" (mouseleave)="ativo.set(null)">
      <div class="legenda" aria-hidden="true">
        <span><i class="cor-entrada"></i>Entradas{{ temEstimado() ? ' confirmadas' : '' }}</span>
        @if (temEstimado()) { <span><i class="cor-estimada"></i>Vendas estimadas</span> }
        <span><i class="cor-saida"></i>Saídas</span>
      </div>
      <svg [attr.width]="largura()" [attr.height]="ALTURA" role="img" [attr.aria-label]="descricao()">
        @let g = geo();
        @for (t of g.marcas; track t.valor) {
          <line class="grade" [attr.x1]="g.esq" [attr.x2]="g.dir" [attr.y1]="t.y" [attr.y2]="t.y" />
          <text class="eixo" [attr.x]="g.esq - 8" [attr.y]="t.y" text-anchor="end" dominant-baseline="middle">{{ t.texto }}</text>
        }
        @for (c of g.colunas; track $index) {
          @if (ativo() === $index) { <rect class="faixa-ativa" [attr.x]="c.faixaX" [attr.y]="g.topo" [attr.width]="g.faixa" [attr.height]="g.base - g.topo" /> }
          <path class="coluna" [attr.d]="c.entrada" />
          <path class="coluna-estimada" [attr.d]="c.estimada" />
          <path class="coluna-saida" [attr.d]="c.saida" />
          @if (c.mostrarRotulo) {
            <text class="eixo" [attr.x]="c.centro" [attr.y]="g.base + 16" text-anchor="middle">{{ c.rotulo }}</text>
          }
          <rect class="alvo" [attr.x]="c.faixaX" [attr.y]="g.topo" [attr.width]="g.faixa" [attr.height]="g.base - g.topo"
                (mouseenter)="ativo.set($index)" />
        }
        <line class="ref-zero" [attr.x1]="g.esq" [attr.x2]="g.dir" [attr.y1]="g.yZero" [attr.y2]="g.yZero" />
      </svg>
      @if (ativo() !== null) {
        @let c = geo().colunas[ativo()!];
        <div class="dica-grafico" [style.left.px]="c.centro" [style.top.px]="geo().yZero">
          <strong>{{ c.rotulo }}</strong>
          <span>Entradas: {{ c.textoEntradas }}</span>
          @if (c.entradasEstimadas) { <small>· {{ c.textoEstimadas }} estimadas</small> }
          <span>Saídas: {{ c.textoSaidas }}</span>
          @for (d of c.detalhes; track $index) { <small>{{ d }}</small> }
        </div>
      }
    </div>
  `,
  styles: `
    .legenda { display: flex; flex-wrap: wrap; gap: 14px; margin: 0 0 6px ${MARGEM_ESQ}px; font-size: 12px; color: var(--cor-texto-suave); }
    .legenda span { display: inline-flex; align-items: center; gap: 6px; }
    .legenda i { width: 12px; height: 12px; border-radius: 3px; }
    .cor-entrada { background: var(--grafico-serie); }
    .cor-estimada { background: var(--grafico-estimado); }
    .cor-saida { background: var(--grafico-saidas); }
  `,
})
export class GraficoEntradasSaidas {
  readonly dados = input.required<PontoEntradaSaida[]>();
  readonly descricao = input('Entradas e saídas');

  protected readonly ALTURA = 200;
  protected readonly largura = larguraDoComponente();
  protected readonly ativo = signal<number | null>(null);
  protected readonly temEstimado = computed(() => this.dados().some((d) => (d.entradasEstimadas ?? 0) > 0));

  protected readonly geo = computed(() => {
    const dados = this.dados();
    const esq = MARGEM_ESQ, dir = Math.max(this.largura(), 280) - 8, topo = 8, base = this.ALTURA - 26;
    const maxCima = Math.max(1, ...dados.map((d) => d.entradas + (d.entradasEstimadas ?? 0)));
    const maxBaixo = Math.max(0, ...dados.map((d) => d.saidas));
    const ticks = escalaIntervalo(-maxBaixo, maxCima);
    const [tMin, tMax] = [ticks[0], ticks[ticks.length - 1]];
    const y = (v: number) => base - ((v - tMin) / (tMax - tMin || 1)) * (base - topo);
    const yZero = y(0);
    const faixa = (dir - esq) / Math.max(dados.length, 1);
    const largura = Math.max(2, Math.min(28, faixa - 2)); // 2px de respiro entre colunas vizinhas
    const cada = Math.max(1, Math.ceil(dados.length / Math.max(2, Math.floor((dir - esq) / 70))));

    const colunas = dados.map((d, i) => {
      const faixaX = esq + faixa * i;
      const x = faixaX + (faixa - largura) / 2;
      const est = d.entradasEstimadas ?? 0;
      const yEntrada = y(d.entradas);
      const yTopo = y(d.entradas + est);
      return {
        ...d, faixaX, centro: faixaX + faixa / 2, mostrarRotulo: i % cada === 0, detalhes: d.detalhes ?? [],
        // confirmadas embaixo (reta, colada no zero); estimadas em cima com 2px de respiro e a ponta arredondada
        entrada: est > 0 ? retangulo(x, yEntrada, largura, yZero) : caminhoColuna(x, yEntrada, largura, yZero),
        estimada: est > 0 ? caminhoColuna(x, yTopo, largura, d.entradas > 0 ? yEntrada - 2 : yZero) : '',
        saida: caminhoColunaAbaixo(x, yZero, largura, y(-d.saidas)),
        textoEntradas: formatar(d.entradas + est, 'moeda'), textoEstimadas: formatar(est, 'moeda'), textoSaidas: formatar(d.saidas, 'moeda'),
      };
    });
    return { esq, dir, topo, base, faixa, yZero, colunas, marcas: ticks.map((v) => ({ valor: v, y: y(v), texto: formatar(Math.abs(v), 'moedaCompacta') })) };
  });
}

function retangulo(x: number, y: number, largura: number, base: number): string {
  return base - y <= 0 ? '' : `M${x},${base} V${y} H${x + largura} V${base} Z`;
}

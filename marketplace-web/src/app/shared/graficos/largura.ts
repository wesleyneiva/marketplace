import { DestroyRef, ElementRef, afterNextRender, inject, signal } from '@angular/core';

// Mede a largura do componente e avisa quando ela muda (janela redimensionada, menu aberto...).
// Os gráficos usam isso para recalcular o desenho: SVG desenhado em pixels reais, nunca "esticado".
export function larguraDoComponente(inicial = 600) {
  const host = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
  const largura = signal(inicial);
  const atualizar = (w: number) => {
    w = Math.floor(w);
    if (w > 0 && w !== largura()) largura.set(w);
  };
  // 1) Mede logo depois de aparecer na tela (funciona até com a aba em segundo plano)…
  afterNextRender(() => atualizar(host.getBoundingClientRect().width));
  // 2) …e continua observando: a janela mudou de tamanho, o menu abriu → redesenha.
  const observador = new ResizeObserver(([entrada]) => atualizar(entrada.contentRect.width));
  observador.observe(host);
  inject(DestroyRef).onDestroy(() => observador.disconnect());
  return largura.asReadonly();
}

// Marcas "redondas" para o eixo: 0, 2.000, 4.000… (e não 0, 1.873, 3.746…).
export function escalaRedonda(maximo: number, marcas = 4): number[] {
  if (maximo <= 0) return [0];
  const bruto = maximo / marcas;
  const potencia = 10 ** Math.floor(Math.log10(bruto));
  const passo = [1, 2, 2.5, 5, 10].map((m) => m * potencia).find((p) => p >= bruto)!;
  const topo = Math.ceil(maximo / passo) * passo;
  return Array.from({ length: Math.round(topo / passo) + 1 }, (_, i) => +(i * passo).toFixed(6));
}

// Barra com o topo arredondado (4px) e a base reta, crescendo de baixo para cima.
export function caminhoColuna(x: number, y: number, largura: number, base: number): string {
  const altura = base - y;
  if (altura <= 0) return '';
  const r = Math.min(4, largura / 2, altura);
  return `M${x},${base} V${y + r} Q${x},${y} ${x + r},${y} H${x + largura - r} Q${x + largura},${y} ${x + largura},${y + r} V${base} Z`;
}

// Barra deitada: começa reta na esquerda e termina arredondada à direita.
export function caminhoBarra(x: number, y: number, comprimento: number, espessura: number): string {
  if (comprimento <= 0) return '';
  const r = Math.min(4, espessura / 2, comprimento);
  return `M${x},${y} H${x + comprimento - r} Q${x + comprimento},${y} ${x + comprimento},${y + r} V${y + espessura - r} Q${x + comprimento},${y + espessura} ${x + comprimento - r},${y + espessura} H${x} Z`;
}

const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });
const numero = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 });
const compacto = new Intl.NumberFormat('pt-BR', { notation: 'compact', maximumFractionDigits: 1 });

export type Formato = 'moeda' | 'numero' | 'compacto' | 'moedaCompacta';

export function formatar(valor: number, formato: Formato): string {
  switch (formato) {
    case 'moeda': return moeda.format(valor);
    case 'compacto': return compacto.format(valor);
    case 'moedaCompacta': return 'R$ ' + compacto.format(valor);
    default: return numero.format(valor);
  }
}

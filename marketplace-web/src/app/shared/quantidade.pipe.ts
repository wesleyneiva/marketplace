import { Pipe, PipeTransform, inject, LOCALE_ID } from '@angular/core';
import { formatNumber } from '@angular/common';

// Mostra a quantidade do jeito certo para a unidade:
//   {{ 12.35 | quantidade: 'KG' }} → "12,350 KG"     {{ 8 | quantidade: 'UN' }} → "8 UN"
// Uso com sinal (movimentações): {{ -3 | quantidade: 'UN' : true }} → "−3 UN"
@Pipe({ name: 'quantidade' })
export class QuantidadePipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(valor: number | null | undefined, unidade: string, comSinal = false): string {
    if (valor === null || valor === undefined) return '—';
    const fracionado = unidade === 'KG' || unidade === 'L';
    const texto = formatNumber(Math.abs(valor), this.locale, fracionado ? '1.3-3' : '1.0-0');
    const sinal = comSinal ? (valor > 0 ? '+' : valor < 0 ? '−' : '') : valor < 0 ? '−' : '';
    return `${sinal}${texto} ${unidade}`;
  }
}

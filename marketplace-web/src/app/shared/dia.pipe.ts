import { Pipe, PipeTransform } from '@angular/core';

// Datas "só dia" (como a validade "2026-10-20") viram "20/10/2026".
// Cuidado clássico: new Date("2026-10-20") é MEIA-NOITE EM UTC, que em Brasília ainda é dia 19!
// Por isso aqui só reorganizamos o texto, sem passar por Date.
@Pipe({ name: 'dia' })
export class DiaPipe implements PipeTransform {
  transform(valor: string | null | undefined): string {
    if (!valor) return '—';
    const [ano, mes, dia] = valor.split('-');
    return `${dia}/${mes}/${ano}`;
  }
}

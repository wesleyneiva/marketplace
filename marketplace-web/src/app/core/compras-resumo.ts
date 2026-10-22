import { Injectable, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { AuthService } from './auth/auth.service';

// Quantos produtos a sugestão de compra manda comprar agora: o número ao lado de "Compras" no menu.
// A tela de Compras chama recarregar() ao enviar um pedido, para o número baixar na hora.
@Injectable({ providedIn: 'root' })
export class ComprasResumo {
  private readonly auth = inject(AuthService);

  private readonly resumo = httpResource<{ itens: number }>(() => {
    const u = this.auth.usuario();
    return u && !u.soPlataforma && this.auth.temPerfil('Administrador', 'Gerente') && this.auth.liberado('compras') ? '/api/compras/sugestao/resumo' : undefined;
  });
  readonly paraComprar = computed(() => (this.resumo.hasValue() ? this.resumo.value().itens : 0));

  recarregar(): void {
    this.resumo.reload();
  }
}

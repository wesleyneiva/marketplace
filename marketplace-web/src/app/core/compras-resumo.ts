import { Injectable, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { AuthService } from './auth/auth.service';

// Números do menu: quantos produtos a sugestão de compra manda comprar ("Compras") e quantos estão sem estoque ou
// abaixo do mínimo ("Estoque").
// A tela de Compras chama recarregar() ao enviar um pedido, para o número baixar na hora.
@Injectable({ providedIn: 'root' })
export class ComprasResumo {
  private readonly auth = inject(AuthService);

  private readonly resumo = httpResource<{ itens: number }>(() => {
    const u = this.auth.usuario();
    return u && !u.soPlataforma && this.auth.temPerfil('Administrador', 'Gerente') && this.auth.liberado('compras') ? '/api/compras/sugestao/resumo' : undefined;
  });
  readonly paraComprar = computed(() => (this.resumo.hasValue() ? this.resumo.value().itens : 0));

  // Número ao lado de "Estoque": produtos sem estoque ou abaixo do mínimo.
  private readonly estoque = httpResource<{ produtos: number }>(() => {
    const u = this.auth.usuario();
    return u && !u.soPlataforma && this.auth.temPerfil('Administrador', 'Gerente') ? '/api/estoque/atencao' : undefined;
  });
  readonly estoqueAtencao = computed(() => (this.estoque.hasValue() ? this.estoque.value().produtos : 0));

  recarregar(): void {
    this.resumo.reload();
    this.estoque.reload();
  }
}

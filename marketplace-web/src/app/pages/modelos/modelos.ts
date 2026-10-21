import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Categoria, Pagina } from '../../core/api/produtos.api';
import { ModeloResumo } from '../../core/api/modelos.api';
import { Icone } from '../../shared/icone';
import { Paginacao } from '../../shared/paginacao';

// Produtos → Modelos com grade: um modelo por linha (as variações tamanho × cor ficam dentro dele).
@Component({
  selector: 'app-modelos',
  imports: [Icone, Paginacao, RouterLink, CurrencyPipe, DecimalPipe],
  templateUrl: './modelos.html',
  styleUrl: './modelos.scss',
})
export class Modelos {
  protected readonly auth = inject(AuthService);
  protected readonly busca = signal('');
  protected readonly categoriaId = signal<number | null>(null);
  protected readonly incluirInativos = signal(false);
  protected readonly pagina = signal(1);
  protected readonly tamanho = 15;

  protected readonly modelos = httpResource<Pagina<ModeloResumo>>(() => ({
    url: '/api/modelos',
    params: {
      busca: this.busca(),
      ...(this.categoriaId() ? { categoriaId: this.categoriaId()! } : {}),
      incluirInativos: this.incluirInativos(),
      pagina: this.pagina(),
      tamanho: this.tamanho,
    },
  }));
  protected readonly categorias = httpResource<Categoria[]>(() => '/api/categorias');
  protected readonly paginas = computed(() => Math.max(1, Math.ceil((this.modelos.value()?.total ?? 0) / this.tamanho)));

  private espera?: ReturnType<typeof setTimeout>;
  protected digitar(texto: string): void {
    clearTimeout(this.espera);
    this.espera = setTimeout(() => {
      this.busca.set(texto.trim());
      this.pagina.set(1);
    }, 300);
  }

  protected escolherCategoria(valor: string): void {
    this.categoriaId.set(valor ? +valor : null);
    this.pagina.set(1);
  }

  protected alternarInativos(): void {
    this.incluirInativos.update((v) => !v);
    this.pagina.set(1);
  }

  // "38–44 · 3 cores"
  protected resumoGrade(m: ModeloResumo): string {
    const t = m.tamanhos.length > 1 ? `${m.tamanhos[0]}–${m.tamanhos[m.tamanhos.length - 1]}` : (m.tamanhos[0] ?? '');
    const c = m.cores.length === 1 ? m.cores[0] : `${m.cores.length} cores`;
    return [t !== 'Único' ? t : '', c !== 'Única' ? c : ''].filter(Boolean).join(' · ');
  }
}

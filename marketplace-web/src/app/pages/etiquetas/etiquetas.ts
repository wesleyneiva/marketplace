import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { KeyValuePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Categoria, Produto } from '../../core/api/produtos.api';
import { CodigoBarras } from '../../shared/codigo-barras';
import { ProdutoBusca } from '../../shared/produto-busca';

interface Etiqueta {
  id: number;
  nome: string;
  codigoBarras: string | null;
  unidade: string;
  precoVenda: number;
  categoria: string;
  precoAlteradoEm: string | null;
}

interface Linha {
  etiqueta: Etiqueta;
  copias: number;
}

// Tamanhos das folhas de etiqueta A4 mais comuns (papelaria: "etiqueta adesiva A4").
const FORMATOS = {
  gondola: { nome: 'Gôndola · 3 × 8 por folha (70 × 37 mm)', colunas: 3, linhas: 8 },
  pequena: { nome: 'Pequena · 4 × 10 por folha (52 × 29 mm)', colunas: 4, linhas: 10 },
} as const;
type Formato = keyof typeof FORMATOS;

// Etiquetas de preço para a prateleira: escolher produtos (os que mudaram de preço, uma categoria, ou um a um),
// conferir a prévia e imprimir numa folha A4 de etiquetas adesivas.
@Component({
  selector: 'app-etiquetas',
  imports: [FormsModule, KeyValuePipe, RouterLink, CodigoBarras, ProdutoBusca],
  templateUrl: './etiquetas.html',
  styleUrl: './etiquetas.scss',
})
export class Etiquetas implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  // Atalhos vindos de outras telas: /produtos/etiquetas?desde=2026-10-09 ou ?ids=1,2,3
  readonly desde = input<string>();
  readonly ids = input<string>();

  protected readonly formatos = FORMATOS;
  protected readonly categorias = httpResource<Categoria[]>(() => '/api/categorias');
  protected readonly empresa = computed(() => this.auth.usuario()?.empresa.nome ?? '');
  protected readonly hoje = new Date().toLocaleDateString('sv-SE');
  protected readonly dataImpressao = new Date().toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: '2-digit' });

  protected dataAlteracao = this.hoje;
  protected categoriaId: number | null = null;
  protected readonly formatoAtual = signal<Formato>('gondola');
  protected readonly linhas = signal<Linha[]>([]);
  protected readonly carregando = signal(false);
  protected readonly aviso = signal<string | null>(null);

  // Cada cópia vira uma etiqueta; as etiquetas são distribuídas em folhas.
  protected readonly folhas = computed(() => {
    const f = FORMATOS[this.formatoAtual()];
    const porFolha = f.colunas * f.linhas;
    const todas = this.linhas().flatMap((l) => Array.from({ length: Math.max(0, Math.min(99, Math.floor(l.copias) || 0)) }, () => l.etiqueta));
    const folhas: Etiqueta[][] = [];
    for (let i = 0; i < todas.length; i += porFolha) folhas.push(todas.slice(i, i + porFolha));
    return folhas;
  });
  protected readonly totalEtiquetas = computed(() => this.folhas().reduce((s, f) => s + f.length, 0));

  ngOnInit(): void {
    const ids = this.ids();
    if (ids) this.carregar({ ids }, 'Produtos escolhidos');
    else if (this.desde()) {
      this.dataAlteracao = this.desde()!;
      this.carregar({ precoAlteradoDesde: this.dataAlteracao }, 'Preços alterados');
    }
  }

  adicionarAlterados(): void {
    if (this.dataAlteracao) this.carregar({ precoAlteradoDesde: this.dataAlteracao }, 'Preços alterados');
  }

  adicionarCategoria(): void {
    if (this.categoriaId) this.carregar({ categoriaId: this.categoriaId }, 'Categoria');
  }

  adicionarProduto(p: Produto): void {
    this.juntar([{ id: p.id, nome: p.nome, codigoBarras: p.codigoBarras, unidade: p.unidade, precoVenda: p.precoVenda, categoria: p.categoria, precoAlteradoEm: null }]);
  }

  remover(l: Linha): void {
    this.linhas.update((lista) => lista.filter((x) => x !== l));
  }

  limpar(): void {
    this.linhas.set([]);
    this.aviso.set(null);
  }

  mexeu(): void {
    this.linhas.update((lista) => [...lista]);
  }

  imprimir(): void {
    window.print();
  }

  private async carregar(filtro: Record<string, string | number>, origem: string): Promise<void> {
    this.carregando.set(true);
    try {
      const lista = await firstValueFrom(this.http.get<Etiqueta[]>('/api/produtos/etiquetas', { params: filtro }));
      const novas = this.juntar(lista);
      this.aviso.set(lista.length === 0 ? `${origem}: nenhum produto encontrado.` : `${origem}: ${novas} produto(s) adicionado(s)${novas < lista.length ? ` (${lista.length - novas} já estavam na lista)` : ''}.`);
    } finally {
      this.carregando.set(false);
    }
  }

  // Junta sem repetir produto (se já está na lista, fica como está).
  private juntar(etiquetas: Etiqueta[]): number {
    const ja = new Set(this.linhas().map((l) => l.etiqueta.id));
    const novas = etiquetas.filter((e) => !ja.has(e.id)).map((etiqueta) => ({ etiqueta, copias: 1 }));
    this.linhas.update((lista) => [...lista, ...novas]);
    return novas.length;
  }

  // 29.9 → { reais: "29", centavos: "90" }
  protected preco(valor: number): { reais: string; centavos: string } {
    const [reais, centavos] = valor.toFixed(2).split('.');
    return { reais: Number(reais).toLocaleString('pt-BR'), centavos };
  }

  protected sufixo(unidade: string): string {
    return { KG: 'o quilo', L: 'o litro', PCT: 'o pacote', CX: 'a caixa', DZ: 'a dúzia' }[unidade] ?? 'a unidade';
  }
}

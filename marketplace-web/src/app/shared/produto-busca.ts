import { Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Pagina, Produto } from '../core/api/produtos.api';
import { QuantidadePipe } from './quantidade.pipe';

// Campo "procure um produto": digita parte do nome (ou bipa o código de barras) e escolhe da lista.
// Se o texto for um código de barras que bate exatamente com um produto, já seleciona sozinho.
@Component({
  selector: 'app-produto-busca',
  imports: [CurrencyPipe, QuantidadePipe],
  template: `
    <div class="caixa" [class.flutuante]="flutuante()">
      <input #campo type="search" [placeholder]="placeholder()" autocomplete="off"
             (input)="digitar(campo.value)" (keydown.enter)="$event.preventDefault(); escolherPrimeiro()"
             (keydown.escape)="resultados.set([])" [attr.aria-label]="placeholder()" />
      @if (resultados().length) {
        <ul class="lista" role="listbox">
          @for (p of resultados(); track p.id) {
            <li role="option" (mousedown)="escolher(p)">
              <span class="nome">{{ p.nome }}</span>
              <small>{{ p.codigoBarras ?? 'sem código' }} · {{ p.precoVenda | currency: 'BRL' }} · estoque {{ p.estoqueAtual | quantidade: p.unidade }}</small>
            </li>
          }
        </ul>
      } @else if (semResultado()) {
        <div class="lista vazio">Nenhum produto encontrado.</div>
      }
    </div>
  `,
  styles: `
    .caixa { position: relative; }
    input { width: 100%; }
    .lista {
      margin: 4px 0 0; padding: 4px; list-style: none; max-height: 280px; overflow-y: auto;
      background: var(--cor-superficie); border: 1px solid var(--cor-borda); border-radius: 10px;
      box-shadow: var(--sombra);
    }
    .flutuante .lista { position: absolute; z-index: 30; left: 0; right: 0; top: 100%; }
    li { padding: 8px 10px; border-radius: 8px; cursor: pointer; display: flex; flex-direction: column; }
    li:hover { background: var(--cor-primaria-clara); }
    .nome { font-weight: 600; }
    small { color: var(--cor-texto-suave); font-size: 12px; }
    .vazio { padding: 12px; color: var(--cor-texto-suave); font-size: 14px; }
  `,
})
export class ProdutoBusca {
  private readonly http = inject(HttpClient);

  readonly placeholder = input('Nome ou código de barras do produto…');
  // true: a lista "flutua" por cima do resto da página. false: empurra o conteúdo (bom dentro de janelas).
  readonly flutuante = input(true);
  readonly selecionado = output<Produto>();

  protected readonly resultados = signal<Produto[]>([]);
  protected readonly semResultado = signal(false);
  private readonly campo = viewChild.required<ElementRef<HTMLInputElement>>('campo');
  private espera?: ReturnType<typeof setTimeout>;
  private ultimaBusca = 0;

  protected digitar(texto: string): void {
    clearTimeout(this.espera);
    const termo = texto.trim();
    if (termo.length < 2) {
      this.resultados.set([]);
      this.semResultado.set(false);
      return;
    }
    this.espera = setTimeout(() => this.buscar(termo), 250);
  }

  private async buscar(termo: string): Promise<void> {
    // Se o usuário digitar rápido, só a resposta da ÚLTIMA busca vale.
    const numero = ++this.ultimaBusca;
    const pagina = await firstValueFrom(
      this.http.get<Pagina<Produto>>('/api/produtos', { params: { busca: termo, tamanho: 8 } }),
    );
    if (numero !== this.ultimaBusca) return;

    // Leitor de código de barras: o código bate exatamente → escolhe direto.
    const exato = pagina.itens.find((p) => p.codigoBarras === termo);
    if (exato) {
      this.escolher(exato);
      return;
    }
    this.resultados.set(pagina.itens);
    this.semResultado.set(pagina.itens.length === 0);
  }

  protected escolherPrimeiro(): void {
    const primeiro = this.resultados()[0];
    if (primeiro) this.escolher(primeiro);
  }

  protected escolher(produto: Produto): void {
    this.selecionado.emit(produto);
    this.resultados.set([]);
    this.semResultado.set(false);
    this.campo().nativeElement.value = '';
  }

  focar(): void {
    this.campo().nativeElement.focus();
  }
}

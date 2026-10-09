import { Component, inject, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Cidade {
  nome: string;
  uf: string;
  estado: string;
  latitude: number;
  longitude: number;
  fuso: string; // ex.: America/Manaus
  populacao: number | null;
}

// Fusos do Brasil em português (para a pessoa entender o que muda).
export function nomeDoFuso(fuso: string): string {
  return ({
    'America/Sao_Paulo': 'Brasília', 'America/Bahia': 'Brasília', 'America/Fortaleza': 'Brasília', 'America/Recife': 'Brasília',
    'America/Belem': 'Brasília', 'America/Maceio': 'Brasília', 'America/Araguaina': 'Brasília', 'America/Santarem': 'Brasília',
    'America/Manaus': 'Amazonas (−1 h)', 'America/Cuiaba': 'Mato Grosso (−1 h)', 'America/Campo_Grande': 'Mato Grosso do Sul (−1 h)',
    'America/Porto_Velho': 'Rondônia (−1 h)', 'America/Boa_Vista': 'Roraima (−1 h)', 'America/Rio_Branco': 'Acre (−2 h)',
    'America/Eirunepe': 'Acre (−2 h)', 'America/Noronha': 'Fernando de Noronha (+1 h)',
  } as Record<string, string>)[fuso] ?? fuso;
}

// Campo "procure a cidade": digita e escolhe da lista (qualquer cidade do Brasil, com o fuso de cada uma).
@Component({
  selector: 'app-cidade-busca',
  template: `
    <div class="caixa">
      <input type="search" [placeholder]="'Digite a cidade (ex.: Manaus, Campinas, Natal)…'" autocomplete="off"
             (input)="digitar($any($event.target).value)" (keydown.escape)="resultados.set([])" aria-label="Cidade" />
      @if (buscando()) { <small class="dica">Procurando…</small> }
      @if (erro()) { <small class="erro">{{ erro() }}</small> }
      @if (resultados().length) {
        <ul class="lista" role="listbox">
          @for (c of resultados(); track $index) {
            <li role="option" (mousedown)="escolher(c)">
              <span class="nome">{{ c.nome }} / {{ c.uf }}</span>
              <small>{{ c.estado }} · horário de {{ fuso(c.fuso) }}{{ c.populacao ? ' · ' + c.populacao.toLocaleString('pt-BR') + ' hab.' : '' }}</small>
            </li>
          }
        </ul>
      } @else if (semResultado()) {
        <small class="dica">Nenhuma cidade encontrada com "{{ semResultado() }}".</small>
      }
    </div>
  `,
  styles: `
    .caixa { position: relative; }
    input { width: 100%; }
    .lista {
      position: absolute; z-index: 30; left: 0; right: 0; top: 100%;
      margin: 4px 0 0; padding: 4px; list-style: none; max-height: 300px; overflow-y: auto;
      background: var(--cor-superficie); border: 1px solid var(--cor-borda); border-radius: 10px; box-shadow: var(--sombra);
    }
    li { padding: 8px 10px; border-radius: 8px; cursor: pointer; display: flex; flex-direction: column; }
    li:hover { background: var(--cor-primaria-clara); }
    .nome { font-weight: 600; }
    small { color: var(--cor-texto-suave); font-size: 12px; font-weight: 400; }
    .dica { display: block; margin-top: 4px; }
    .erro { display: block; margin-top: 4px; color: var(--cor-erro); }
  `,
})
export class CidadeBusca {
  private readonly http = inject(HttpClient);
  readonly selecionada = output<Cidade>();

  protected readonly resultados = signal<Cidade[]>([]);
  protected readonly buscando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly semResultado = signal<string | null>(null);
  protected readonly fuso = nomeDoFuso;
  private espera?: ReturnType<typeof setTimeout>;
  private ultima = 0;

  protected digitar(texto: string): void {
    clearTimeout(this.espera);
    const termo = texto.trim();
    this.erro.set(null);
    if (termo.length < 2) {
      this.resultados.set([]);
      this.semResultado.set(null);
      return;
    }
    this.espera = setTimeout(() => void this.buscar(termo), 300);
  }

  private async buscar(termo: string): Promise<void> {
    const numero = ++this.ultima;
    this.buscando.set(true);
    try {
      const lista = await firstValueFrom(this.http.get<Cidade[]>('/api/empresa/configuracao/cidades', { params: { busca: termo } }));
      if (numero !== this.ultima) return;
      this.resultados.set(lista);
      this.semResultado.set(lista.length ? null : termo);
    } catch {
      if (numero === this.ultima) this.erro.set('A busca de cidades não respondeu. Tente de novo.');
    } finally {
      if (numero === this.ultima) this.buscando.set(false);
    }
  }

  protected escolher(c: Cidade): void {
    this.resultados.set([]);
    this.selecionada.emit(c);
  }
}

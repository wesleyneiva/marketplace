import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DiaPipe } from '../../shared/dia.pipe';
import { Icone } from '../../shared/icone';

// Igual a Contracts/ClienteDtos.cs (importação de clientes).
interface LinhaPreviaCliente {
  linha: number;
  acao: 'Novo' | 'Atualizar' | 'Erro';
  nome: string | null;
  apelido: string | null;
  telefone: string | null;
  cpf: string | null;
  email: string | null;
  nascimento: string | null;
  erros: string[];
  avisos: string[];
}

interface PreviaImportacaoClientes {
  arquivo: string;
  totalLinhas: number;
  novos: number;
  atualizados: number;
  comErro: number;
  comAviso: number;
  errosGerais: string[];
  avisosGerais: string[];
  linhas: LinhaPreviaCliente[];
}

interface ResultadoImportacaoClientes {
  novos: number;
  atualizados: number;
  ignorados: number;
}

type Filtro = 'todas' | 'erro' | 'aviso' | 'novo' | 'atualizar';

// Importar clientes por planilha (a lista do sistema antigo): 1) baixar o modelo, 2) enviar e ver a prévia, 3) confirmar.
// Mesmo jeito (e mesmo visual) da importação de produtos.
@Component({
  selector: 'app-importar-clientes',
  imports: [Icone, RouterLink, DiaPipe],
  templateUrl: './importar-clientes.html',
  styleUrl: '../importar-produtos/importar-produtos.scss',
})
export class ImportarClientes {
  private readonly http = inject(HttpClient);

  protected readonly urlModelo = '/api/clientes/importacao/modelo';
  protected readonly enviando = signal(false);
  protected readonly arrastando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly previa = signal<PreviaImportacaoClientes | null>(null);
  protected readonly filtro = signal<Filtro>('todas');
  protected readonly importando = signal(false);
  protected readonly resultado = signal<ResultadoImportacaoClientes | null>(null);
  private arquivo: File | null = null;
  protected readonly limiteNaTela = 500;

  protected readonly linhasFiltradas = computed(() => {
    const linhas = this.previa()?.linhas ?? [];
    const f = this.filtro();
    const escolhidas = linhas.filter((l) =>
      f === 'todas' ? true
        : f === 'erro' ? l.acao === 'Erro'
        : f === 'aviso' ? l.avisos.length > 0
        : f === 'novo' ? l.acao === 'Novo'
        : l.acao === 'Atualizar');
    return { total: escolhidas.length, visiveis: escolhidas.slice(0, this.limiteNaTela) };
  });

  protected readonly aproveitaveis = computed(() => {
    const p = this.previa();
    return p ? p.novos + p.atualizados : 0;
  });

  escolherArquivo(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    const arquivo = campo.files?.[0];
    campo.value = '';
    if (arquivo) this.enviar(arquivo);
  }

  soltarArquivo(evento: DragEvent): void {
    evento.preventDefault();
    this.arrastando.set(false);
    const arquivo = evento.dataTransfer?.files?.[0];
    if (arquivo) this.enviar(arquivo);
  }

  arrastarPorCima(evento: DragEvent): void {
    evento.preventDefault();
    this.arrastando.set(true);
  }

  async enviar(arquivo: File): Promise<void> {
    if (!/\.(xlsx|csv|txt|xls)$/i.test(arquivo.name)) { this.erro.set('Escolha uma planilha .xlsx (Excel) ou .csv.'); return; }
    if (arquivo.size > 5 * 1024 * 1024) { this.erro.set('Arquivo maior que 5 MB. Divida a planilha em partes menores.'); return; }
    this.enviando.set(true);
    this.erro.set(null);
    this.previa.set(null);
    this.resultado.set(null);
    try {
      const previa = await firstValueFrom(this.http.post<PreviaImportacaoClientes>('/api/clientes/importacao/previa', this.formulario(arquivo)));
      this.arquivo = arquivo;
      this.previa.set(previa);
      this.filtro.set(previa.comErro > 0 ? 'erro' : 'todas');
    } catch (e) {
      this.erro.set((e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível ler a planilha. Tente de novo.');
    } finally {
      this.enviando.set(false);
    }
  }

  async importar(): Promise<void> {
    const p = this.previa();
    if (!this.arquivo || !p) return;
    const total = this.aproveitaveis();
    const aviso = p.comErro === 1 ? '\n\nA linha com erro será ignorada.' : p.comErro > 1 ? `\n\nAs ${p.comErro} linhas com erro serão ignoradas.` : '';
    if (!confirm(`Importar ${total} ${total === 1 ? 'cliente' : 'clientes'} agora?${aviso}`)) return;
    this.importando.set(true);
    this.erro.set(null);
    try {
      this.resultado.set(await firstValueFrom(this.http.post<ResultadoImportacaoClientes>('/api/clientes/importacao', this.formulario(this.arquivo))));
      this.previa.set(null);
      this.arquivo = null;
    } catch (e) {
      this.erro.set((e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível importar. Nada foi gravado; tente de novo.');
    } finally {
      this.importando.set(false);
    }
  }

  recomecar(): void {
    this.previa.set(null);
    this.resultado.set(null);
    this.erro.set(null);
    this.arquivo = null;
  }

  protected classeDaLinha(l: LinhaPreviaCliente): string {
    return l.acao === 'Erro' ? 'erro' : l.avisos.length ? 'aviso' : '';
  }

  // (51) 99999-8888 e 123.456.789-09, como no resto das telas.
  protected telefone(t: string | null): string {
    if (!t) return '—';
    return t.length === 11 ? `(${t.slice(0, 2)}) ${t.slice(2, 7)}-${t.slice(7)}` : t.length === 10 ? `(${t.slice(0, 2)}) ${t.slice(2, 6)}-${t.slice(6)}` : t;
  }
  protected cpf(c: string | null): string {
    return c ? `${c.slice(0, 3)}.${c.slice(3, 6)}.${c.slice(6, 9)}-${c.slice(9)}` : '—';
  }

  private formulario(arquivo: File): FormData {
    const f = new FormData();
    f.append('arquivo', arquivo);
    return f;
  }
}

import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { LinhaPrevia, PreviaImportacao, ProdutosApi, ResultadoImportacao, URL_MODELO_IMPORTACAO } from '../../core/api/produtos.api';
import { DiaPipe } from '../../shared/dia.pipe';
import { QuantidadePipe } from '../../shared/quantidade.pipe';

type Filtro = 'todas' | 'erro' | 'aviso' | 'novo' | 'atualizar';

// Importar produtos por planilha: 1) baixar o modelo, 2) enviar e ver a prévia, 3) confirmar.
@Component({
  selector: 'app-importar-produtos',
  imports: [RouterLink, CurrencyPipe, DiaPipe, QuantidadePipe],
  templateUrl: './importar-produtos.html',
  styleUrl: './importar-produtos.scss',
})
export class ImportarProdutos {
  private readonly api = inject(ProdutosApi);

  protected readonly urlModelo = URL_MODELO_IMPORTACAO;
  protected readonly hoje = new Date().toLocaleDateString('sv-SE');
  protected readonly enviando = signal(false);
  protected readonly arrastando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly previa = signal<PreviaImportacao | null>(null);
  protected readonly filtro = signal<Filtro>('todas');
  protected readonly importando = signal(false);
  protected readonly resultado = signal<ResultadoImportacao | null>(null);

  // O arquivo da prévia: é ele que vai de novo ao servidor na hora de gravar.
  private arquivo: File | null = null;

  // Mostra no máximo 500 linhas de uma vez (uma planilha de 5.000 linhas deixaria a tela pesada).
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

  // Quantas linhas seriam gravadas (as com erro ficam de fora).
  protected readonly aproveitaveis = computed(() => {
    const p = this.previa();
    return p ? p.novos + p.atualizados : 0;
  });

  escolherArquivo(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    const arquivo = campo.files?.[0];
    campo.value = ''; // permite escolher o MESMO arquivo de novo depois de corrigir
    if (arquivo) this.enviar(arquivo);
  }

  soltarArquivo(evento: DragEvent): void {
    evento.preventDefault();
    this.arrastando.set(false);
    const arquivo = evento.dataTransfer?.files?.[0];
    if (arquivo) this.enviar(arquivo);
  }

  arrastarPorCima(evento: DragEvent): void {
    evento.preventDefault(); // sem isso o navegador abre o arquivo em vez de soltar aqui
    this.arrastando.set(true);
  }

  async enviar(arquivo: File): Promise<void> {
    if (!/\.(xlsx|csv|txt|xls)$/i.test(arquivo.name)) {
      this.erro.set('Escolha uma planilha .xlsx (Excel) ou .csv.');
      return;
    }
    if (arquivo.size > 5 * 1024 * 1024) {
      this.erro.set('Arquivo maior que 5 MB. Divida a planilha em partes menores.');
      return;
    }
    this.enviando.set(true);
    this.erro.set(null);
    this.previa.set(null);
    this.resultado.set(null);
    try {
      const previa = await this.api.previaImportacao(arquivo);
      this.arquivo = arquivo;
      this.previa.set(previa);
      this.filtro.set(previa.comErro > 0 ? 'erro' : 'todas');
    } catch (e) {
      const mensagem = e instanceof HttpErrorResponse ? e.error?.mensagem : null;
      this.erro.set(mensagem ?? 'Não foi possível ler a planilha. Tente de novo.');
    } finally {
      this.enviando.set(false);
    }
  }

  async importar(): Promise<void> {
    const p = this.previa();
    if (!this.arquivo || !p) return;
    const total = this.aproveitaveis();
    const aviso = p.comErro > 0 ? `\n\nAs ${p.comErro} linhas com erro serão ignoradas.` : '';
    if (!confirm(`Importar ${total} ${total === 1 ? 'produto' : 'produtos'} agora?${aviso}`)) return;

    this.importando.set(true);
    this.erro.set(null);
    try {
      this.resultado.set(await this.api.importar(this.arquivo));
      this.previa.set(null);
      this.arquivo = null;
    } catch (e) {
      const mensagem = e instanceof HttpErrorResponse ? e.error?.mensagem : null;
      this.erro.set(mensagem ?? 'Não foi possível importar. Nada foi gravado; tente de novo.');
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

  protected classeDaLinha(l: LinhaPrevia): string {
    return l.acao === 'Erro' ? 'erro' : l.avisos.length ? 'aviso' : '';
  }
}

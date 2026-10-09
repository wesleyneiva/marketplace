import { Component, computed, inject, signal } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Produto } from '../../core/api/produtos.api';
import { CodigoBarras } from '../../shared/codigo-barras';
import { ProdutoBusca } from '../../shared/produto-busca';
import { BalancaService } from '../../core/equipamentos/balanca.service';
import { ImpressoraService } from '../../core/equipamentos/impressora.service';
import { Cupom } from '../../core/api/pdv.api';

interface Configuracao {
  balancaDigitosCodigo: number;
  balancaEtiqueta: 'Preco' | 'Peso';
  exemploEtiqueta: string;
}

// Configurações da loja e dos equipamentos: balança (etiqueta e caixa) e impressora.
@Component({
  selector: 'app-configuracoes',
  imports: [FormsModule, CurrencyPipe, DecimalPipe, RouterLink, CodigoBarras, ProdutoBusca],
  templateUrl: './configuracoes.html',
  styleUrl: './configuracoes.scss',
})
export class Configuracoes {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly sanitizer = inject(DomSanitizer);
  protected readonly balanca = inject(BalancaService);
  protected readonly impressora = inject(ImpressoraService);

  protected readonly ehAdmin = computed(() => this.auth.temPerfil('Administrador'));
  protected readonly leituraTeste = signal<string | null>(null);

  // Cupom de exemplo para a prévia e para o teste de impressão.
  private readonly cupomExemplo: Cupom = {
    id: 1234, dataHora: new Date().toISOString(), numeroCaixa: 1, operador: 'Maria (exemplo)', status: 'Concluida',
    subtotal: 47.4, desconto: 2, total: 45.4, valorPago: 50, troco: 4.6, motivoCancelamento: null,
    itens: [
      { produtoId: 1, descricao: 'Arroz Branco Tipo 1 5kg', unidade: 'UN', quantidade: 1, precoUnitario: 29.9, total: 29.9 },
      { produtoId: 2, descricao: 'Banana Prata', unidade: 'KG', quantidade: 1.235, precoUnitario: 6.99, total: 8.63 },
      { produtoId: 3, descricao: 'Refrigerante Cola 2L', unidade: 'UN', quantidade: 2, precoUnitario: 4.43, total: 8.87 },
    ],
    pagamentos: [{ forma: 'Dinheiro', valor: 50 }],
  };
  protected readonly previaCupom = computed(() => {
    this.impressora.config(); // refaz quando a configuração muda
    return this.sanitizer.bypassSecurityTrustHtml(this.impressora.htmlCupom(this.cupomExemplo, this.auth.usuario()?.empresa.nome ?? 'Marketplace'));
  });
  protected readonly config = httpResource<Configuracao>(() => '/api/empresa/configuracao');
  protected readonly aviso = signal<string | null>(null);
  protected readonly erro = signal<string | null>(null);

  protected digitos = 4;
  protected tipo: 'Preco' | 'Peso' = 'Preco';
  private carregado = false;

  // ----- Etiqueta de teste (sem balança: o sistema desenha a etiqueta para bipar ou digitar no PDV) -----
  protected readonly produtoTeste = signal<Produto | null>(null);
  protected pesoTeste = 0.75;
  protected readonly etiquetaTeste = signal<string | null>(null);

  // Preenche o formulário quando a configuração chega (uma vez).
  protected formulario(c: Configuracao): boolean {
    if (!this.carregado) {
      this.digitos = c.balancaDigitosCodigo;
      this.tipo = c.balancaEtiqueta;
      this.carregado = true;
    }
    return true;
  }

  async salvar(): Promise<void> {
    this.erro.set(null);
    try {
      await firstValueFrom(this.http.put('/api/empresa/configuracao', { balancaDigitosCodigo: this.digitos, balancaEtiqueta: this.tipo }));
      this.aviso.set('Formato da etiqueta salvo.');
      this.config.reload();
      this.etiquetaTeste.set(null);
    } catch (e) {
      this.erro.set((e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível salvar.');
    }
  }

  escolherProdutoTeste(p: Produto): void {
    this.produtoTeste.set(p);
    this.etiquetaTeste.set(null);
    this.erro.set(null); // o aviso de "sem PLU" aparece logo abaixo do produto
  }

  // Mesmo formato que o servidor lê (Services/EtiquetaBalanca.cs → Montar).
  gerarEtiqueta(): void {
    const p = this.produtoTeste();
    const c = this.config.value();
    if (!p?.codigoBalanca || !c || !(this.pesoTeste > 0)) return;
    const bruto = c.balancaEtiqueta === 'Peso' ? Math.round(this.pesoTeste * 1000) : Math.round(this.pesoTeste * p.precoVenda * 100);
    const corpo = c.balancaDigitosCodigo === 5
      ? `2${String(p.codigoBalanca).padStart(5, '0')}${String(bruto).padStart(6, '0')}`
      : `2${String(p.codigoBalanca).padStart(4, '0')}0${String(bruto).padStart(6, '0')}`;
    let soma = 0;
    for (let i = 0; i < 12; i++) soma += Number(corpo[i]) * (i % 2 === 0 ? 1 : 3);
    this.etiquetaTeste.set(corpo + ((10 - (soma % 10)) % 10));
  }

  protected valorTeste(): number {
    const p = this.produtoTeste();
    return p ? Math.round(this.pesoTeste * p.precoVenda * 100) / 100 : 0;
  }

  async testarBalanca(): Promise<void> {
    this.leituraTeste.set('Lendo…');
    const r = await this.balanca.lerPeso();
    this.leituraTeste.set(
      r.tipo === 'peso' ? `${r.kg.toLocaleString('pt-BR', { minimumFractionDigits: 3 })} kg`
      : r.tipo === 'instavel' ? 'Peso instável (mexendo)'
      : r.tipo === 'incompleto' ? 'A balança não respondeu: confira o cabo, a porta e a velocidade.'
      : r.tipo === 'negativo' ? 'Negativo: zere a balança' : r.tipo === 'sobrecarga' ? 'Acima do limite' : `Resposta estranha: ${r.recebido}`);
  }

  imprimirTeste(): void {
    this.impressora.imprimirCupom(this.cupomExemplo, this.auth.usuario()?.empresa.nome ?? 'Marketplace');
  }

  async copiar(texto: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(texto);
      this.aviso.set('Código copiado: cole no campo do PDV e aperte Enter.');
    } catch {
      /* sem permissão de área de transferência: o número está na tela */
    }
  }
}

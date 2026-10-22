import { MARCA } from '../../shared/marca';
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
import { copiarTexto } from '../../shared/copiar';
import { Cidade, CidadeBusca, nomeDoFuso } from '../../shared/cidade-busca';
import { BalancaService } from '../../core/equipamentos/balanca.service';
import { ImpressoraService } from '../../core/equipamentos/impressora.service';
import { Cupom } from '../../core/api/pdv.api';
import { Icone } from '../../shared/icone';
import { PixQrCodeComponent, chavePixLegivel } from '../../shared/pix-qrcode';
import { DiaPipe } from '../../shared/dia.pipe';
import { HorarioEditor } from '../../shared/horario-editor/horario-editor';
import { Horario, horarioParaServidor, horarioParaTela } from '../../shared/horario-editor/horario';

interface Configuracao {
  balancaDigitosCodigo: number;
  balancaEtiqueta: 'Preco' | 'Peso';
  exemploEtiqueta: string;
  cidade: string | null;
  uf: string | null;
  fuso: string;
  horaLocal: string;
  horario: Horario;
  horarioResumo: string;
  pixTipoChave: TipoChavePix | null;
  pixChave: string | null;
  pixNomeRecebedor: string | null;
  pixAutomatico: string | null;
  mercadoPagoConta: string | null;
  mercadoPagoTokenFinal: string | null;
  mercadoPagoTeste: boolean;
  pixAvisoUrl: string | null;
  proximosFeriados: { data: string; nome: string; situacao: string }[];
}

type TipoChavePix = 'Cpf' | 'Cnpj' | 'Celular' | 'Email' | 'Aleatoria';
const TIPOS_CHAVE: { valor: TipoChavePix; rotulo: string; exemplo: string }[] = [
  { valor: 'Cnpj', rotulo: 'CNPJ', exemplo: '12.345.678/0001-90' },
  { valor: 'Cpf', rotulo: 'CPF', exemplo: '123.456.789-09' },
  { valor: 'Celular', rotulo: 'Celular', exemplo: '(51) 99999-9999' },
  { valor: 'Email', rotulo: 'E-mail', exemplo: 'pix@suaempresa.com.br' },
  { valor: 'Aleatoria', rotulo: 'Chave aleatória', exemplo: '123e4567-e89b-12d3-a456-426614174000' },
];

// Configurações da loja e dos equipamentos: balança (etiqueta e caixa) e impressora.
@Component({
  selector: 'app-configuracoes',
  imports: [Icone, FormsModule, CurrencyPipe, DiaPipe, DecimalPipe, HorarioEditor, RouterLink, CodigoBarras, ProdutoBusca, CidadeBusca, PixQrCodeComponent],
  templateUrl: './configuracoes.html',
  styleUrl: './configuracoes.scss',
})
export class Configuracoes {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly ramo = this.auth.ramo; // palavras e o que aparece conforme o ramo (core/ramos.ts)

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
    return this.sanitizer.bypassSecurityTrustHtml(this.impressora.htmlCupom(this.cupomExemplo, this.auth.usuario()?.empresa.nome ?? MARCA.nome));
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
  protected readonly copiado = signal<boolean | null>(null); // null = ainda não tentou

  // ----- Cidade da loja (clima + fuso horário) -----
  protected readonly nomeDoFuso = nomeDoFuso;
  protected readonly cidadeEscolhida = signal<Cidade | null>(null);
  protected readonly salvandoCidade = signal(false);
  protected readonly avisoCidade = signal<string | null>(null);

  async salvarCidade(): Promise<void> {
    const c = this.cidadeEscolhida();
    if (!c) return;
    this.salvandoCidade.set(true);
    this.avisoCidade.set(null);
    try {
      await firstValueFrom(this.http.put('/api/empresa/configuracao/cidade', { nome: c.nome, uf: c.uf, latitude: c.latitude, longitude: c.longitude, fuso: c.fuso }));
      this.cidadeEscolhida.set(null);
      this.config.reload();
      this.avisoCidade.set(`Cidade salva: ${c.nome}/${c.uf}. Clima e horário da loja já seguem a cidade nova.`);
    } catch (e) {
      this.avisoCidade.set((e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível salvar a cidade.');
    } finally {
      this.salvandoCidade.set(false);
    }
  }

  // ----- Pix da loja (QR Code com o valor da venda no caixa) -----
  protected readonly tiposChave = TIPOS_CHAVE;
  protected pixTipo: TipoChavePix = 'Cnpj';
  protected pixChave = '';
  protected pixNome = '';
  protected readonly editandoPix = signal(false);
  protected readonly salvandoPix = signal(false);
  protected readonly avisoPix = signal<{ ok: boolean; texto: string } | null>(null);
  protected exemploChave(): string { return TIPOS_CHAVE.find((t) => t.valor === this.pixTipo)?.exemplo ?? ''; }
  protected readonly chaveLegivel = chavePixLegivel;
  protected rotuloTipo(t: string | null): string { return TIPOS_CHAVE.find((x) => x.valor === t)?.rotulo ?? ''; }

  protected editarPix(c: Configuracao): void {
    this.pixTipo = c.pixTipoChave ?? 'Cnpj';
    this.pixChave = c.pixChave ?? '';
    this.pixNome = c.pixNomeRecebedor ?? '';
    this.avisoPix.set(null);
    this.editandoPix.set(true);
  }

  async salvarPix(tirar = false): Promise<void> {
    if (tirar && !confirm('Tirar a chave Pix da loja? O caixa deixa de mostrar o QR Code.')) return;
    this.avisoPix.set(null);
    this.salvandoPix.set(true);
    try {
      const corpo = tirar ? { tipo: null, chave: '', nomeRecebedor: null } : { tipo: this.pixTipo, chave: this.pixChave, nomeRecebedor: this.pixNome };
      this.config.set(await firstValueFrom(this.http.put<Configuracao>('/api/empresa/configuracao/pix', corpo)));
      this.editandoPix.set(false);
      this.avisoPix.set({ ok: true, texto: tirar ? 'Chave Pix retirada.' : 'Chave Pix salva. Teste lendo o QR Code abaixo com o app do banco (não precisa pagar).' });
    } catch (e) {
      this.avisoPix.set({ ok: false, texto: (e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível salvar a chave Pix.' });
    } finally {
      this.salvandoPix.set(false);
    }
  }

  // ----- Pix automático (Mercado Pago avisa o caixa) -----
  protected mpToken = '';
  protected mpTeste = true;
  protected readonly salvandoMp = signal(false);
  protected readonly avisoMp = signal<{ ok: boolean; texto: string } | null>(null);

  async ligarMercadoPago(): Promise<void> {
    this.avisoMp.set(null);
    this.salvandoMp.set(true);
    try {
      const r = await firstValueFrom(this.http.put<{ conta: string; teste: boolean }>('/api/pix/mercado-pago', { token: this.mpToken, teste: this.mpTeste }));
      this.mpToken = '';
      this.config.reload();
      this.avisoMp.set({ ok: true, texto: `Ligado na conta ${r.conta}${r.teste ? ' (modo teste: o Pix é aprovado sozinho)' : ''}. No caixa, o Pix agora gera o QR do Mercado Pago.` });
    } catch (e) {
      this.avisoMp.set({ ok: false, texto: (e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível ligar o Pix automático.' });
    } finally {
      this.salvandoMp.set(false);
    }
  }

  protected readonly avisoCopiado = signal(false);
  async copiarAviso(url: string): Promise<void> { this.avisoCopiado.set(await copiarTexto(url)); }

  async desligarMercadoPago(): Promise<void> {
    if (!confirm('Desligar o Pix automático? O caixa volta ao Pix manual (QR da chave da loja) e o token do Mercado Pago é apagado.')) return;
    this.avisoMp.set(null);
    try {
      await firstValueFrom(this.http.delete('/api/pix/mercado-pago'));
      this.config.reload();
      this.avisoMp.set({ ok: true, texto: 'Pix automático desligado.' });
    } catch (e) {
      this.avisoMp.set({ ok: false, texto: (e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível desligar.' });
    }
  }

  // ----- Horário de funcionamento (os campos ficam no <app-horario-editor>) -----
  protected horario: Horario | null = null;
  protected readonly salvandoHorario = signal(false);
  protected readonly avisoHorario = signal<{ ok: boolean; texto: string } | null>(null);

  async salvarHorario(): Promise<void> {
    const h = this.horario;
    if (!h) return;
    this.salvandoHorario.set(true);
    this.avisoHorario.set(null);
    try {
      const corpo = horarioParaServidor(h);
      // A resposta já é a configuração gravada: vira o valor da tela (um reload() aqui ainda devolveria o valor antigo
      // enquanto carrega, e o formulário seria preenchido de novo com o horário de antes).
      const gravada = await firstValueFrom(this.http.put<Configuracao>('/api/empresa/configuracao/horario', corpo));
      this.config.set(gravada);
      this.horario = horarioParaTela(gravada.horario);
      this.avisoHorario.set({ ok: true, texto: 'Horário salvo. Relatórios, compras e insights já seguem o horário novo.' });
    } catch (e) {
      this.avisoHorario.set({ ok: false, texto: (e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível salvar o horário.' });
    } finally {
      this.salvandoHorario.set(false);
    }
  }

  private carregadoHorario = false;

  // Preenche o formulário quando a configuração chega (uma vez).
  protected formulario(c: Configuracao): boolean {
    if (!this.carregado) {
      this.digitos = c.balancaDigitosCodigo;
      this.tipo = c.balancaEtiqueta;
      this.carregado = true;
    }
    if (!this.carregadoHorario) {
      this.horario = horarioParaTela(c.horario);
      this.carregadoHorario = true;
    }
    return true;
  }

  // "2026-11-02" → "seg" (montando a data no fuso local: new Date('2026-11-02') seria o dia 1º às 21h em Brasília).
  protected diaDaSemana(data: string): string {
    const [ano, mes, dia] = data.split('-').map(Number);
    return ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'][new Date(ano, mes - 1, dia).getDay()];
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
    this.copiado.set(null);
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
    this.impressora.imprimirCupom(this.cupomExemplo, this.auth.usuario()?.empresa.nome ?? MARCA.nome);
  }

  async copiar(texto: string): Promise<void> {
    this.copiado.set(await copiarTexto(texto));
  }
}

import { MARCA } from '../../shared/marca';
import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DomSanitizer } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Pagina, Produto } from '../../core/api/produtos.api';
import { ModelosApi } from '../../core/api/modelos.api';
import { Ordem, URL_ORDENS } from '../../core/api/ordens.api';
import {
  CaixaDisponivel, ClientePdv, Cupom, FORMAS_PAGAMENTO, FORMAS_RECEBER, FormaPagamento, PdvApi, ReciboFiado, ResumoCaixa, VendaLista,
  arredondar, lerNumero,
} from '../../core/api/pdv.api';
import { QuantidadePipe } from '../../shared/quantidade.pipe';
import { Logo } from '../../shared/logo';
import { BalancaService } from '../../core/equipamentos/balanca.service';
import { ImpressoraService } from '../../core/equipamentos/impressora.service';
import { Icone } from '../../shared/icone';
import { PixQrCodeComponent } from '../../shared/pix-qrcode';
import { CobrancaPix, PixAutomatico } from '../../shared/pix-automatico';
import { AutorizacaoGerente } from '../../shared/autorizacao-gerente';
import { EstornoVenda } from '../../shared/estorno-venda';
import { CupomDetalhe } from '../../shared/cupom-detalhe';
import { Conexao } from '../../core/offline/conexao';
import { CaixaOffline, novoIdLocal } from '../../core/offline/caixa-offline.service';
import { Troca, TrocasApi } from '../../core/api/trocas.api';
import { TrocaCaixa } from '../../shared/troca-caixa';

interface ItemCarrinho {
  produto: Produto;
  quantidade: number;
}

// Tela do caixa (PDV). Feita para TECLADO: o campo de busca fica sempre focado.
//   • Bipa o código de barras (ou digita parte do nome) e Enter.
//   • "3*arroz" ou "3*789..." → 3 unidades.  Produto por KG pede o peso.
//   • F2 → pagamento.  Esc → fecha listas/janelas.  F6 → receber fiado (cliente pagando a caderneta).  F7 → troca.
//   • Tirar item, cancelar a compra e cancelar uma venda já paga (estorno): só com a senha de um gerente.
//   • SEM INTERNET (caixa offline): continua vendendo com o catálogo guardado neste computador; as vendas ficam numa
//     fila e são enviadas sozinhas quando a internet volta (ver core/offline). Abrir/fechar caixa, sangria e estorno esperam.
@Component({
  selector: 'app-pdv',
  imports: [Icone, FormsModule, CurrencyPipe, DatePipe, QuantidadePipe, Logo, PixQrCodeComponent, PixAutomatico, AutorizacaoGerente,
    EstornoVenda, CupomDetalhe, TrocaCaixa],
  templateUrl: './pdv.html',
  styleUrl: './pdv.scss',
  host: { '(document:keydown)': 'teclaGlobal($event)' },
})
export class Pdv {
  private readonly api = inject(PdvApi);
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly nomeLoja = computed(() => this.auth.usuario()?.empresa.nome ?? '');
  protected readonly ramo = this.auth.ramo; // exemplos e palavras conforme o ramo (core/ramos.ts)
  protected readonly balanca = inject(BalancaService);
  protected readonly impressora = inject(ImpressoraService);
  protected readonly conexao = inject(Conexao);
  protected readonly offline = inject(CaixaOffline);
  private readonly modelosApi = inject(ModelosApi);
  private readonly router = inject(Router);
  protected readonly semInternet = computed(() => !this.conexao.online());
  // O QR fixo do Pix (SVG gerado pelo nosso servidor e guardado no caixa) para cobrar Pix sem internet.
  private readonly sanitizer = inject(DomSanitizer);
  protected readonly svgPixFixo = computed(() => {
    const qr = this.offline.pixFixo();
    return qr ? this.sanitizer.bypassSecurityTrustHtml(qr.svg) : null;
  });

  // Fiado: só com o módulo ligado na plataforma (core/modulos.ts) e liberado para este cliente.
  protected readonly comFiado = this.auth.liberado('fiado');
  protected readonly formas = this.comFiado ? FORMAS_PAGAMENTO : FORMAS_PAGAMENTO.filter((f) => f.valor !== 'Fiado');
  protected readonly ehGerencia = computed(() => this.auth.temPerfil('Administrador', 'Gerente'));

  // ----- Caixa (sessão) -----
  // undefined = carregando · null = sem caixa aberto · objeto = caixa aberto
  protected readonly caixa = signal<ResumoCaixa | null | undefined>(undefined);
  protected numeroCaixa = 1;
  protected readonly caixas = signal<CaixaDisponivel[]>([]); // os caixas da empresa (limite do plano)
  protected trocoInicial = '100,00';

  // ----- Venda em andamento -----
  protected readonly carrinho = signal<ItemCarrinho[]>([]);
  protected readonly ultimoId = signal<number | null>(null); // destaca o último item bipado
  protected readonly desconto = signal(0);
  protected textoDesconto = '';

  protected readonly subtotal = computed(() =>
    arredondar(this.carrinho().reduce((s, i) => s + arredondar(i.quantidade * i.produto.precoVenda), 0)),
  );
  protected readonly total = computed(() => arredondar(Math.max(0, this.subtotal() - this.desconto())));
  protected readonly quantidadeItens = computed(() => this.carrinho().length);
  protected readonly descontoAcimaDoLimite = computed(
    () => !this.ehGerencia() && this.desconto() > arredondar(this.subtotal() * 0.1),
  );

  // ----- Busca -----
  protected textoBusca = '';
  protected readonly resultados = signal<Produto[]>([]);
  protected readonly indiceResultado = signal(0);
  private quantidadeDaBusca: number | null = null;
  protected readonly buscando = signal(false);

  // ----- Peso (produto por KG/L) -----
  protected readonly produtoPeso = signal<Produto | null>(null);
  // Oficina → "Cobrar" (/pdv?os=5): o serviço e a corda da ordem entram no carrinho; ao concluir, a OS fica paga.
  readonly os = input<string>();
  protected readonly osEmCobranca = signal<{ id: number; numero: number } | null>(null);
  private osCarregada: string | null = null;
  // Grade (tênis, roupa): buscou pelo NOME um modelo → escolhe o tamanho e a cor. Pelo código de barras vai direto.
  protected readonly grade = signal<{ modelo: string; tamanhos: string[]; cores: string[]; itens: Produto[]; quantidade: number } | null>(null);
  protected textoPeso = '';
  protected readonly lendoBalanca = signal(false);

  // ----- Pagamento -----
  protected readonly pagamentos = signal<{ forma: FormaPagamento; valor: number; cobrancaPixId?: number; valeTrocaId?: number; valeCodigo?: string }[]>([]);
  // Troca (F7) com "escolher o produto novo": o crédito do vale entra sozinho no pagamento desta compra.
  protected readonly creditoTroca = signal<{ valeId: number; codigo: string; saldo: number } | null>(null);
  private readonly trocasApi = inject(TrocasApi);
  private readonly janelaTroca = viewChild.required(TrocaCaixa);
  protected textoVale = '';
  protected readonly buscandoVale = signal(false);
  protected readonly formaAtual = signal<FormaPagamento>('Dinheiro');
  // O valor digitado no pagamento (sinal: o QR Code do Pix acompanha o valor enquanto o caixa digita).
  private readonly valorDigitado = signal('');
  protected get textoValor(): string { return this.valorDigitado(); }
  protected set textoValor(v: string) { this.valorDigitado.set(v ?? ''); }
  protected readonly valorPix = computed(() => (this.formaAtual() === 'Pix' ? arredondar(lerNumero(this.valorDigitado())) : 0));
  // Pix automático (Mercado Pago) ligado nas Configurações? E os Pix já pagos que ainda não entraram em venda.
  protected readonly pixAutomatico = signal<string | null>(null);
  protected readonly pixSoltos = signal<CobrancaPix[]>([]);
  // Pix que entraram nesta venda pelo aviso do Mercado Pago (a janela mostra a confirmação em destaque).
  protected readonly pixConfirmados = computed(() => {
    const ids = new Set(this.pagamentos().filter((p) => p.cobrancaPixId).map((p) => p.cobrancaPixId));
    return this.pixRecebidos().filter((c) => ids.has(c.id));
  });
  private readonly pixRecebidos = signal<CobrancaPix[]>([]);
  // Modal grande "Pix recebido!" (o caixa vê de longe) e a situação da venda logo abaixo.
  protected readonly pixOk = signal<{ cobranca: CobrancaPix; situacao: 'registrando' | 'registrada' | 'falta' | 'erro'; falta: number } | null>(null);
  private fecharPixOk?: ReturnType<typeof setTimeout>;
  protected readonly pago = computed(() => arredondar(this.pagamentos().reduce((s, p) => s + p.valor, 0)));
  protected readonly falta = computed(() => arredondar(Math.max(0, this.total() - this.pago())));
  protected readonly troco = computed(() => arredondar(Math.max(0, this.pago() - this.total())));
  protected readonly finalizando = signal(false);

  // ----- Fiado (cliente da caderneta) -----
  protected readonly clienteFiado = signal<ClientePdv | null>(null);
  protected textoCliente = '';
  protected readonly clientesAchados = signal<ClientePdv[]>([]);
  protected readonly buscandoCliente = signal(false);
  private esperaCliente?: ReturnType<typeof setTimeout>;
  // Cliente com restrição (sem limite, acima do limite, bloqueado, atrasado): o operador de caixa não libera.
  protected readonly fiadoTravado = computed(() => {
    const c = this.clienteFiado();
    if (!c || this.ehGerencia()) return null;
    if (c.restricao) return c.restricao;
    const valor = this.formaAtual() === 'Fiado' ? arredondar(lerNumero(this.textoValor)) || this.falta() : 0;
    return c.saldo + valor > c.limiteCredito ? `Passa do limite: disponível ${dinheiroBr(c.disponivel)}.` : null;
  });

  // ----- Receber fiado no balcão (F6; o F4 é da balança) -----
  protected readonly formasReceber = FORMAS_RECEBER;
  protected readonly clienteReceber = signal<ClientePdv | null>(null);
  protected textoReceber = '';
  protected textoRecebido = ''; // dinheiro que o cliente entregou (para o troco)
  protected readonly formaReceber = signal<FormaPagamento>('Dinheiro');
  protected readonly recibo = signal<ReciboFiado | null>(null);
  private reciboRecebido: number | undefined;

  // ----- Resultado / mensagens -----
  protected readonly cupom = signal<Cupom | null>(null);
  protected readonly ultimoCupom = signal<Cupom | null>(null); // para reimprimir depois que a janela fechou
  protected readonly erro = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);

  // ----- Cancelar item / compra (antes de pagar): só com autorização do gerente -----
  protected readonly cancelando = signal<{ tipo: 'Item' | 'Compra'; item?: ItemCarrinho } | null>(null);
  protected textoQtdCancelar = '';
  protected readonly motivoCancelamento = signal('');
  protected readonly erroCancelamento = signal<string | null>(null);
  protected readonly enviandoCancelamento = signal(false);

  // ----- Vendas deste caixa (reimprimir e cancelar venda paga com estorno) -----
  protected readonly vendasCaixa = signal<VendaLista[] | null>(null);
  protected readonly vendaAberta = signal<Cupom | null>(null);
  protected readonly estornando = signal(false);
  protected readonly avisoVenda = signal<string | null>(null);

  // ----- Sangria / suprimento / fechamento -----
  protected readonly tipoMovimento = signal<'sangria' | 'suprimento'>('sangria');
  protected textoMovimento = '';
  protected motivoMovimento = '';
  protected imprimirComprovante = true; // papel assinado que vai junto com o dinheiro
  protected textoContado = '';
  protected observacaoFechamento = '';
  protected readonly fechamento = signal<ResumoCaixa | null>(null);

  private readonly campoBusca = viewChild<ElementRef<HTMLInputElement>>('campoBusca');
  private readonly campoPeso = viewChild<ElementRef<HTMLInputElement>>('campoPeso');
  private readonly campoValor = viewChild<ElementRef<HTMLInputElement>>('campoValor');
  private readonly btnConfirmar = viewChild<ElementRef<HTMLButtonElement>>('btnConfirmar');
  private readonly btnNovaVenda = viewChild<ElementRef<HTMLButtonElement>>('btnNovaVenda');
  private readonly dlgPagamento = viewChild<ElementRef<HTMLDialogElement>>('dlgPagamento');
  private readonly dlgCupom = viewChild<ElementRef<HTMLDialogElement>>('dlgCupom');
  private readonly dlgPixOk = viewChild<ElementRef<HTMLDialogElement>>('dlgPixOk');
  private readonly dlgMovimento = viewChild<ElementRef<HTMLDialogElement>>('dlgMovimento');
  private readonly dlgFechar = viewChild<ElementRef<HTMLDialogElement>>('dlgFechar');
  private readonly dlgReceber = viewChild<ElementRef<HTMLDialogElement>>('dlgReceber');
  private readonly dlgCancelar = viewChild<ElementRef<HTMLDialogElement>>('dlgCancelar');
  private readonly dlgVendas = viewChild<ElementRef<HTMLDialogElement>>('dlgVendas');
  private readonly autorizacaoPdv = viewChild<AutorizacaoGerente>('autorizacaoPdv');
  private readonly campoCliente = viewChild<ElementRef<HTMLInputElement>>('campoCliente');
  private readonly campoVale = viewChild<ElementRef<HTMLInputElement>>('campoVale');
  private readonly campoReceber = viewChild<ElementRef<HTMLInputElement>>('campoReceber');

  constructor() {
    void this.carregarCaixa();
    effect(() => {
      this.os();
      this.caixa();
      untracked(() => this.verificarOrdem());
    });
    // Venda em andamento guardada neste computador: ir ao Dashboard e voltar, apertar F5 ou o navegador fechar
    // não apaga o que já foi bipado. Ao abrir o caixa, ela volta; a cada item/desconto, é guardada de novo.
    effect(() => {
      const sessao = this.caixa()?.sessaoId;
      if (sessao && this.vendaRestaurada !== sessao) untracked(() => this.restaurarVenda(sessao));
    });
    effect(() => {
      const itens = this.carrinho(), desconto = this.desconto(), sessao = this.caixa()?.sessaoId;
      if (sessao && this.vendaRestaurada === sessao) untracked(() => this.guardarVenda(sessao, itens, desconto));
    });
    // A fila de vendas offline diminuiu (foram enviadas) com internet: atualiza os números do caixa.
    let antes = 0;
    effect(() => {
      const agora = this.offline.vendasPendentes().length;
      if (agora < antes && this.conexao.online()) void this.atualizarResumo();
      antes = agora;
    });
    // Sem caixa aberto (ao entrar ou depois de fechar): busca a lista de caixas e sugere o primeiro livre.
    effect(() => {
      if (this.caixa() === null) void this.carregarCaixas();
    });
  }

  private async carregarCaixas(): Promise<void> {
    try {
      const lista = await this.api.caixas();
      this.caixas.set(lista);
      const atual = lista.find((c) => c.numero === this.numeroCaixa);
      if (!atual || atual.ocupadoPor) this.numeroCaixa = lista.find((c) => !c.ocupadoPor)?.numero ?? 1;
    } catch {
      /* fica a lista vazia; o servidor confere o número na abertura */
    }
  }

  // ================================================================ caixa

  private async carregarCaixa(): Promise<void> {
    const usuario = this.auth.usuario();
    try {
      const r = await this.api.caixaAtual();
      this.caixa.set(r);
      if (usuario) await this.offline.iniciar(usuario.id, usuario.empresa.id);
      void this.offline.guardarResumo(r);
      this.focarBusca();
    } catch (e) {
      // Sem internet: abre com o caixa que estava aberto neste computador (se havia um).
      const guardado = this.conexao.falhou(e) && usuario ? await this.offline.lerResumo(usuario.id) : null;
      this.caixa.set(guardado);
      if (guardado && usuario) {
        await this.offline.iniciar(usuario.id, usuario.empresa.id);
        this.focarBusca();
      } else {
        this.erro.set(this.semInternet() ? 'Sem internet e nenhum caixa aberto neste computador: abra o caixa quando a internet voltar.' : 'Não foi possível consultar o caixa.');
      }
    }
  }

  // Números do caixa (vendas, total): com internet, pergunta ao servidor e guarda para abrir offline.
  private async atualizarResumo(): Promise<void> {
    try {
      const r = await this.api.caixaAtual();
      this.caixa.set(r);
      void this.offline.guardarResumo(r);
    } catch (e) {
      this.conexao.falhou(e);
    }
  }

  // Buscar produtos: no servidor; sem internet (ou se ela cair agora), no catálogo guardado neste computador.
  private async procurar(termo: string): Promise<Produto[]> {
    if (!this.conexao.online() && this.offline.temCatalogo()) return this.offline.buscar(termo);
    try {
      return (await firstValueFrom(this.http.get<Pagina<Produto>>('/api/produtos', { params: { busca: termo, tamanho: 8, agruparGrade: true } }))).itens;
    } catch (e) {
      if (this.conexao.falhou(e) && this.offline.temCatalogo()) return this.offline.buscar(termo);
      throw e;
    }
  }

  protected async abrirCaixa(): Promise<void> {
    const valor = lerNumero(this.trocoInicial);
    if (Number.isNaN(valor) || valor < 0) {
      this.erro.set('Troco inicial inválido.');
      return;
    }
    await this.executar(async () => {
      this.caixa.set(await this.api.abrir(this.numeroCaixa, valor));
      const usuario = this.auth.usuario();
      if (usuario) await this.offline.iniciar(usuario.id, usuario.empresa.id);
      void this.offline.guardarResumo(this.caixa() ?? null);
      this.mostrarAviso(`Caixa ${this.numeroCaixa} aberto. Boas vendas!`);
      this.focarBusca();
    });
  }

  // ================================================================ busca e itens

  protected async buscar(): Promise<void> {
    this.erro.set(null);
    this.avisoBalanca = null;

    clearTimeout(this.esperaDigitacao);
    const texto = this.textoBusca.trim();
    const { quantidade, termo } = separarQuantidade(texto);

    // Lista aberta (filtrada enquanto digitava) → Enter escolhe o item destacado.
    // Exceto se o texto é só números (leitor de código de barras): aí vale o código exato.
    if (this.resultados().length && this.termoDaLista === termo && !/^\d+$/.test(termo)) {
      this.quantidadeDaBusca = quantidade;
      this.escolher(this.resultados()[this.indiceResultado()]);
      return;
    }
    this.resultados.set([]);
    if (!texto) return;
    this.quantidadeDaBusca = quantidade;

    this.buscando.set(true);
    try {
      // Etiqueta da balança (açougue/hortifrúti): 13 dígitos começando com 2 → o servidor diz o produto e o peso.
      if (/^2\d{12}$/.test(termo) && this.conexao.online() && (await this.lerEtiquetaBalanca(termo))) return;

      const itens = await this.procurar(termo);
      const exato = itens.find((p) => p.codigoBarras === termo);
      if (exato || itens.length === 1) {
        this.escolher(exato ?? itens[0], !!exato);
      } else if (itens.length === 0) {
        this.erro.set(this.avisoBalanca ?? (/^2\d{12}$/.test(termo) && this.semInternet()
          ? 'Sem internet a etiqueta da balança não é lida: busque o produto pelo nome e digite o peso.'
          : `Produto não encontrado: "${termo}"`));
        this.textoBusca = '';
      } else {
        this.resultados.set(agruparGrade(itens));
        this.indiceResultado.set(0);
      }
    } finally {
      this.buscando.set(false);
    }
  }

  // true = era etiqueta de balança (lançou o item ou mostrou o erro). false = não é: segue a busca normal
  // (pode ser um produto com código interno começando com 2).
  private avisoBalanca: string | null = null;

  private async lerEtiquetaBalanca(codigo: string): Promise<boolean> {
    this.avisoBalanca = null;
    try {
      const r = await firstValueFrom(this.http.get<{ produto: Produto; quantidade: number; precoEtiqueta: number | null }>(
        `/api/produtos/balanca/etiqueta/${codigo}`));
      this.textoBusca = '';
      this.quantidadeDaBusca = null;
      this.adicionar(r.produto, r.quantidade);
      if (r.precoEtiqueta !== null) {
        const calculado = arredondar(r.quantidade * r.produto.precoVenda);
        if (calculado !== r.precoEtiqueta)
          this.mostrarAviso(`Etiqueta R$ ${r.precoEtiqueta.toFixed(2)} → ${r.quantidade} ${r.produto.unidade} (no caixa R$ ${calculado.toFixed(2)}: arredondamento do peso).`);
      }
      return true;
    } catch (e) {
      if (this.conexao.falhou(e)) return false; // sem internet: segue a busca no catálogo guardado
      if (e instanceof HttpErrorResponse && e.status === 404) {
        // PLU que não existe: pode ser um produto com código de barras "2…" de verdade → busca normal
        // (se ela também não achar, o caixa vê a mensagem da balança).
        this.avisoBalanca = e.error?.mensagem ?? null;
        return false;
      }
      this.erro.set((e instanceof HttpErrorResponse ? e.error?.mensagem : null) ?? 'Não foi possível ler a etiqueta da balança.');
      this.textoBusca = '';
      return true;
    }
  }

  // Filtra enquanto digita (espera 200 ms sem teclar). Só números = código de barras: não filtra, espera o Enter
  // (o leitor "digita" o código inteiro em milissegundos e aperta Enter sozinho).
  private esperaDigitacao?: ReturnType<typeof setTimeout>;
  private termoDaLista = '';
  protected readonly semResultado = signal<string | null>(null);

  protected digitar(): void {
    clearTimeout(this.esperaDigitacao);
    this.erro.set(null);
    const { termo } = separarQuantidade(this.textoBusca.trim());
    if (termo.length < 2 || /^\d+$/.test(termo)) {
      this.resultados.set([]);
      this.semResultado.set(null);
      return;
    }
    this.esperaDigitacao = setTimeout(() => void this.filtrar(termo), 200);
  }

  private async filtrar(termo: string): Promise<void> {
    try {
      const itens = await this.procurar(termo);
      // Se a pessoa continuou digitando (ou já apertou Enter), esta resposta ficou velha.
      if (separarQuantidade(this.textoBusca.trim()).termo !== termo) return;
      this.termoDaLista = termo;
      this.resultados.set(agruparGrade(itens));
      this.indiceResultado.set(0);
      this.semResultado.set(itens.length ? null : termo);
    } catch {
      /* falhou o filtro: o Enter ainda busca normalmente */
    }
  }

  protected moverSelecao(passo: number, evento: Event): void {
    if (!this.resultados().length) return;
    evento.preventDefault();
    const n = this.resultados().length;
    this.indiceResultado.update((i) => (i + passo + n) % n);
  }

  // Clique num produto da lista: vale a quantidade digitada antes do "*" (ex.: "3*arroz" → 3 unidades), como no Enter.
  protected escolherDaLista(produto: Produto): void {
    this.quantidadeDaBusca = separarQuantidade(this.textoBusca.trim()).quantidade;
    this.escolher(produto);
  }

  protected escolher(produto: Produto, peloCodigo = false): void {
    this.resultados.set([]);
    this.semResultado.set(null);
    clearTimeout(this.esperaDigitacao);
    this.textoBusca = '';
    const quantidade = this.quantidadeDaBusca;
    this.quantidadeDaBusca = null;

    if (produto.modeloId && !peloCodigo) {
      void this.abrirGrade(produto, quantidade ?? 1);
      return;
    }

    // Produto por peso/volume sem quantidade informada → pergunta o peso.
    if (quantidade === null && this.fracionado(produto)) {
      this.produtoPeso.set(produto);
      this.textoPeso = '';
      setTimeout(() => this.campoPeso()?.nativeElement.focus());
      if (this.balanca.ativa()) void this.lerBalanca(); // balança no caixa: o peso já vem
      return;
    }
    this.adicionar(produto, quantidade ?? 1);
  }

  // As variações do modelo: do servidor; sem internet, do catálogo guardado neste computador.
  private async abrirGrade(produto: Produto, quantidade: number): Promise<void> {
    const id = produto.modeloId!;
    let nome = produto.modelo ?? produto.nome;
    let itens: Produto[];
    let tamanhos: string[];
    let cores: string[];
    try {
      if (!this.conexao.online()) throw new Error('offline');
      const m = await this.modelosApi.obter(id);
      nome = m.nome;
      tamanhos = m.tamanhos;
      cores = m.cores;
      itens = m.variacoes.filter((v) => v.ativo).map((v) => ({
        ...produto, id: v.produtoId, nome: v.nome, codigoBarras: v.codigoBarras, precoVenda: v.precoVenda, estoqueAtual: v.estoqueAtual,
        tamanho: v.tamanho, cor: v.cor,
      }));
    } catch (e) {
      if (e instanceof HttpErrorResponse) this.conexao.falhou(e);
      itens = this.offline.variacoes(id);
      if (!itens.length) { this.adicionar(produto, quantidade); return; }
      tamanhos = [...new Set(itens.map((p) => p.tamanho ?? ''))];
      cores = [...new Set(itens.map((p) => p.cor ?? ''))];
    }
    this.grade.set({ modelo: nome, tamanhos, cores, itens, quantidade });
    // Foco no primeiro tamanho com estoque: setas/Tab andam entre eles, Enter escolhe e Esc cancela.
    setTimeout(() => (document.querySelector<HTMLElement>('.grade-pdv .opcao:not(.sem)') ?? document.querySelector<HTMLElement>('.grade-pdv .opcao'))?.focus());
  }

  protected variacaoDaGrade(t: string, c: string): Produto | undefined {
    return this.grade()?.itens.find((p) => p.tamanho === t && p.cor === c);
  }

  protected escolherVariacao(p: Produto): void {
    const q = this.grade()?.quantidade ?? 1;
    this.grade.set(null);
    this.adicionar(p, q);
  }

  protected cancelarGrade(): void {
    this.grade.set(null);
    this.focarBusca();
  }

  protected confirmarPeso(): void {
    const produto = this.produtoPeso();
    const peso = lerNumero(this.textoPeso);
    if (!produto) return;
    if (Number.isNaN(peso) || peso <= 0) {
      this.erro.set('Informe o peso (ex.: 1,235).');
      return;
    }
    this.produtoPeso.set(null);
    this.adicionar(produto, peso);
  }

  // Pede o peso à balança do caixa e preenche o campo (a pessoa confere e aperta Enter).
  protected async lerBalanca(): Promise<void> {
    if (this.lendoBalanca()) return;
    this.lendoBalanca.set(true);
    this.erro.set(null);
    try {
      const r = await this.balanca.lerPeso();
      if (!this.produtoPeso()) return;
      if (r.tipo === 'peso' && r.kg > 0) this.textoPeso = r.kg.toLocaleString('pt-BR', { minimumFractionDigits: 3, maximumFractionDigits: 3 });
      else if (r.tipo === 'peso') this.erro.set('Balança vazia: coloque o produto e leia de novo (F4).');
      else if (r.tipo === 'instavel' || r.tipo === 'incompleto') this.erro.set('O peso não estabilizou (ou a balança não respondeu). Tente de novo (F4).');
      else if (r.tipo === 'negativo') this.erro.set('Peso negativo: zere (tare) a balança.');
      else if (r.tipo === 'sobrecarga') this.erro.set('Acima do limite da balança.');
      else this.erro.set(`Resposta estranha da balança: ${r.recebido}`);
    } finally {
      this.lendoBalanca.set(false);
      setTimeout(() => this.campoPeso()?.nativeElement.select());
    }
  }

  protected cancelarPeso(): void {
    this.produtoPeso.set(null);
    this.focarBusca();
  }

  private adicionar(produto: Produto, quantidade: number): void {
    if (!this.fracionado(produto) && quantidade % 1 !== 0) {
      this.erro.set(`"${produto.nome}" é vendido por ${produto.unidade}: use número inteiro.`);
      this.focarBusca();
      return;
    }

    // Mesmo produto de novo → soma na linha existente.
    this.carrinho.update((itens) => {
      const existente = itens.find((i) => i.produto.id === produto.id);
      if (existente) {
        return itens.map((i) =>
          i === existente ? { ...i, quantidade: Math.round((i.quantidade + quantidade) * 1000) / 1000 } : i,
        );
      }
      return [...itens, { produto, quantidade }];
    });

    const naLista = this.carrinho().find((i) => i.produto.id === produto.id)!;
    if (!produto.servico && naLista.quantidade > produto.estoqueAtual) {
      this.erro.set(`Atenção: "${produto.nome}" tem só ${produto.estoqueAtual} ${produto.unidade} no estoque.`);
    }
    this.ultimoId.set(produto.id);
    this.focarBusca();
  }

  // ================================================================ cancelar item / compra (com o gerente)

  protected remover(item: ItemCarrinho): void {
    this.abrirCancelamento('Item', item);
  }

  protected cancelarCompra(): void {
    if (this.carrinho().length) this.abrirCancelamento('Compra');
  }

  private abrirCancelamento(tipo: 'Item' | 'Compra', item?: ItemCarrinho): void {
    this.cancelando.set({ tipo, item });
    this.textoQtdCancelar = item ? String(item.quantidade).replace('.', ',') : '';
    this.motivoCancelamento.set('');
    this.erroCancelamento.set(null);
    this.dlgCancelar()?.nativeElement.showModal();
    setTimeout(() => this.autorizacaoPdv()?.focar());
  }

  // Quanto sai: produto por unidade pode sair só uma parte (ex.: 2 das 5 latas); por peso, o item inteiro.
  protected qtdCancelar(item: ItemCarrinho): number {
    if (this.fracionado(item.produto) || item.quantidade <= 1) return item.quantidade;
    return lerNumero(this.textoQtdCancelar);
  }

  protected async confirmarCancelamento(): Promise<void> {
    const c = this.cancelando(), aut = this.autorizacaoPdv();
    if (!c || !aut || this.enviandoCancelamento()) return;
    const qtd = c.item ? this.qtdCancelar(c.item) : 0;
    if (c.item && (Number.isNaN(qtd) || qtd <= 0 || qtd > c.item.quantidade || qtd % 1 !== 0 && !this.fracionado(c.item.produto))) {
      this.erroCancelamento.set(`Quantidade a cancelar: de 1 a ${c.item.quantidade}.`);
      return;
    }
    const falta = aut.falta();
    if (falta) { this.erroCancelamento.set(falta); return; }

    const itens = c.item
      ? [{ produtoId: c.item.produto.id, quantidade: qtd }]
      : this.carrinho().map((i) => ({ produtoId: i.produto.id, quantidade: i.quantidade }));
    this.enviandoCancelamento.set(true);
    this.erroCancelamento.set(null);
    try {
      let r: { autorizadoPor: string; valor: number };
      try {
        if (this.semInternet()) throw new TypeError('sem internet');
        r = await this.api.cancelarNoPdv(c.tipo, itens, this.motivoCancelamento().trim(), aut.autorizacao());
      } catch (e) {
        if (!this.conexao.falhou(e) && !this.semInternet()) throw e;
        // Sem internet: guarda para enviar depois, em nome do gerente escolhido (a senha não pôde ser conferida).
        await this.offline.guardarCancelamento(c.tipo, itens, this.motivoCancelamento().trim(), aut.gerenteEscolhido());
        const gerente = this.offline.gerentes().find((g) => g.id === aut.gerenteEscolhido())?.nome ?? this.auth.usuario()?.nome ?? 'gerente';
        const valor = arredondar(itens.reduce((soma, i) => soma + i.quantidade * (this.carrinho().find((x) => x.produto.id === i.produtoId)?.produto.precoVenda ?? 0), 0));
        r = { autorizadoPor: `${gerente} (sem internet: senha não conferida)`, valor };
      }
      if (c.item) {
        const item = c.item;
        this.carrinho.update((lista) => qtd >= item.quantidade
          ? lista.filter((i) => i !== item)
          : lista.map((i) => (i === item ? { ...i, quantidade: Math.round((i.quantidade - qtd) * 1000) / 1000 } : i)));
      } else {
        this.limparVenda(false);
      }
      this.dlgCancelar()?.nativeElement.close();
      this.mostrarAviso(`${c.item ? 'Item cancelado' : 'Compra cancelada'} (${dinheiroBr(r.valor)}) · autorizado por ${r.autorizadoPor}.`);
    } catch (e) {
      this.erroCancelamento.set(this.mensagem(e));
      if (e instanceof HttpErrorResponse && e.error?.autorizacao) aut.senhaErrada();
    } finally {
      this.enviandoCancelamento.set(false);
    }
  }

  protected aoFecharCancelamento(): void {
    this.cancelando.set(null);
    this.focarBusca();
  }

  // ================================================================ vendas deste caixa (reimprimir / estorno)

  protected abrirVendasCaixa(): void {
    if (this.precisaInternet('ver as vendas do caixa')) return;
    this.vendasCaixa.set(null);
    this.vendaAberta.set(null);
    this.estornando.set(false);
    this.avisoVenda.set(null);
    this.erro.set(null);
    this.dlgVendas()?.nativeElement.showModal();
    void this.carregarVendasCaixa();
  }

  private async carregarVendasCaixa(): Promise<void> {
    const sessao = this.caixa()?.sessaoId;
    if (!sessao) return;
    try {
      this.vendasCaixa.set((await this.api.vendas({ sessaoId: sessao, tamanho: 50 })).itens);
    } catch (e) {
      this.vendasCaixa.set([]);
      this.erro.set(this.mensagem(e));
    }
  }

  protected async verVenda(v: VendaLista): Promise<void> {
    this.avisoVenda.set(null);
    this.estornando.set(false);
    try {
      this.vendaAberta.set(await this.api.venda(v.id));
    } catch (e) {
      this.erro.set(this.mensagem(e));
    }
  }

  protected voltarParaVendas(): void {
    this.vendaAberta.set(null);
    this.estornando.set(false);
    this.avisoVenda.set(null);
  }

  protected reimprimirVenda(): void {
    const c = this.vendaAberta();
    if (c) this.impressora.imprimirCupom(c, this.loja());
  }

  // Cancelou e estornou: o comprovante (cupom "VENDA CANCELADA" com a assinatura do cliente) e o caixa atualizado.
  protected async vendaCancelada(c: Cupom): Promise<void> {
    this.vendaAberta.set(c);
    this.estornando.set(false);
    const dinheiro = c.pagamentos.filter((p) => p.forma === 'Dinheiro').reduce((s, p) => s + p.valor, 0) - c.troco;
    this.avisoVenda.set(`Venda #${c.id} cancelada. ${dinheiro > 0 ? `Devolva ${dinheiroBr(dinheiro)} em dinheiro ao cliente.` : 'Estorno registrado.'}`);
    if (this.ultimoCupom()?.id === c.id) this.ultimoCupom.set(c);
    if (this.impressora.config().automatica) this.impressora.imprimirCupom(c, this.loja());
    void this.carregarVendasCaixa();
    this.caixa.set(await this.api.caixaAtual());
  }

  protected aoFecharVendas(): void {
    this.vendaAberta.set(null);
    this.estornando.set(false);
    this.erro.set(null);
    this.focarBusca();
  }

  protected aplicarDesconto(): void {
    const valor = this.textoDesconto.trim() ? lerNumero(this.textoDesconto) : 0;
    this.desconto.set(Number.isNaN(valor) || valor < 0 ? 0 : arredondar(valor));
    this.focarBusca();
  }

  protected totalItem(item: ItemCarrinho): number {
    return arredondar(item.quantidade * item.produto.precoVenda);
  }

  // ================================================================ pagamento

  protected abrirPagamento(): void {
    if (!this.carrinho().length) {
      this.erro.set('Nenhum item na compra.');
      return;
    }
    if (this.descontoAcimaDoLimite()) {
      this.erro.set('Desconto acima de 10% precisa de um gerente.');
      return;
    }
    this.erro.set(null);
    this.pagamentos.set([]);
    this.limparCliente();
    const credito = this.creditoTroca();
    if (credito && credito.saldo > 0)
      this.pagamentos.set([{ forma: 'ValeTroca', valor: Math.min(credito.saldo, this.total()), valeTrocaId: credito.valeId, valeCodigo: credito.codigo }]);
    this.escolherForma('Dinheiro');
    this.dlgPagamento()?.nativeElement.showModal();
    if (this.falta() === 0) setTimeout(() => this.btnConfirmar()?.nativeElement.focus());
    void this.carregarPix();
  }

  // ================================================================ troca (F7)

  protected abrirTroca(): void {
    if (this.precisaInternet('fazer troca')) return;
    this.erro.set(null);
    this.janelaTroca().abrir();
  }

  protected aoUsarTroca(t: Troca): void {
    this.creditoTroca.set({ valeId: t.vale.id, codigo: t.vale.codigo, saldo: t.vale.saldo });
    this.mostrarAviso(`Troca registrada: crédito de ${dinheiroBr(t.vale.saldo)}. Bipe o produto novo; o crédito entra no pagamento.`);
  }

  protected tirarCreditoTroca(): void {
    const c = this.creditoTroca();
    this.creditoTroca.set(null);
    if (c) this.mostrarAviso(`O crédito continua no vale ${c.codigo}: o cliente usa depois com o código.`);
    this.focarBusca();
  }

  // Vale-troca no pagamento: digita o código, o sistema confere saldo e validade e usa até o que falta.
  protected async usarVale(): Promise<void> {
    const codigo = this.textoVale.trim();
    if (!codigo) { this.erro.set('Digite o código do vale-troca (VT-…).'); return; }
    this.buscandoVale.set(true);
    this.erro.set(null);
    try {
      const v = await this.trocasApi.vale(codigo);
      if (v.situacao === 'Usado') throw new Error(`O vale ${v.codigo} já foi todo usado.`);
      if (v.situacao === 'Vencido') throw new Error(`O vale ${v.codigo} venceu em ${v.validade.split('-').reverse().join('/')}.`);
      if (this.pagamentos().some((p) => p.valeTrocaId === v.id)) throw new Error(`O vale ${v.codigo} já está nesta compra.`);
      this.pagamentos.update((p) => [...p, { forma: 'ValeTroca', valor: Math.min(v.saldo, this.falta()), valeTrocaId: v.id, valeCodigo: v.codigo }]);
      this.textoVale = '';
      if (this.falta() > 0) this.escolherForma('Dinheiro');
      else setTimeout(() => this.btnConfirmar()?.nativeElement.focus());
    } catch (e) {
      this.erro.set(e instanceof Error && !(e instanceof HttpErrorResponse) ? e.message : this.mensagem(e));
    } finally {
      this.buscandoVale.set(false);
    }
  }

  // Lê (a cada pagamento) se o Pix automático está ligado e se há Pix pagos sem venda neste caixa.
  private async carregarPix(): Promise<void> {
    if (this.semInternet()) { this.pixAutomatico.set(null); this.pixSoltos.set([]); return; } // sem internet: Pix pelo QR fixo
    try {
      const cfg = await firstValueFrom(this.http.get<{ pixAutomatico: string | null }>('/api/empresa/configuracao'));
      this.pixAutomatico.set(cfg.pixAutomatico);
      const sessao = this.caixa()?.sessaoId;
      this.pixSoltos.set(cfg.pixAutomatico && sessao
        ? await firstValueFrom(this.http.get<CobrancaPix[]>('/api/pix/cobrancas/soltas', { params: { sessaoCaixaId: sessao } }))
        : []);
    } catch {
      /* sem a configuração: o Pix fica manual nesta venda */
    }
  }

  // Sem o aviso (internet caiu, Mercado Pago fora do ar): o caixa confere no celular e confirma por conta dele.
  protected pixManual(): void {
    if (!confirm(`Confirmar o Pix de ${dinheiroBr(this.valorPix())} SEM o aviso do Mercado Pago?\n\nSó confirme se você viu o Pix cair no celular da loja.`)) return;
    const automatico = this.pixAutomatico();
    this.pixAutomatico.set(null); // deixa o "Adicionar" registrar este Pix como manual
    void this.confirmarValor().finally(() => this.pixAutomatico.set(automatico));
  }

  // O Mercado Pago confirmou: o Pix entra sozinho na venda (o servidor confere de novo ao registrar).
  protected pixPago(c: CobrancaPix): void {
    if (this.pagamentos().some((p) => p.cobrancaPixId === c.id)) return;
    if (c.valor > this.falta() + 0.001) {
      this.erro.set(`O Pix recebido (${dinheiroBr(c.valor)}) passa do que falta (${dinheiroBr(this.falta())}). Use-o em outra venda ou devolva pelo Mercado Pago.`);
      return;
    }
    this.erro.set(null);
    this.pagamentos.update((p) => [...p, { forma: 'Pix', valor: c.valor, cobrancaPixId: c.id }]);
    this.pixRecebidos.update((l) => [...l.filter((x) => x.id !== c.id), c]);
    this.pixSoltos.update((l) => l.filter((x) => x.id !== c.id));
    void this.avisarPixRecebido(c);
  }

  // Pix confirmado: modal grande + bipe. Se quitou a compra, a venda é registrada sozinha (sem "Confirmar venda").
  private async avisarPixRecebido(c: CobrancaPix): Promise<void> {
    clearTimeout(this.fecharPixOk);
    tocarBipe();
    const falta = this.falta();
    this.pixOk.set({ cobranca: c, situacao: falta > 0 ? 'falta' : 'registrando', falta });
    const dlg = this.dlgPixOk()?.nativeElement;
    if (dlg && !dlg.open) dlg.showModal();
    if (falta > 0) {
      this.escolherForma('Dinheiro');
      this.fecharPixOk = setTimeout(() => this.fecharAvisoPix(), 3000);
      return;
    }
    this.textoValor = '';
    await this.finalizar();
    const registrou = !this.dlgPagamento()?.nativeElement.open;
    this.pixOk.update((a) => (a ? { ...a, situacao: registrou ? 'registrada' : 'erro' } : a));
    // O cupom abriu por cima: traz o aviso de volta para a frente por mais uns segundos.
    if (dlg) { if (dlg.open) dlg.close(); dlg.showModal(); }
    this.fecharPixOk = setTimeout(() => this.fecharAvisoPix(), registrou ? 3000 : 5000);
  }

  protected fecharAvisoPix(): void {
    clearTimeout(this.fecharPixOk);
    const dlg = this.dlgPixOk()?.nativeElement;
    if (dlg?.open) dlg.close();
    if (this.pixOk()?.situacao === 'erro' || this.pixOk()?.situacao === 'falta') setTimeout(() => this.campoValor()?.nativeElement.focus());
    this.pixOk.set(null);
  }

  protected escolherForma(forma: FormaPagamento): void {
    this.formaAtual.set(forma);
    this.textoValor = this.falta().toFixed(2).replace('.', ',');
    // Fiado sem cliente escolhido: o cursor vai para a busca do cliente; com cliente, para o valor. Vale: o código.
    if (forma === 'Fiado' && !this.clienteFiado()) setTimeout(() => this.campoCliente()?.nativeElement.focus());
    else if (forma === 'ValeTroca') setTimeout(() => this.campoVale()?.nativeElement.focus());
    else setTimeout(() => this.campoValor()?.nativeElement.select());
  }

  // ----- Cliente (fiado e receber) -----

  // Digitou o nome/apelido/telefone: 0,3 s parado → busca.
  protected digitouCliente(texto: string): void {
    this.textoCliente = texto;
    clearTimeout(this.esperaCliente);
    if (texto.trim().length < 2) { this.clientesAchados.set([]); return; }
    this.esperaCliente = setTimeout(async () => {
      this.buscandoCliente.set(true);
      try { this.clientesAchados.set(await this.api.buscarClientes(texto.trim())); }
      catch { this.clientesAchados.set([]); }
      finally { this.buscandoCliente.set(false); }
    }, 300);
  }

  protected escolherClienteFiado(c: ClientePdv): void {
    this.clienteFiado.set(c);
    this.clientesAchados.set([]);
    this.textoCliente = '';
    this.erro.set(null);
    setTimeout(() => this.campoValor()?.nativeElement.select());
  }

  protected trocarClienteFiado(): void {
    // Já tem fiado lançado para este cliente nesta compra: tira antes (o fiado é de UM cliente).
    this.pagamentos.update((p) => p.filter((x) => x.forma !== 'Fiado'));
    this.limparCliente();
    setTimeout(() => this.campoCliente()?.nativeElement.focus());
  }

  private limparCliente(): void {
    this.clienteFiado.set(null);
    this.clientesAchados.set([]);
    this.textoCliente = '';
  }

  // Enter no valor: registra o pagamento; se já está tudo pago, finaliza.
  protected async confirmarValor(): Promise<void> {
    if (this.falta() === 0 && this.pagamentos().length) {
      await this.finalizar();
      return;
    }
    if (this.formaAtual() === 'ValeTroca') { await this.usarVale(); return; }
    const valor = arredondar(lerNumero(this.textoValor));
    if (Number.isNaN(valor) || valor <= 0) {
      this.erro.set('Valor inválido.');
      return;
    }
    if (this.formaAtual() === 'Pix' && this.pixAutomatico()) {
      this.erro.set('Com o Pix automático, gere o QR e espere o aviso do Mercado Pago (ou use "Confirmar o Pix manualmente").');
      return;
    }
    if (this.formaAtual() !== 'Dinheiro' && valor > this.falta()) {
      this.erro.set('Cartão, PIX e fiado não podem passar do valor que falta (troco só em dinheiro).');
      return;
    }
    if (this.formaAtual() === 'Fiado') {
      if (!this.clienteFiado()) { this.erro.set('Fiado: escolha o cliente primeiro.'); return; }
      const c = this.clienteFiado()!;
      if (!this.ehGerencia() && (c.restricao || c.saldo + valor > c.limiteCredito)) {
        this.erro.set((c.restricao ?? `Passa do limite de ${c.nome}: disponível ${dinheiroBr(c.disponivel)}.`) + ' Chame um gerente.');
        return;
      }
    }
    this.erro.set(null);
    this.pagamentos.update((p) => [...p, { forma: this.formaAtual(), valor }]);

    if (this.falta() > 0) {
      this.escolherForma(this.formaAtual());
    } else {
      // Pago! O campo de valor some; o foco vai para "Confirmar venda" (Enter confirma).
      this.textoValor = '';
      setTimeout(() => this.btnConfirmar()?.nativeElement.focus());
    }
  }

  protected notaRapida(valor: number): void {
    this.formaAtual.set('Dinheiro');
    this.textoValor = valor.toFixed(2).replace('.', ',');
    void this.confirmarValor();
  }

  protected removerPagamento(indice: number): void {
    const removido = this.pagamentos()[indice];
    this.pagamentos.update((p) => p.filter((_, i) => i !== indice));
    if (removido?.cobrancaPixId) void this.carregarPix(); // o Pix pago volta para "recebido sem venda"
    this.escolherForma(this.formaAtual());
  }

  protected async finalizar(): Promise<void> {
    if (this.falta() > 0 || this.finalizando()) return;
    this.finalizando.set(true);
    this.erro.set(null);
    const pedido = {
      itens: this.carrinho().map((i) => ({ produtoId: i.produto.id, quantidade: i.quantidade })),
      desconto: this.desconto(),
      pagamentos: this.pagamentos(),
      clienteId: this.pagamentos().some((p) => p.forma === 'Fiado') ? this.clienteFiado()?.id ?? null : null,
      idLocal: novoIdLocal(), // se a internet cair na resposta e a venda for guardada, o servidor não grava duas vezes
    };
    const podeOffline = !pedido.pagamentos.some((p) => p.cobrancaPixId || p.forma === 'Fiado' || p.forma === 'ValeTroca');
    try {
      let cupom: Cupom;
      try {
        if (this.semInternet() && podeOffline) throw new TypeError('sem internet');
        cupom = await this.api.vender(pedido);
      } catch (e) {
        if (!podeOffline || (!this.conexao.falhou(e) && !this.semInternet())) throw e;
        cupom = await this.guardarVendaOffline(pedido);
      }
      this.dlgPagamento()?.nativeElement.close();
      this.cupom.set(cupom);
      this.ultimoCupom.set(cupom);
      this.dlgCupom()?.nativeElement.showModal();
      if (this.impressora.config().automatica) this.imprimirCupom();
      // O botão só existe depois que o Angular desenha o cupom → foca no próximo "tique".
      setTimeout(() => this.btnNovaVenda()?.nativeElement.focus());
      this.limparVenda(false);
      const os = this.osEmCobranca();
      if (os && !cupom.offline) {
        this.osEmCobranca.set(null);
        await firstValueFrom(this.http.post(`${URL_ORDENS}/${os.id}/paga`, {}, { params: { vendaId: cupom.id } })).catch(() => null);
      }
      if (!cupom.offline) await this.atualizarResumo();
    } catch (e) {
      this.erro.set(this.mensagem(e));
    } finally {
      this.finalizando.set(false);
    }
  }

  // Sem internet: monta o cupom aqui (preço do catálogo guardado) e coloca a venda na fila para enviar depois.
  private async guardarVendaOffline(pedido: { desconto: number; pagamentos: { forma: FormaPagamento; valor: number }[]; idLocal: string }): Promise<Cupom> {
    const cx = this.caixa()!;
    const itens = this.carrinho().map((i) => ({
      produtoId: i.produto.id, descricao: i.produto.nome, unidade: i.produto.unidade, quantidade: i.quantidade,
      precoUnitario: i.produto.precoVenda, total: this.totalItem(i),
    }));
    const dataHora = new Date().toISOString();
    const cupom: Cupom = {
      id: 0, dataHora, numeroCaixa: cx.numeroCaixa, operador: this.auth.usuario()?.nome ?? cx.operador, status: 'Concluida',
      subtotal: this.subtotal(), desconto: this.desconto(), total: this.total(), valorPago: this.pago(), troco: this.troco(),
      itens, pagamentos: pedido.pagamentos.map((p) => ({ forma: p.forma, valor: p.valor })), motivoCancelamento: null,
      offline: true, idLocal: pedido.idLocal,
    };
    await this.offline.guardarVenda({ ...pedido, itens: [] }, cupom, pedido.idLocal, dataHora);
    return cupom;
  }

  // "Enviar agora" (e "tentar de novo" das que o servidor recusou).
  protected enviarPendentes(): void {
    void this.offline.sincronizar(true);
  }

  protected novaVenda(): void {
    this.dlgCupom()?.nativeElement.close();
    this.cupom.set(null);
    this.focarBusca();
  }

  // ================================================================ receber fiado (F6)

  protected abrirReceber(): void {
    if (this.precisaInternet('receber fiado')) return;
    if (this.carrinho().length) {
      this.erro.set('Finalize ou cancele a compra atual antes de receber um fiado.');
      return;
    }
    this.clienteReceber.set(null);
    this.limparCliente();
    this.textoReceber = '';
    this.textoRecebido = '';
    this.formaReceber.set('Dinheiro');
    this.recibo.set(null);
    this.erro.set(null);
    this.dlgReceber()?.nativeElement.showModal();
    setTimeout(() => this.campoCliente()?.nativeElement.focus());
  }

  protected escolherClienteReceber(c: ClientePdv): void {
    this.clienteReceber.set(c);
    this.clientesAchados.set([]);
    this.textoCliente = '';
    this.textoReceber = Math.max(0, c.saldo).toFixed(2).replace('.', ',');
    this.erro.set(null);
    setTimeout(() => this.campoReceber()?.nativeElement.select());
  }

  protected trocoReceber(): number {
    const valor = lerNumero(this.textoReceber), recebido = lerNumero(this.textoRecebido);
    return this.formaReceber() === 'Dinheiro' && recebido > valor ? arredondar(recebido - valor) : 0;
  }

  protected async confirmarReceber(): Promise<void> {
    const c = this.clienteReceber();
    if (!c) return;
    const valor = arredondar(lerNumero(this.textoReceber));
    if (Number.isNaN(valor) || valor <= 0) { this.erro.set('Informe o valor que o cliente está pagando.'); return; }
    if (valor > c.saldo) { this.erro.set(`${c.nome} deve ${dinheiroBr(c.saldo)}: receba até esse valor.`); return; }
    const recebido = lerNumero(this.textoRecebido);
    if (this.formaReceber() === 'Dinheiro' && !Number.isNaN(recebido) && recebido > 0 && recebido < valor) {
      this.erro.set('O dinheiro entregue é menor que o valor pago.');
      return;
    }
    await this.executar(async () => {
      const r = await this.api.receberFiado(c.id, valor, this.formaReceber());
      this.recibo.set(r);
      this.reciboRecebido = Number.isNaN(recebido) ? undefined : recebido;
      if (this.impressora.config().automatica) this.imprimirRecibo();
      this.caixa.set(await this.api.caixaAtual());
    });
  }

  protected imprimirRecibo(): void {
    const r = this.recibo(), cx = this.caixa();
    if (r && cx) this.impressora.imprimirHtml(this.impressora.htmlReciboFiado(r, cx.numeroCaixa, this.loja(), this.reciboRecebido));
  }

  protected concluirReceber(): void {
    this.dlgReceber()?.nativeElement.close(); // o (close) da janela faz o resto
  }

  // Fechou a janela (Concluir, Esc ou o X): aviso do que foi recebido e o cursor de volta na busca de produtos.
  protected aoFecharReceber(): void {
    const r = this.recibo();
    if (r) this.mostrarAviso(`Fiado de ${r.cliente}: recebido ${dinheiroBr(r.valor)}. ${r.saldoAtual > 0 ? 'Ainda deve ' + dinheiroBr(r.saldoAtual) + '.' : 'Quitado!'}`);
    this.recibo.set(null);
    this.erro.set(null);
    this.clientesAchados.set([]);
    this.focarBusca();
  }

  protected rotuloSaldo(saldo: number): string {
    return saldo > 0 ? `deve ${dinheiroBr(saldo)}` : saldo < 0 ? `crédito de ${dinheiroBr(-saldo)}` : 'não deve nada';
  }

  // ================================================================ sangria / suprimento / fechamento

  protected abrirMovimento(tipo: 'sangria' | 'suprimento'): void {
    if (this.precisaInternet(tipo === 'sangria' ? 'fazer sangria' : 'fazer suprimento')) return;
    this.tipoMovimento.set(tipo);
    this.textoMovimento = '';
    this.motivoMovimento = tipo === 'sangria' ? 'Recolhimento para o cofre' : 'Troco';
    this.erro.set(null);
    this.dlgMovimento()?.nativeElement.showModal();
  }

  protected async confirmarMovimento(): Promise<void> {
    const valor = lerNumero(this.textoMovimento);
    if (Number.isNaN(valor) || valor <= 0) {
      this.erro.set('Informe um valor maior que zero.');
      return;
    }
    await this.executar(async () => {
      const resumo = await this.api.movimentar(this.tipoMovimento(), valor, this.motivoMovimento);
      this.caixa.set(resumo);
      this.dlgMovimento()?.nativeElement.close();
      if (this.imprimirComprovante)
        this.impressora.imprimirHtml(this.impressora.htmlMovimento(this.tipoMovimento(), valor, this.motivoMovimento, resumo, this.loja()));
      this.mostrarAviso(`${this.tipoMovimento() === 'sangria' ? 'Sangria' : 'Suprimento'} de ${valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })} registrado.`);
      this.focarBusca();
    });
  }

  protected abrirFechamento(): void {
    if (this.precisaInternet('fechar o caixa')) return;
    if (this.offline.fila().length) {
      this.erro.set(`Ainda há ${this.offline.fila().length} registro(s) feitos sem internet para enviar. Clique em "Enviar agora" antes de fechar o caixa.`);
      void this.offline.sincronizar(true);
      return;
    }
    if (this.carrinho().length) {
      this.erro.set('Finalize ou cancele a compra atual antes de fechar o caixa.');
      return;
    }
    this.textoContado = '';
    this.observacaoFechamento = '';
    this.fechamento.set(null);
    this.erro.set(null);
    this.dlgFechar()?.nativeElement.showModal();
  }

  protected async confirmarFechamento(): Promise<void> {
    const contado = lerNumero(this.textoContado);
    if (Number.isNaN(contado) || contado < 0) {
      this.erro.set('Informe quanto dinheiro há na gaveta.');
      return;
    }
    await this.executar(async () => {
      this.fechamento.set(await this.api.fechar(contado, this.observacaoFechamento.trim() || null));
      if (this.impressora.config().automatica) this.imprimirFechamento();
    });
  }

  protected concluirFechamento(): void {
    this.dlgFechar()?.nativeElement.close();
    this.fechamento.set(null);
    this.caixa.set(null);
  }

  // ================================================================ teclado e utilidades

  protected teclaGlobal(e: KeyboardEvent): void {
    if (!this.caixa()) return;
    const algumaJanelaAberta = !!document.querySelector('dialog[open]');

    if (e.key === 'F2' && !algumaJanelaAberta && !this.produtoPeso()) {
      e.preventDefault();
      this.abrirPagamento();
    } else if (e.key === 'F6' && this.comFiado && !algumaJanelaAberta && !this.produtoPeso()) {
      e.preventDefault();
      this.abrirReceber();
    } else if (e.key === 'F7' && !algumaJanelaAberta && !this.produtoPeso()) {
      e.preventDefault();
      this.abrirTroca();
    } else if (e.key === 'Escape' && !algumaJanelaAberta) {
      clearTimeout(this.esperaDigitacao);
      this.resultados.set([]);
      this.semResultado.set(null);
      this.produtoPeso.set(null);
      this.erro.set(null);
      this.focarBusca();
    } else if (this.dlgPagamento()?.nativeElement.open && e.altKey && /^[1-9]$/.test(e.key) && Number(e.key) <= this.formas.length) {
      // Alt+1..6 troca a forma de pagamento sem tirar a mão do teclado (na ordem dos botões).
      e.preventDefault();
      this.escolherForma(this.formas[Number(e.key) - 1].valor);
    }
  }

  protected fracionado(p: Produto): boolean {
    return p.unidade === 'KG' || p.unidade === 'L';
  }

  protected imprimirCupom(): void {
    const c = this.cupom();
    if (c) this.impressora.imprimirCupom(c, this.loja());
    setTimeout(() => this.btnNovaVenda()?.nativeElement.focus(), 300);
  }

  // "Moço, me dá o cupom?" — depois que a janela da venda já fechou.
  protected reimprimirUltimo(): void {
    const c = this.ultimoCupom();
    if (c) this.impressora.imprimirCupom(c, this.loja());
    this.focarBusca();
  }

  protected imprimirFechamento(): void {
    const f = this.fechamento();
    if (f) this.impressora.imprimirHtml(this.impressora.htmlFechamento(f, this.loja()));
  }

  // O que não dá para fazer sem internet: avisa (junto da barra do caixa) em vez de abrir a janela.
  private precisaInternet(acao: string): boolean {
    if (!this.semInternet()) return false;
    this.erro.set(`Sem internet não dá para ${acao}. As vendas continuam normalmente; isto volta quando a internet voltar.`);
    return true;
  }

  private loja(): string {
    return this.auth.usuario()?.empresa.nome ?? MARCA.nome;
  }

  protected rotuloForma(forma: string): string {
    return this.formas.find((f) => f.valor === forma)?.rotulo ?? forma;
  }

  // "Dinheiro + Pix" → "Dinheiro + Pix" com os rótulos da tela (Debito → Débito).
  protected formasTexto(formas: string): string {
    return formas.split(' + ').map((f) => this.rotuloForma(f)).join(' + ');
  }

  protected valorForma(resumo: ResumoCaixa, forma: string): number {
    return resumo.porForma.find((f) => f.forma === forma)?.valor ?? 0;
  }

  protected somaFiadoRecebido(resumo: ResumoCaixa): number {
    return (resumo.recebimentosFiado ?? []).reduce((s, f) => s + f.valor, 0);
  }

  protected fiadoEmDinheiro(resumo: ResumoCaixa): number {
    return resumo.recebimentosFiado?.find((f) => f.forma === 'Dinheiro')?.valor ?? 0;
  }

  // ----- Venda em andamento guardada (localStorage, por usuário e por turno de caixa) -----
  private vendaRestaurada: number | null = null;

  private chaveVenda(sessao: number): string {
    return `marcus.vendaEmAndamento:${this.auth.usuario()?.id ?? ''}:${sessao}`;
  }

  private guardarVenda(sessao: number, itens: ItemCarrinho[], desconto: number): void {
    try {
      if (itens.length) localStorage.setItem(this.chaveVenda(sessao), JSON.stringify({ itens, desconto }));
      else localStorage.removeItem(this.chaveVenda(sessao));
    } catch { /* sem armazenamento: a venda só não sobrevive a sair da tela */ }
  }

  private restaurarVenda(sessao: number): void {
    this.vendaRestaurada = sessao;
    setTimeout(() => this.verificarOrdem());
    try {
      // Turnos antigos deste usuário (caixa já fechado): não servem mais.
      const prefixo = `marcus.vendaEmAndamento:${this.auth.usuario()?.id ?? ''}:`;
      for (const k of Object.keys(localStorage)) if (k.startsWith(prefixo) && k !== this.chaveVenda(sessao)) localStorage.removeItem(k);
      const guardada = JSON.parse(localStorage.getItem(this.chaveVenda(sessao)) ?? 'null') as { itens: ItemCarrinho[]; desconto: number } | null;
      if (!guardada?.itens?.length || this.carrinho().length) return;
      this.carrinho.set(guardada.itens);
      this.desconto.set(guardada.desconto ?? 0);
      this.textoDesconto = guardada.desconto ? guardada.desconto.toFixed(2).replace('.', ',') : '';
      this.mostrarAviso(`Venda em andamento recuperada: ${guardada.itens.length} ${guardada.itens.length === 1 ? 'item' : 'itens'}.`);
    } catch { /* guardado inválido: começa do zero */ }
  }

  // Só depois que a venda guardada neste computador foi recuperada (senão ela apagaria os itens da OS).
  private verificarOrdem(): void {
    const os = this.os(), sessao = this.caixa()?.sessaoId;
    if (os && sessao && this.osCarregada !== os && this.vendaRestaurada === sessao) void this.cobrarOrdem(os);
  }

  private async cobrarOrdem(id: string): Promise<void> {
    this.osCarregada = id;
    // Tira o ?os= do endereço: recarregar a página (F5) não lança a ordem de novo (a venda já fica guardada).
    void this.router.navigate([], { queryParams: { os: null }, queryParamsHandling: 'merge', replaceUrl: true });
    try {
      const o = await firstValueFrom(this.http.get<Ordem>(`${URL_ORDENS}/${id}`));
      if (o.paga) { this.mostrarAviso(`A OS ${o.numero} já está paga.`); return; }
      const servico = (await this.procurar(o.servico)).find((p) => p.nome === o.servico && p.servico);
      if (servico) this.adicionar(servico, 1);
      if (o.corda && !/cliente/i.test(o.corda)) {
        const corda = (await this.procurar(o.corda)).find((p) => !p.servico && p.nome.startsWith(o.corda!));
        if (corda) this.adicionar(corda, 1);
      }
      this.osEmCobranca.set({ id: o.id, numero: o.numero });
      this.mostrarAviso(`OS ${o.numero} de ${o.cliente}: confira os itens e finalize. A ordem fica paga ao concluir.`);
    } catch (e) {
      this.erro.set(this.mensagem(e));
    }
  }

  private limparVenda(focar = true): void {
    this.carrinho.set([]);
    this.creditoTroca.set(null);
    this.desconto.set(0);
    this.textoDesconto = '';
    this.pagamentos.set([]);
    this.ultimoId.set(null);
    this.resultados.set([]);
    this.textoBusca = '';
    if (focar) this.focarBusca();
  }

  protected focarBusca(): void {
    setTimeout(() => this.campoBusca()?.nativeElement.focus());
  }

  private mostrarAviso(texto: string): void {
    this.aviso.set(texto);
    setTimeout(() => this.aviso.set(null), 5000);
  }

  private async executar(acao: () => Promise<void>): Promise<void> {
    this.erro.set(null);
    try {
      await acao();
    } catch (e) {
      this.erro.set(this.mensagem(e));
    }
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      if (e.error?.errors) return Object.values<string[]>(e.error.errors).flat().join(' ');
      if (e.status === 0) return 'Sem conexão com o servidor.';
    }
    return 'Não foi possível concluir. Tente de novo.';
  }
}

function dinheiroBr(v: number): string {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

// "3*arroz" → { quantidade: 3, termo: "arroz" } · "arroz" → { quantidade: null, termo: "arroz" }
function separarQuantidade(texto: string): { quantidade: number | null; termo: string } {
  const m = /^(\d+(?:[.,]\d+)?)\s*\*\s*(.*)$/.exec(texto);
  return m ? { quantidade: lerNumero(m[1]), termo: m[2].trim() } : { quantidade: null, termo: texto };
}

// Dois toques curtos e agradáveis quando o Pix é confirmado (o caixa ouve mesmo sem olhar a tela).
function tocarBipe(): void {
  try {
    const ctx = new AudioContext();
    [[880, 0], [1320, 0.16]].forEach(([freq, quando]) => {
      const osc = ctx.createOscillator(), vol = ctx.createGain();
      osc.frequency.value = freq;
      vol.gain.setValueAtTime(0.0001, ctx.currentTime + quando);
      vol.gain.exponentialRampToValueAtTime(0.25, ctx.currentTime + quando + 0.02);
      vol.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + quando + 0.18);
      osc.connect(vol).connect(ctx.destination);
      osc.start(ctx.currentTime + quando);
      osc.stop(ctx.currentTime + quando + 0.2);
    });
    setTimeout(() => void ctx.close(), 800);
  } catch {
    /* navegador sem som: fica só o aviso na tela */
  }
}

// Na lista da busca, um modelo da grade aparece UMA vez (escolhe o tamanho/cor depois), não uma linha por número.
function agruparGrade(itens: Produto[]): Produto[] {
  const vistos = new Set<number>();
  return itens.filter((p) => !p.modeloId || (!vistos.has(p.modeloId) && vistos.add(p.modeloId)));
}

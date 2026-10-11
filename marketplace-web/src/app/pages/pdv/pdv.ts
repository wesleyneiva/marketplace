import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Pagina, Produto } from '../../core/api/produtos.api';
import {
  CaixaDisponivel, ClientePdv, Cupom, FORMAS_PAGAMENTO, FORMAS_RECEBER, FormaPagamento, PdvApi, ReciboFiado, ResumoCaixa, arredondar, lerNumero,
} from '../../core/api/pdv.api';
import { QuantidadePipe } from '../../shared/quantidade.pipe';
import { Logo } from '../../shared/logo';
import { BalancaService } from '../../core/equipamentos/balanca.service';
import { ImpressoraService } from '../../core/equipamentos/impressora.service';
import { Icone } from '../../shared/icone';

interface ItemCarrinho {
  produto: Produto;
  quantidade: number;
}

// Tela do caixa (PDV). Feita para TECLADO: o campo de busca fica sempre focado.
//   • Bipa o código de barras (ou digita parte do nome) e Enter.
//   • "3*arroz" ou "3*789..." → 3 unidades.  Produto por KG pede o peso.
//   • F2 → pagamento.  Esc → fecha listas/janelas.  F6 → receber fiado (cliente pagando a caderneta).
@Component({
  selector: 'app-pdv',
  imports: [Icone, FormsModule, CurrencyPipe, DatePipe, QuantidadePipe, Logo],
  templateUrl: './pdv.html',
  styleUrl: './pdv.scss',
  host: { '(document:keydown)': 'teclaGlobal($event)' },
})
export class Pdv {
  private readonly api = inject(PdvApi);
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  protected readonly nomeLoja = computed(() => this.auth.usuario()?.empresa.nome ?? '');
  protected readonly balanca = inject(BalancaService);
  protected readonly impressora = inject(ImpressoraService);

  protected readonly formas = FORMAS_PAGAMENTO;
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
  protected textoPeso = '';
  protected readonly lendoBalanca = signal(false);

  // ----- Pagamento -----
  protected readonly pagamentos = signal<{ forma: FormaPagamento; valor: number }[]>([]);
  protected readonly formaAtual = signal<FormaPagamento>('Dinheiro');
  protected textoValor = '';
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
  private readonly dlgMovimento = viewChild<ElementRef<HTMLDialogElement>>('dlgMovimento');
  private readonly dlgFechar = viewChild<ElementRef<HTMLDialogElement>>('dlgFechar');
  private readonly dlgReceber = viewChild<ElementRef<HTMLDialogElement>>('dlgReceber');
  private readonly campoCliente = viewChild<ElementRef<HTMLInputElement>>('campoCliente');
  private readonly campoReceber = viewChild<ElementRef<HTMLInputElement>>('campoReceber');

  constructor() {
    void this.carregarCaixa();
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
    try {
      this.caixa.set(await this.api.caixaAtual());
      this.focarBusca();
    } catch {
      this.caixa.set(null);
      this.erro.set('Não foi possível consultar o caixa.');
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
      if (/^2\d{12}$/.test(termo) && (await this.lerEtiquetaBalanca(termo))) return;

      const pagina = await firstValueFrom(
        this.http.get<Pagina<Produto>>('/api/produtos', { params: { busca: termo, tamanho: 8 } }),
      );
      const exato = pagina.itens.find((p) => p.codigoBarras === termo);
      if (exato || pagina.itens.length === 1) {
        this.escolher(exato ?? pagina.itens[0]);
      } else if (pagina.itens.length === 0) {
        this.erro.set(this.avisoBalanca ?? `Produto não encontrado: "${termo}"`);
        this.textoBusca = '';
      } else {
        this.resultados.set(pagina.itens);
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
      const pagina = await firstValueFrom(this.http.get<Pagina<Produto>>('/api/produtos', { params: { busca: termo, tamanho: 8 } }));
      // Se a pessoa continuou digitando (ou já apertou Enter), esta resposta ficou velha.
      if (separarQuantidade(this.textoBusca.trim()).termo !== termo) return;
      this.termoDaLista = termo;
      this.resultados.set(pagina.itens);
      this.indiceResultado.set(0);
      this.semResultado.set(pagina.itens.length ? null : termo);
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

  protected escolher(produto: Produto): void {
    this.resultados.set([]);
    this.semResultado.set(null);
    clearTimeout(this.esperaDigitacao);
    this.textoBusca = '';
    const quantidade = this.quantidadeDaBusca;
    this.quantidadeDaBusca = null;

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
    if (naLista.quantidade > produto.estoqueAtual) {
      this.erro.set(`Atenção: "${produto.nome}" tem só ${produto.estoqueAtual} ${produto.unidade} no estoque.`);
    }
    this.ultimoId.set(produto.id);
    this.focarBusca();
  }

  protected remover(item: ItemCarrinho): void {
    this.carrinho.update((itens) => itens.filter((i) => i !== item));
    this.focarBusca();
  }

  protected cancelarCompra(): void {
    if (this.carrinho().length && !confirm('Cancelar a compra atual? Todos os itens serão removidos.')) return;
    this.limparVenda();
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
    this.escolherForma('Dinheiro');
    this.dlgPagamento()?.nativeElement.showModal();
  }

  protected escolherForma(forma: FormaPagamento): void {
    this.formaAtual.set(forma);
    this.textoValor = this.falta().toFixed(2).replace('.', ',');
    // Fiado sem cliente escolhido: o cursor vai para a busca do cliente; com cliente, para o valor.
    if (forma === 'Fiado' && !this.clienteFiado()) setTimeout(() => this.campoCliente()?.nativeElement.focus());
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
    const valor = arredondar(lerNumero(this.textoValor));
    if (Number.isNaN(valor) || valor <= 0) {
      this.erro.set('Valor inválido.');
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
    this.pagamentos.update((p) => p.filter((_, i) => i !== indice));
    this.escolherForma(this.formaAtual());
  }

  protected async finalizar(): Promise<void> {
    if (this.falta() > 0 || this.finalizando()) return;
    this.finalizando.set(true);
    this.erro.set(null);
    try {
      const cupom = await this.api.vender({
        itens: this.carrinho().map((i) => ({ produtoId: i.produto.id, quantidade: i.quantidade })),
        desconto: this.desconto(),
        pagamentos: this.pagamentos(),
        clienteId: this.pagamentos().some((p) => p.forma === 'Fiado') ? this.clienteFiado()?.id ?? null : null,
      });
      this.dlgPagamento()?.nativeElement.close();
      this.cupom.set(cupom);
      this.ultimoCupom.set(cupom);
      this.dlgCupom()?.nativeElement.showModal();
      if (this.impressora.config().automatica) this.imprimirCupom();
      // O botão só existe depois que o Angular desenha o cupom → foca no próximo "tique".
      setTimeout(() => this.btnNovaVenda()?.nativeElement.focus());
      this.limparVenda(false);
      this.caixa.set(await this.api.caixaAtual());
    } catch (e) {
      this.erro.set(this.mensagem(e));
    } finally {
      this.finalizando.set(false);
    }
  }

  protected novaVenda(): void {
    this.dlgCupom()?.nativeElement.close();
    this.cupom.set(null);
    this.focarBusca();
  }

  // ================================================================ receber fiado (F6)

  protected abrirReceber(): void {
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
    } else if (e.key === 'F6' && !algumaJanelaAberta && !this.produtoPeso()) {
      e.preventDefault();
      this.abrirReceber();
    } else if (e.key === 'Escape' && !algumaJanelaAberta) {
      clearTimeout(this.esperaDigitacao);
      this.resultados.set([]);
      this.semResultado.set(null);
      this.produtoPeso.set(null);
      this.erro.set(null);
      this.focarBusca();
    } else if (this.dlgPagamento()?.nativeElement.open && e.altKey && ['1', '2', '3', '4', '5'].includes(e.key)) {
      // Alt+1..5 troca a forma de pagamento sem tirar a mão do teclado (5 = fiado).
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

  private loja(): string {
    return this.auth.usuario()?.empresa.nome ?? 'Marketplace';
  }

  protected rotuloForma(forma: string): string {
    return this.formas.find((f) => f.valor === forma)?.rotulo ?? forma;
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

  private limparVenda(focar = true): void {
    this.carrinho.set([]);
    this.desconto.set(0);
    this.textoDesconto = '';
    this.pagamentos.set([]);
    this.ultimoId.set(null);
    this.resultados.set([]);
    this.textoBusca = '';
    if (focar) this.focarBusca();
  }

  private focarBusca(): void {
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

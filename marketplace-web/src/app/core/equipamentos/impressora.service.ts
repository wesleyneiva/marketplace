import { Injectable, signal } from '@angular/core';
import { Cupom, FORMAS_PAGAMENTO, ResumoCaixa } from '../api/pdv.api';
import { Troca, rotuloMotivo } from '../api/trocas.api';

// Impressora TÉRMICA de cupom (Elgin i9, Epson TM-T20, Bematech MP-4200, Daruma…), instalada no Windows como
// impressora comum. O sistema monta o cupom no tamanho da bobina e manda imprimir por um quadro escondido.
// Para sair DIRETO, sem a janela de impressão: atalho do Chrome com --kiosk-printing (ver tela Configurações).
// A configuração fica NESTE computador (cada caixa tem a sua impressora).

export interface ConfigImpressora {
  largura: 80 | 58;        // largura da bobina em mm
  automatica: boolean;     // imprime sozinho ao finalizar a venda
  vias: number;            // 1 ou 2 (2 = uma para a loja)
  rodape: string;          // mensagem no fim do cupom
}

const CHAVE = 'marcus.impressora';
const CHAVE_ANTIGA = 'marketplace.impressora'; // antes da troca de nome (lida uma vez e migrada)
const PADRAO: ConfigImpressora = { largura: 80, automatica: false, vias: 1, rodape: 'Obrigado pela preferência! Volte sempre.' };

@Injectable({ providedIn: 'root' })
export class ImpressoraService {
  readonly config = signal<ConfigImpressora>(this.carregar());

  salvarConfig(mudancas: Partial<ConfigImpressora>): void {
    const nova = { ...this.config(), ...mudancas };
    this.config.set(nova);
    try {
      localStorage.setItem(CHAVE, JSON.stringify(nova));
    } catch {
      /* sem armazenamento: vale só até fechar o navegador */
    }
  }

  imprimirCupom(cupom: Cupom, loja: string): void {
    this.imprimirHtml(this.htmlCupom(cupom, loja));
  }

  // O cupom em HTML (também usado na prévia da tela de Configurações).
  htmlCupom(c: Cupom, loja: string): string {
    const cfg = this.config();
    const qtd = (v: number, un: string) =>
      un === 'KG' || un === 'L' ? v.toLocaleString('pt-BR', { minimumFractionDigits: 3, maximumFractionDigits: 3 }) : String(v);
    const data = new Date(c.dataHora).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

    const itens = c.itens.map((i, n) => `
      <div class="item"><span>${String(n + 1).padStart(3, '0')} ${esc(i.descricao)}</span></div>
      <div class="linha"><span>${qtd(i.quantidade, i.unidade)} ${esc(i.unidade)} x ${dinheiro(i.precoUnitario)}</span><span>${dinheiro(i.total)}</span></div>`).join('');
    const pagamentos = c.pagamentos.map((p) => `<div class="linha"><span>${esc(rotuloForma(p.forma))}${p.valeCodigo ? ' ' + esc(p.valeCodigo) : ''}${p.parcelas && p.parcelas > 1 ? ` ${p.parcelas}x de ${dinheiro(p.valor / p.parcelas)}` : ''}</span><span>${dinheiro(p.valor)}</span></div>`
      // Vale-troca com saldo: o que ficou e até quando vale (o cliente guarda este cupom junto com o vale).
      + (p.valeCodigo && p.valeSaldo ? `<div class="linha"><span>Saldo no vale${p.valeValidade ? ' (até ' + diaBr(p.valeValidade) + ')' : ''}</span><span>${dinheiro(p.valeSaldo)}</span></div>` : '')).join('');
    // Fiado: quem comprou, quanto deve agora, quando vence e a ASSINATURA (a "nota" da caderneta).
    const fiado = c.pagamentos.find((p) => p.forma === 'Fiado');
    const cli = c.cliente;
    const blocoCliente = !cli ? '' : fiado ? `
        <div class="tracos"></div>
        <div class="centro forte">COMPRA NO FIADO</div>
        <div class="linha"><span>Cliente</span><span>${esc(cli.nome)}</span></div>
        ${cli.apelido ? `<div class="linha"><span></span><span>${esc(cli.apelido)}</span></div>` : ''}
        <div class="linha"><span>Nesta compra</span><span>${dinheiro(fiado.valor)}</span></div>
        ${cli.vencimentoFiado ? `<div class="linha"><span>Vence em</span><span>${diaBr(cli.vencimentoFiado)}</span></div>` : ''}
        ${cli.saldoDevedor !== null ? `<div class="linha forte"><span>${cli.saldoDevedor >= 0 ? 'SALDO DEVEDOR' : 'CRÉDITO'}</span><span>${dinheiro(Math.abs(cli.saldoDevedor))}</span></div>` : ''}
        <div class="assinatura">${esc(cli.nome)}</div>`
      : `<div class="linha"><span>Cliente</span><span>${esc(cli.nome)}</span></div>`;

    // Venda cancelada: quando, por quê, quem autorizou e a assinatura do cliente que recebeu o dinheiro de volta.
    const can = c.status === 'Cancelada' ? c.cancelamento : null;
    const blocoCancelamento = c.status !== 'Cancelada' ? '' : `
        <div class="tracos"></div>
        <div class="centro forte">ESTORNO / CANCELAMENTO</div>
        ${can?.em ? `<div class="linha"><span>Cancelada em</span><span>${new Date(can.em).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</span></div>` : ''}
        ${c.motivoCancelamento ? `<div class="linha"><span>Motivo</span><span>${esc(c.motivoCancelamento)}</span></div>` : ''}
        ${can?.autorizadoPor ? `<div class="linha"><span>Autorizado por</span><span>${esc(can.autorizadoPor)}</span></div>` : ''}
        <div class="linha forte"><span>DEVOLVIDO</span><span>${dinheiro(c.total)}</span></div>
        <div class="assinatura">Cliente</div>`;

    const via = (n: number) => `
      <section class="via">
        <div class="centro forte grande">${esc(loja)}</div>
        <div class="centro">CUPOM NÃO FISCAL${cfg.vias > 1 ? ` · ${n === 1 ? 'via do cliente' : 'via da loja'}` : ''}</div>
        <div class="tracos"></div>
        <div class="linha"><span>${c.id ? `Venda nº ${c.id}` : `Venda sem internet ${esc((c.idLocal ?? '').slice(0, 8).toUpperCase())}`}</span><span>${data}</span></div>
        <div class="linha"><span>Caixa ${c.numeroCaixa}</span><span>${esc(c.operador)}</span></div>
        ${c.vendedor ? `<div class="linha"><span>Vendedor</span><span>${esc(c.vendedor)}</span></div>` : ''}
        ${c.status === 'Cancelada' ? '<div class="centro forte">*** VENDA CANCELADA ***</div>' : ''}
        <div class="tracos"></div>
        ${itens}
        <div class="tracos"></div>
        <div class="linha"><span>${c.itens.length} ${c.itens.length === 1 ? 'item' : 'itens'} · Subtotal</span><span>${dinheiro(c.subtotal)}</span></div>
        ${c.desconto > 0 ? `<div class="linha"><span>Desconto</span><span>-${dinheiro(c.desconto)}</span></div>` : ''}
        <div class="linha forte grande"><span>TOTAL R$</span><span>${dinheiro(c.total)}</span></div>
        <div class="tracos"></div>
        ${pagamentos}
        ${c.troco > 0 ? `<div class="linha forte"><span>TROCO</span><span>${dinheiro(c.troco)}</span></div>` : ''}
        ${blocoCliente}
        ${blocoCancelamento}
        <div class="tracos"></div>
        <div class="centro">${esc(cfg.rodape)}</div>
        <div class="centro pequeno">NÃO É DOCUMENTO FISCAL</div>
      </section>`;

    return this.envelope(`Cupom ${c.id}`, Array.from({ length: Math.max(1, Math.min(3, cfg.vias)) }, (_, i) => via(i + 1)).join(''));
  }

  // Fechamento do caixa: o papel que vai junto com o dinheiro para quem confere.
  htmlFechamento(r: ResumoCaixa, loja: string): string {
    const linha = (rotulo: string, valor: string, classe = '') => `<div class="linha ${classe}"><span>${rotulo}</span><span>${valor}</span></div>`;
    const formas = r.porForma.map((f) => linha(esc(rotuloForma(f.forma)), dinheiro(f.valor))).join('');
    const diferenca = r.diferenca ?? 0;
    const fiadoRecebido = r.recebimentosFiado ?? [];
    const fiadoEmDinheiro = fiadoRecebido.find((f) => f.forma === 'Dinheiro')?.valor ?? 0;
    const blocoFiado = fiadoRecebido.length
      ? `<div class="tracos"></div><div class="forte">Fiado recebido (clientes que pagaram)</div>${fiadoRecebido.map((f) => linha(esc(rotuloForma(f.forma)), dinheiro(f.valor))).join('')}`
      : '';
    const corpo = `
      <section class="via">
        <div class="centro forte grande">${esc(loja)}</div>
        <div class="centro forte">FECHAMENTO DE CAIXA</div>
        <div class="tracos"></div>
        ${linha(`Caixa ${r.numeroCaixa}`, esc(r.operador))}
        ${linha('Abertura', dataHora(r.abertaEm))}
        ${linha('Fechamento', r.fechadaEm ? dataHora(r.fechadaEm) : '—')}
        <div class="tracos"></div>
        ${linha(`Vendas (${r.quantidadeVendas})`, dinheiro(r.totalVendido), 'forte')}
        ${linha('Ticket médio', dinheiro(r.ticketMedio))}
        ${r.vendasCanceladas ? linha(`Canceladas (${r.vendasCanceladas})`, dinheiro(r.valorCancelado)) : ''}
        <div class="tracos"></div>
        <div class="forte">Por forma de pagamento</div>
        ${formas || linha('(nenhuma venda)', '')}
        ${blocoFiado}
        <div class="tracos"></div>
        <div class="forte">Dinheiro na gaveta</div>
        ${linha('Troco inicial', dinheiro(r.valorAbertura))}
        ${linha('+ Recebido em dinheiro', dinheiro((r.porForma.find((f) => f.forma === 'Dinheiro')?.valor ?? 0) + r.trocoDado))}
        ${linha('− Troco dado', dinheiro(r.trocoDado))}
        ${fiadoEmDinheiro ? linha('+ Fiado recebido', dinheiro(fiadoEmDinheiro)) : ''}
        ${linha('+ Suprimentos', dinheiro(r.suprimentos))}
        ${linha('− Sangrias', dinheiro(r.sangrias))}
        ${r.devolucoes ? linha('− Devoluções (estornos)', dinheiro(r.devolucoes)) : ''}
        ${linha('= Esperado', dinheiro(r.dinheiroEsperado), 'forte')}
        ${linha('Contado', r.valorContado === null ? '—' : dinheiro(r.valorContado), 'forte')}
        ${linha(diferenca === 0 ? 'Diferença' : diferenca > 0 ? 'SOBRA' : 'FALTA', dinheiro(Math.abs(diferenca)), 'forte grande')}
        ${r.observacaoFechamento ? `<div class="tracos"></div><div>Obs.: ${esc(r.observacaoFechamento)}</div>` : ''}
        <div class="assinatura">Operador</div>
        <div class="assinatura">Conferido por</div>
        <div class="centro pequeno">Impresso em ${dataHora(new Date().toISOString())}</div>
      </section>`;
    return this.envelope(`Fechamento do caixa ${r.numeroCaixa}`, corpo);
  }

  // Recibo do fiado pago no balcão (uma via para o cliente; a loja tem o registro na caderneta).
  htmlReciboFiado(r: { cliente: string; valor: number; forma: string; saldoAnterior: number; saldoAtual: number; dataHora: string; operador: string },
    numeroCaixa: number, loja: string, recebido?: number): string {
    const troco = recebido && recebido > r.valor ? recebido - r.valor : 0;
    const corpo = `
      <section class="via">
        <div class="centro forte grande">${esc(loja)}</div>
        <div class="centro forte">RECIBO · PAGAMENTO DE FIADO</div>
        <div class="tracos"></div>
        <div class="linha"><span>Caixa ${numeroCaixa}</span><span>${esc(r.operador)}</span></div>
        <div class="linha"><span>Data</span><span>${dataHora(r.dataHora)}</span></div>
        <div class="linha"><span>Cliente</span><span>${esc(r.cliente)}</span></div>
        <div class="tracos"></div>
        <div class="linha"><span>Devia</span><span>${dinheiro(r.saldoAnterior)}</span></div>
        <div class="linha forte grande"><span>PAGOU (${esc(rotuloForma(r.forma))})</span><span>${dinheiro(r.valor)}</span></div>
        ${troco ? `<div class="linha"><span>Recebido</span><span>${dinheiro(recebido!)}</span></div><div class="linha forte"><span>TROCO</span><span>${dinheiro(troco)}</span></div>` : ''}
        <div class="linha forte"><span>${r.saldoAtual > 0 ? 'AINDA DEVE' : r.saldoAtual < 0 ? 'CRÉDITO' : 'SALDO'}</span><span>${r.saldoAtual === 0 ? 'quitado' : dinheiro(Math.abs(r.saldoAtual))}</span></div>
        <div class="tracos"></div>
        <div class="centro">Obrigado!</div>
        <div class="centro pequeno">NÃO É DOCUMENTO FISCAL</div>
      </section>`;
    return this.envelope(`Recibo de fiado`, corpo);
  }

  // Vale-troca: o cliente leva e usa como pagamento numa próxima compra (o caixa digita o código).
  // Duas partes: o vale do cliente e o comprovante da troca (itens que voltaram), com a assinatura.
  htmlValeTroca(t: Troca, loja: string): string {
    const v = t.vale;
    const itens = t.itens.map((i) => `<div class="item"><span>${i.quantidade} × ${esc(i.descricao)}</span></div>
      <div class="linha"><span></span><span>${dinheiro(i.valor)}</span></div>`).join('');
    const corpo = `
      <section class="via">
        <div class="centro forte grande">${esc(loja)}</div>
        <div class="centro forte">VALE-TROCA</div>
        <div class="tracos"></div>
        <div class="centro forte grande">R$ ${dinheiro(v.saldo)}</div>
        <div class="centro forte grande">${esc(v.codigo)}</div>
        <div class="centro">Válido até ${diaBr(v.validade)}</div>
        ${v.cliente ? `<div class="centro">Cliente: ${esc(v.cliente)}</div>` : ''}
        <div class="tracos"></div>
        <div class="centro pequeno">Apresente este vale no caixa. Vale como pagamento em qualquer produto da loja; o que sobrar
          fica no vale até a validade. Não é trocado por dinheiro.</div>
        <div class="tracos"></div>
        <div class="forte">Troca da venda nº ${t.vendaId}</div>
        <div class="linha"><span>Data</span><span>${dataHora(t.dataHora)}</span></div>
        <div class="linha"><span>Caixa ${t.numeroCaixa}</span><span>${esc(t.operador)}</span></div>
        <div class="linha"><span>Motivo</span><span>${esc(rotuloMotivo(t.motivo))}</span></div>
        ${itens}
        <div class="linha forte"><span>CRÉDITO</span><span>${dinheiro(t.valor)}</span></div>
        <div class="assinatura">Cliente</div>
        <div class="centro pequeno">NÃO É DOCUMENTO FISCAL</div>
      </section>`;
    return this.envelope(`Vale-troca ${v.codigo}`, corpo);
  }

  // Comprovante de sangria (dinheiro que sai da gaveta para o cofre) ou suprimento (troco que entra).
  htmlMovimento(tipo: 'sangria' | 'suprimento', valor: number, motivo: string, caixa: ResumoCaixa, loja: string): string {
    const corpo = `
      <section class="via">
        <div class="centro forte grande">${esc(loja)}</div>
        <div class="centro forte">${tipo === 'sangria' ? 'SANGRIA (retirada de dinheiro)' : 'SUPRIMENTO (entrada de troco)'}</div>
        <div class="tracos"></div>
        <div class="linha"><span>Caixa ${caixa.numeroCaixa}</span><span>${esc(caixa.operador)}</span></div>
        <div class="linha"><span>Data</span><span>${dataHora(new Date().toISOString())}</span></div>
        <div class="tracos"></div>
        <div class="linha forte grande"><span>VALOR R$</span><span>${dinheiro(valor)}</span></div>
        <div>Motivo: ${esc(motivo || '—')}</div>
        <div class="assinatura">Operador do caixa</div>
        <div class="assinatura">${tipo === 'sangria' ? 'Recebido por' : 'Entregue por'}</div>
      </section>`;
    return this.envelope(tipo === 'sangria' ? 'Sangria' : 'Suprimento', corpo);
  }

  // A "bobina": largura, fonte de cupom, margens zeradas. Todos os documentos usam.
  private envelope(titulo: string, corpo: string): string {
    const cfg = this.config();
    const largura = cfg.largura === 58 ? 48 : 72; // área que a cabeça de impressão alcança (mm)
    return `<!doctype html><html><head><meta charset="utf-8"><title>${esc(titulo)}</title><style>
      @page { size: ${cfg.largura}mm auto; margin: 0; }
      * { box-sizing: border-box; }
      body { margin: 0; color: #000; background: #fff; }
      .via { width: ${largura}mm; margin: 0 auto; padding: 2mm 0 6mm; font: ${cfg.largura === 58 ? 10 : 11.5}px/1.3 'Courier New', monospace; }
      .via + .via { break-before: page; }
      .linha { display: flex; justify-content: space-between; gap: 2mm; }
      .linha span:last-child { white-space: nowrap; }
      .item span { display: block; overflow-wrap: anywhere; }
      .centro { text-align: center; }
      .forte { font-weight: 700; }
      .grande { font-size: 1.25em; }
      .pequeno { font-size: 0.85em; margin-top: 1mm; }
      .tracos { border-top: 1px dashed #000; margin: 1.5mm 0; }
      .assinatura { margin-top: 10mm; border-top: 1px solid #000; text-align: center; font-size: 0.9em; padding-top: 0.5mm; }
    </style></head><body>${corpo}</body></html>`;
  }

  // Imprime um HTML num quadro invisível (a tela do caixa não muda).
  imprimirHtml(html: string): void {
    const quadro = document.createElement('iframe');
    quadro.setAttribute('aria-hidden', 'true');
    Object.assign(quadro.style, { position: 'fixed', right: '0', bottom: '0', width: '0', height: '0', border: '0' });
    document.body.appendChild(quadro);
    const doc = quadro.contentDocument!;
    doc.open();
    doc.write(html);
    doc.close();
    quadro.contentWindow!.focus();
    setTimeout(() => {
      quadro.contentWindow!.print();
      setTimeout(() => quadro.remove(), 1000);
    }, 50);
  }

  private carregar(): ConfigImpressora {
    try {
      return { ...PADRAO, ...JSON.parse(localStorage.getItem(CHAVE) ?? localStorage.getItem(CHAVE_ANTIGA) ?? '{}') };
    } catch {
      return PADRAO;
    }
  }
}

function dinheiro(v: number): string {
  return v.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function dataHora(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

// "2026-11-10" → "10/11/2026" (sem passar por Date: o dia não "volta" em UTC).
function diaBr(iso: string): string {
  const [a, m, d] = iso.split('-');
  return `${d}/${m}/${a}`;
}

function rotuloForma(forma: string): string {
  return FORMAS_PAGAMENTO.find((f) => f.valor === forma)?.rotulo ?? forma;
}

function esc(texto: string): string {
  return texto.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
}

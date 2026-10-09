import { Injectable, computed, signal } from '@angular/core';
import { ENQ, LeituraBalanca, interpretarResposta } from './protocolo-balanca';

// Balança de CAIXA ligada ao computador (USB-serial ou serial), lida pelo navegador com a Web Serial API
// (Chrome e Edge, em endereço seguro: https://app.wnlabs.com.br ou localhost).
// "Simulada" = sem aparelho: devolve o peso configurado (para treinar e testar).
// A configuração fica NESTE computador (cada caixa tem a sua balança).

export type ModoBalanca = 'nenhuma' | 'serial' | 'simulada';

interface ConfigBalanca {
  modo: ModoBalanca;
  velocidade: number; // baud rate: 2400, 4800 ou 9600 (está no manual / menu da balança)
  pesoSimulado: number;
}

const CHAVE = 'marketplace.balanca';
const PADRAO: ConfigBalanca = { modo: 'nenhuma', velocidade: 9600, pesoSimulado: 0.75 };

// A Web Serial ainda não está nos tipos padrão do TypeScript: só o que usamos.
interface PortaSerial {
  open(opcoes: { baudRate: number; dataBits?: number; stopBits?: number; parity?: string }): Promise<void>;
  close(): Promise<void>;
  readable: ReadableStream<Uint8Array> | null;
  writable: WritableStream<Uint8Array> | null;
}
interface SerialNavegador {
  requestPort(): Promise<PortaSerial>;
  getPorts(): Promise<PortaSerial[]>;
}

@Injectable({ providedIn: 'root' })
export class BalancaService {
  readonly config = signal<ConfigBalanca>(this.carregar());
  readonly conectada = signal(false);
  readonly ultimoErro = signal<string | null>(null);
  private porta: PortaSerial | null = null;

  // A porta serial só funciona em navegador compatível e endereço seguro.
  readonly serialDisponivel = 'serial' in navigator && window.isSecureContext;
  readonly ativa = computed(() => this.config().modo === 'simulada' || (this.config().modo === 'serial' && this.conectada()));

  constructor() {
    // Porta já autorizada antes neste computador: reconecta sozinha ao abrir o sistema.
    if (this.config().modo === 'serial' && this.serialDisponivel) void this.reconectar();
  }

  salvarConfig(mudancas: Partial<ConfigBalanca>): void {
    const nova = { ...this.config(), ...mudancas };
    this.config.set(nova);
    try {
      localStorage.setItem(CHAVE, JSON.stringify(nova));
    } catch {
      /* navegador sem armazenamento: vale só até fechar */
    }
  }

  // Precisa ser chamado num clique (o navegador pergunta qual porta usar).
  async escolherPorta(): Promise<void> {
    this.ultimoErro.set(null);
    const serial = (navigator as unknown as { serial: SerialNavegador }).serial;
    try {
      await this.fechar();
      this.porta = await serial.requestPort();
      await this.abrir();
      this.salvarConfig({ modo: 'serial' });
    } catch (e) {
      this.ultimoErro.set(e instanceof Error && e.name === 'NotFoundError' ? 'Nenhuma porta escolhida.' : `Não foi possível abrir a porta: ${e}`);
    }
  }

  async desconectar(): Promise<void> {
    await this.fechar();
    this.salvarConfig({ modo: 'nenhuma' });
  }

  // Lê o peso. Se a balança disser "mexendo" (instável), tenta de novo algumas vezes.
  async lerPeso(tentativas = 6): Promise<LeituraBalanca> {
    const c = this.config();
    if (c.modo === 'simulada') {
      await new Promise((r) => setTimeout(r, 250));
      return c.pesoSimulado > 0 ? { tipo: 'peso', kg: c.pesoSimulado } : { tipo: 'peso', kg: 0 };
    }
    if (c.modo !== 'serial' || !this.porta) return { tipo: 'invalido', recebido: 'balança desligada' };

    let ultima: LeituraBalanca = { tipo: 'incompleto' };
    for (let i = 0; i < tentativas; i++) {
      ultima = await this.perguntar();
      if (ultima.tipo !== 'instavel' && ultima.tipo !== 'incompleto') return ultima;
      await new Promise((r) => setTimeout(r, 350));
    }
    return ultima;
  }

  // Manda ENQ e espera a resposta (até 1,5 s).
  private async perguntar(): Promise<LeituraBalanca> {
    const porta = this.porta!;
    if (!porta.writable || !porta.readable) return { tipo: 'invalido', recebido: 'porta fechada' };
    const escritor = porta.writable.getWriter();
    await escritor.write(new Uint8Array([ENQ]));
    escritor.releaseLock();

    const leitor = porta.readable.getReader();
    const recebido: number[] = [];
    const limite = setTimeout(() => void leitor.cancel(), 1500);
    try {
      while (true) {
        const { value, done } = await leitor.read();
        if (done) break;
        if (value) recebido.push(...value);
        const r = interpretarResposta(new Uint8Array(recebido));
        if (r.tipo !== 'incompleto') return r;
      }
      return { tipo: 'incompleto' };
    } catch {
      return { tipo: 'incompleto' };
    } finally {
      clearTimeout(limite);
      leitor.releaseLock();
    }
  }

  private async reconectar(): Promise<void> {
    const serial = (navigator as unknown as { serial: SerialNavegador }).serial;
    const portas = await serial.getPorts();
    if (!portas.length) return;
    this.porta = portas[0];
    try {
      await this.abrir();
    } catch {
      this.porta = null;
    }
  }

  private async abrir(): Promise<void> {
    await this.porta!.open({ baudRate: this.config().velocidade, dataBits: 8, stopBits: 1, parity: 'none' });
    this.conectada.set(true);
  }

  private async fechar(): Promise<void> {
    if (this.porta) {
      try {
        await this.porta.close();
      } catch {
        /* já estava fechada */
      }
    }
    this.porta = null;
    this.conectada.set(false);
  }

  private carregar(): ConfigBalanca {
    try {
      return { ...PADRAO, ...JSON.parse(localStorage.getItem(CHAVE) ?? '{}') };
    } catch {
      return PADRAO;
    }
  }
}

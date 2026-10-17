import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';

// O erro é "falta de internet" (e não um "não" do servidor)? Status 0 = a requisição nem saiu/voltou;
// 502/503/504/52x/530 = o servidor (ou o Cloudflare na frente dele) não respondeu. Nesses casos o caixa segue offline.
export function semConexao(e: unknown): boolean {
  if (!(e instanceof HttpErrorResponse)) return e instanceof TypeError; // fetch que falhou na rede
  return e.status === 0 || e.status === 502 || e.status === 503 || e.status === 504 || (e.status >= 520 && e.status <= 530);
}

// Está com internet? Começa pelo que o navegador diz e se corrige a cada pedido que falha ou dá certo.
// Sem internet, pergunta ao servidor (/api/saude) a cada 10 s para perceber quando ela voltou.
@Injectable({ providedIn: 'root' })
export class Conexao {
  readonly online = signal(navigator.onLine);
  private teste?: ReturnType<typeof setInterval>;
  private readonly aoVoltar: (() => void)[] = [];

  constructor() {
    window.addEventListener('online', () => void this.conferir());
    window.addEventListener('offline', () => this.caiu());
    if (!navigator.onLine) this.caiu();
  }

  // Avise quando a internet voltar (o caixa envia as vendas guardadas).
  quandoVoltar(acao: () => void): void {
    this.aoVoltar.push(acao);
  }

  caiu(): void {
    if (this.online()) this.online.set(false);
    this.teste ??= setInterval(() => void this.conferir(), 10_000);
  }

  voltou(): void {
    clearInterval(this.teste);
    this.teste = undefined;
    if (this.online()) return;
    this.online.set(true);
    for (const acao of this.aoVoltar) acao();
  }

  // Um pedido falhou: se foi por falta de internet, entra no modo offline. Devolve true nesse caso.
  falhou(e: unknown): boolean {
    if (!semConexao(e)) return false;
    this.caiu();
    return true;
  }

  async conferir(): Promise<boolean> {
    try {
      const r = await fetch('/api/saude', { cache: 'no-store' });
      if (r.ok) { this.voltou(); return true; }
    } catch { /* continua sem internet */ }
    this.caiu();
    return false;
  }
}

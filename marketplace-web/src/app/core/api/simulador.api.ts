import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface SimuladorStatus {
  disponivel: boolean; // esta empresa tem vendas automáticas
  numeroCaixa: number; // o caixa que o robô usa
  ativo: boolean;
  intensidade: number;
  caixaAberto: boolean;
  ultimaRodada: string | null;
  ultimoErro: string | null;
  temperatura: number | null;
  tempo: string | null;
  cidade: string | null; // cidade da loja (de onde vem o clima)
  climaReal: boolean;
  vendasHoje: number;
  faturamentoHoje: number;
  caixaComOutro: string | null; // uma pessoa está com o caixa do robô aberto: o robô espera
  caixaComOutroDesde: string | null;
}

@Injectable({ providedIn: 'root' })
export class SimuladorApi {
  private readonly http = inject(HttpClient);

  pausar(): Promise<SimuladorStatus> {
    return firstValueFrom(this.http.post<SimuladorStatus>('/api/simulador/pausar', {}));
  }

  retomar(): Promise<SimuladorStatus> {
    return firstValueFrom(this.http.post<SimuladorStatus>('/api/simulador/retomar', {}));
  }

  atenderAgora(quantidade: number): Promise<{ clientes: number; vendasFeitas: number }> {
    return firstValueFrom(this.http.post<{ clientes: number; vendasFeitas: number }>(
      `/api/simulador/clientes?quantidade=${quantidade}`, {}));
  }
}

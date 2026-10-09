import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface SimuladorStatus {
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

import { Component, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ImpressoraService } from '../../core/equipamentos/impressora.service';
import { RelatorioTrocas, Troca, rotuloMotivo } from '../../core/api/trocas.api';
import { MARCA } from '../../shared/marca';
import { CampoData } from '../../shared/campo-data';
import { Icone } from '../../shared/icone';
import { hojeIso } from '../plataforma/comum';

// Vendas → Trocas e vales: as trocas do período (motivos, produtos mais trocados) e os vales-troca em aberto
// (o crédito que os clientes ainda têm na loja). A troca em si é feita no caixa (F7).
@Component({
  selector: 'app-trocas',
  imports: [FormsModule, CurrencyPipe, DatePipe, DecimalPipe, RouterLink, CampoData, Icone],
  templateUrl: './trocas.html',
  styleUrl: './trocas.scss',
})
export class Trocas {
  private readonly auth = inject(AuthService);
  private readonly impressora = inject(ImpressoraService);

  protected readonly ate = signal(hojeIso());
  protected readonly de = signal(this.diasAntes(hojeIso(), 29));
  protected readonly relatorio = httpResource<RelatorioTrocas>(() => ({ url: '/api/trocas', params: { de: this.de(), ate: this.ate() } }));
  protected readonly rotuloMotivo = rotuloMotivo;

  protected imprimirVale(t: Troca): void {
    this.impressora.imprimirHtml(this.impressora.htmlValeTroca(t, this.auth.usuario()?.empresa.nome ?? MARCA.nome));
  }

  protected diaBr(iso: string): string {
    const [a, m, d] = iso.slice(0, 10).split('-');
    return `${d}/${m}/${a}`;
  }

  protected situacao(s: string): string {
    return s === 'Disponivel' ? 'disponível' : s === 'Usado' ? 'usado' : 'vencido';
  }

  private diasAntes(iso: string, dias: number): string {
    const d = new Date(`${iso}T12:00:00`);
    d.setDate(d.getDate() - dias);
    return d.toLocaleDateString('sv-SE');
  }
}

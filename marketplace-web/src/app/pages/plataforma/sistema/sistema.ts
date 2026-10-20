import { Component } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { httpResource } from '@angular/common/http';
import { Icone } from '../../../shared/icone';
import { API } from '../comum';

interface Sistema {
  ligadoDesde: string; versao: string; memoriaMb: number; banco: string; bancoMb: number; discoLivreGb: number; discoTotalGb: number;
  enderecos: { endereco: string; noAr: boolean; ms: number | null; erro: string | null }[];
  backup: { ultimoLocal: string | null; ultimoLocalKb: number | null; copiasLocais: number; nuvem: string | null; nuvemOk: boolean; continuo: string | null; continuoOk: boolean };
  usoPorCliente: { id: number; nome: string; registros: number; percentual: number }[];
  rotinas: { nome: string; situacao: string }[];
}

// Área do dono → Sistema: saúde da plataforma e quanto do banco cada cliente ocupa.
@Component({
  selector: 'app-sistema',
  imports: [Icone, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './sistema.html',
  styleUrl: './sistema.scss',
})
export class SistemaPagina {
  protected readonly dados = httpResource<Sistema>(() => `${API}/sistema`);

  // "2026-10-12" (nome da pasta do backup) → "12/10/2026"
  protected dataPasta(p: string | null): string {
    if (!p) return 'nenhum';
    const [a, m, d] = p.split('-');
    return `${d}/${m}/${a}`;
  }
}

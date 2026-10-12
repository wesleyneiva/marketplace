import { Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Icone } from '../icone';
import { FeriadoLocal, Horario } from './horario';

// Os campos do horário de funcionamento (dias da semana, regra dos feriados, feriados da cidade). Edita o objeto
// recebido "no lugar"; quem usa decide quando salvar. Usado em Configurações (loja) e na ficha do cliente (dono).
@Component({
  selector: 'app-horario-editor',
  imports: [FormsModule, Icone],
  templateUrl: './horario-editor.html',
  styleUrl: './horario-editor.scss',
})
export class HorarioEditor {
  readonly horario = input.required<Horario>();
  readonly podeEditar = input(true);

  // Linhas na ordem da semana do comércio (segunda primeiro); "indice" é o do servidor (0 = domingo).
  protected readonly diasSemana = [
    { indice: 1, nome: 'Segunda' }, { indice: 2, nome: 'Terça' }, { indice: 3, nome: 'Quarta' }, { indice: 4, nome: 'Quinta' },
    { indice: 5, nome: 'Sexta' }, { indice: 6, nome: 'Sábado' }, { indice: 0, nome: 'Domingo' },
  ];
  protected readonly meses = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];
  protected novoFeriado: FeriadoLocal = { dia: 1, mes: 1, nome: '' };

  // Segunda-feira vale para todos os dias abertos (o caso mais comum: "todo dia das 8 às 21").
  copiarSegunda(): void {
    const h = this.horario();
    const seg = h.dias[1];
    h.dias.forEach((d, i) => { if (i !== 1 && d.aberto) { d.abre = seg.abre; d.fecha = seg.fecha; } });
  }

  adicionarFeriado(): void {
    const h = this.horario();
    const f = this.novoFeriado;
    if (!f.nome.trim()) return;
    h.feriadosLocais = [...h.feriadosLocais.filter((x) => !(x.dia === f.dia && x.mes === f.mes)), { ...f, nome: f.nome.trim() }]
      .sort((a, b) => a.mes - b.mes || a.dia - b.dia);
    this.novoFeriado = { dia: 1, mes: 1, nome: '' };
  }

  removerFeriado(f: FeriadoLocal): void {
    const h = this.horario();
    h.feriadosLocais = h.feriadosLocais.filter((x) => x !== f);
  }

  protected diasDoMes(mes: number): number[] {
    return Array.from({ length: new Date(2024, mes, 0).getDate() }, (_, i) => i + 1);
  }
}

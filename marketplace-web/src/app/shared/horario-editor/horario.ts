// Horário de funcionamento (mesmo formato do Models/HorarioFuncionamento.cs). O servidor manda as horas como
// "08:00:00"; os campos da tela trabalham com "08:00".
export interface HorarioDia { aberto: boolean; abre: string; fecha: string }
export interface FeriadoLocal { dia: number; mes: number; nome: string }
export interface Horario { dias: HorarioDia[]; feriados: 'Fechado' | 'Domingo' | 'Normal'; feriadosLocais: FeriadoLocal[] }

// Cópia editável, com as horas no formato do campo ("08:00").
export function horarioParaTela(h: Horario): Horario {
  return {
    feriados: h.feriados,
    feriadosLocais: h.feriadosLocais.map((f) => ({ ...f })),
    dias: h.dias.map((d) => ({ aberto: d.aberto, abre: d.abre.slice(0, 5), fecha: d.fecha.slice(0, 5) })),
  };
}

// O que vai para o servidor ("08:00" → "08:00:00").
export function horarioParaServidor(h: Horario): Horario {
  return { ...h, dias: h.dias.map((d) => ({ aberto: d.aberto, abre: `${d.abre.slice(0, 5)}:00`, fecha: `${d.fecha.slice(0, 5)}:00` })) };
}

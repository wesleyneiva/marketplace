// Protocolo das balanças de caixa Toledo / Filizola / Elgin / Urano ("protocolo Toledo"): o PDV manda ENQ (0x05)
// e a balança responde  STX (0x02) + peso em GRAMAS (5 ou 6 dígitos) + ETX (0x03).  Ex.: STX "01235" ETX = 1,235 kg.
// Quando o peso ainda está mexendo, vem "IIIII"; sem nada em cima, "00000"; peso negativo, "NNNNN"; acima do limite, "SSSSS".
// Sem dependência do navegador: dá para testar com qualquer sequência de bytes.

export const ENQ = 0x05;
export const STX = 0x02;
export const ETX = 0x03;

export type LeituraBalanca =
  | { tipo: 'peso'; kg: number }
  | { tipo: 'instavel' }
  | { tipo: 'negativo' }
  | { tipo: 'sobrecarga' }
  | { tipo: 'incompleto' } // ainda não chegou o ETX: continue lendo
  | { tipo: 'invalido'; recebido: string };

export function interpretarResposta(bytes: Uint8Array): LeituraBalanca {
  const fim = bytes.lastIndexOf(ETX);
  if (fim < 0) return { tipo: 'incompleto' };
  const inicio = bytes.lastIndexOf(STX, fim);
  const conteudo = String.fromCharCode(...bytes.slice(inicio < 0 ? 0 : inicio + 1, fim)).trim();

  if (/^I+$/i.test(conteudo)) return { tipo: 'instavel' };
  if (/^N+$/i.test(conteudo)) return { tipo: 'negativo' };
  if (/^S+$/i.test(conteudo)) return { tipo: 'sobrecarga' };
  // Algumas balanças mandam com ponto ("1.235") em vez de gramas.
  if (/^\d+[.,]\d+$/.test(conteudo)) return { tipo: 'peso', kg: Number(conteudo.replace(',', '.')) };
  if (/^\d{4,6}$/.test(conteudo)) return { tipo: 'peso', kg: Number(conteudo) / 1000 };
  return { tipo: 'invalido', recebido: conteudo };
}

// Copia um texto para a área de transferência. O jeito moderno (navigator.clipboard) só funciona em endereço
// seguro (https / localhost); pelo http://oracle-a1:5100 ele nem existe. Nesse caso usa o jeito antigo:
// um campo de texto escondido, selecionado, e o comando "copiar" do navegador.
export async function copiarTexto(texto: string): Promise<boolean> {
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(texto);
      return true;
    } catch {
      /* sem permissão: tenta o jeito antigo */
    }
  }
  const campo = document.createElement('textarea');
  campo.value = texto;
  campo.setAttribute('readonly', '');
  Object.assign(campo.style, { position: 'fixed', top: '0', left: '0', opacity: '0' });
  // Dentro de um <dialog> aberto, o campo precisa ficar nele (fora, o navegador não deixa selecionar).
  (document.querySelector('dialog[open]') ?? document.body).appendChild(campo);
  campo.select();
  campo.setSelectionRange(0, texto.length);
  let ok = false;
  try {
    ok = document.execCommand('copy');
  } catch {
    ok = false;
  }
  campo.remove();
  return ok;
}

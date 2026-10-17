import { Component } from '@angular/core';

// Pé de todas as telas (casca, login, troca de senha): quem fez e como falar com a gente.
@Component({
  selector: 'app-rodape',
  template: `<footer class="rodape">Desenvolvido por <strong>WN Labs</strong> · <a href="mailto:contato@wnlabs.com.br">contato&#64;wnlabs.com.br</a></footer>`,
  styles: `
    .rodape { padding: 14px 16px; text-align: center; font-size: 13px; color: var(--cor-texto-suave); }
    strong { font-weight: 600; color: var(--cor-texto); }
    a { color: inherit; text-decoration: none; }
    a:hover { color: var(--cor-primaria); text-decoration: underline; }
  `,
})
export class Rodape {}

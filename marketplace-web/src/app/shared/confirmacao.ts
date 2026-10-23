import { Component, ElementRef, Injectable, effect, inject, signal, viewChild } from '@angular/core';
import { Icone } from './icone';

// Pergunta de confirmação no padrão das janelas do sistema (no lugar do confirm() do navegador, que mostra
// "app.wnlabs.com.br diz" e ignora o tema). Uso: if (!(await this.confirmacao.pedir('Excluir X?', { botao: 'Excluir', perigo: true }))) return;
// O texto até o primeiro "?" vira o título; o resto (cada linha um parágrafo) vira a explicação.
export interface OpcoesConfirmacao {
  botao?: string;   // texto do botão de confirmar (padrão "Confirmar")
  perigo?: boolean; // ação que apaga, desativa ou cancela: botão vermelho
}

interface Pedido extends OpcoesConfirmacao {
  titulo: string;
  paragrafos: string[];
  responder: (ok: boolean) => void;
}

@Injectable({ providedIn: 'root' })
export class Confirmacao {
  readonly pedido = signal<Pedido | null>(null);

  pedir(texto: string, opcoes: OpcoesConfirmacao = {}): Promise<boolean> {
    this.pedido()?.responder(false); // uma pergunta por vez: a anterior conta como "Cancelar"
    const fim = texto.indexOf('?');
    const titulo = (fim >= 0 ? texto.slice(0, fim + 1) : texto).trim();
    const paragrafos = (fim >= 0 ? texto.slice(fim + 1) : '').split('\n').map((p) => p.trim()).filter((p) => p.length > 0);
    return new Promise((resolve) =>
      this.pedido.set({ ...opcoes, titulo, paragrafos, responder: (ok) => { this.pedido.set(null); resolve(ok); } }));
  }
}

// Fica uma vez só, no componente raiz (app.ts): abre sempre que alguém chama Confirmacao.pedir.
@Component({
  selector: 'app-confirmacao',
  imports: [Icone],
  template: `
    <dialog #janela class="janela" (cancel)="$event.preventDefault(); responder(false)" (click)="clicouFora($event)">
      @if (confirmacao.pedido(); as p) {
        <header>
          <h2><app-icone [nome]="p.perigo ? 'triangle-alert' : 'circle-alert'" [class.perigo]="p.perigo" /> {{ p.titulo }}</h2>
          <button type="button" class="fechar" (click)="responder(false)" aria-label="Fechar"><app-icone nome="x" /></button>
        </header>
        @if (p.paragrafos.length) {
          <div class="corpo">
            @for (t of p.paragrafos; track $index) { <p>{{ t }}</p> }
          </div>
        }
        <footer>
          <button type="button" class="botao" (click)="responder(false)">Cancelar</button>
          <button #confirmar type="button" class="botao" [class.primario]="!p.perigo" [class.perigo-forte]="p.perigo" (click)="responder(true)">
            {{ p.botao ?? 'Confirmar' }}</button>
        </footer>
      }
    </dialog>
  `,
  styles: `
    .janela {
      width: min(440px, calc(100vw - 32px)); padding: 0; border: 0; border-radius: 14px;
      background: var(--cor-superficie); color: var(--cor-texto); box-shadow: 0 20px 60px rgb(0 0 0 / 0.25);
      &::backdrop { background: rgb(10 20 15 / 0.5); }
    }
    header, footer { display: flex; align-items: center; gap: 10px; padding: 14px 18px; }
    header { justify-content: space-between; align-items: flex-start; border-bottom: 1px solid var(--cor-borda); }
    h2 { margin: 0; font-size: 17px; line-height: 1.35; display: flex; gap: 8px; align-items: flex-start; }
    h2 app-icone { flex: none; margin-top: 2px; color: var(--cor-primaria); }
    h2 app-icone.perigo { color: var(--cor-erro); }
    .fechar { border: 0; background: transparent; font-size: 18px; cursor: pointer; color: var(--cor-texto-suave); padding: 2px; }
    .corpo { padding: 14px 18px; display: flex; flex-direction: column; gap: 8px; color: var(--cor-texto-suave); font-size: 14px; }
    .corpo p { margin: 0; }
    footer { justify-content: flex-end; border-top: 1px solid var(--cor-borda); }
    .perigo-forte { background: var(--cor-erro); border-color: var(--cor-erro); color: #fff; font-weight: 600; }
    .perigo-forte:hover:not(:disabled) { filter: brightness(0.92); border-color: var(--cor-erro); color: #fff; }
  `,
})
export class ConfirmacaoJanela {
  protected readonly confirmacao = inject(Confirmacao);
  private readonly janela = viewChild.required<ElementRef<HTMLDialogElement>>('janela');
  private readonly botaoConfirmar = viewChild<ElementRef<HTMLButtonElement>>('confirmar');

  constructor() {
    effect(() => {
      const d = this.janela().nativeElement;
      if (this.confirmacao.pedido()) {
        if (!d.open) d.showModal();
        queueMicrotask(() => this.botaoConfirmar()?.nativeElement.focus()); // Enter confirma, Esc cancela
      } else if (d.open) d.close();
    });
  }

  protected responder(ok: boolean): void {
    this.confirmacao.pedido()?.responder(ok);
  }

  // Clique no fundo escuro (fora da caixa) = Cancelar.
  protected clicouFora(e: MouseEvent): void {
    if (e.target === this.janela().nativeElement) this.responder(false);
  }
}

import { Injectable, computed, effect, signal } from '@angular/core';

export type PreferenciaTema = 'claro' | 'escuro' | 'automatico';
const CHAVE = 'marcus.tema';

// Modo claro / escuro. "Automático" segue o computador ou celular (Windows, Android, iPhone).
// A escolha fica guardada NESTE navegador (cada computador do mercado pode ter a sua).
// O index.html aplica o tema antes da página aparecer; aqui ele muda na hora pelo botão do topo.
@Injectable({ providedIn: 'root' })
export class TemaService {
  private readonly sistema = matchMedia('(prefers-color-scheme: dark)');
  private readonly sistemaEscuro = signal(this.sistema.matches);

  readonly preferencia = signal<PreferenciaTema>(this.lerPreferencia());
  readonly escuro = computed(() => this.preferencia() === 'escuro' || (this.preferencia() === 'automatico' && this.sistemaEscuro()));

  constructor() {
    this.sistema.addEventListener('change', (e) => this.sistemaEscuro.set(e.matches));
    effect(() => {
      document.documentElement.setAttribute('data-tema', this.escuro() ? 'escuro' : 'claro');
      try { localStorage.setItem(CHAVE, this.preferencia()); } catch { /* navegador sem armazenamento: vale só agora */ }
    });
  }

  // Claro → Escuro → Automático → Claro…
  alternar(): void {
    const ordem: PreferenciaTema[] = ['claro', 'escuro', 'automatico'];
    this.preferencia.set(ordem[(ordem.indexOf(this.preferencia()) + 1) % ordem.length]);
  }

  private lerPreferencia(): PreferenciaTema {
    try {
      const v = localStorage.getItem(CHAVE);
      return v === 'claro' || v === 'escuro' ? v : 'automatico';
    } catch {
      return 'automatico';
    }
  }
}

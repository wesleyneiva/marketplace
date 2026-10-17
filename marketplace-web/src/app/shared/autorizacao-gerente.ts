import { Component, ElementRef, effect, inject, input, model, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Autorizacao, Gerente, PdvApi } from '../core/api/pdv.api';
import { Icone } from './icone';

// "Chama o gerente!": motivo + o gerente escolhe o nome dele e digita a PRÓPRIA senha no caixa.
// Se quem está logado já é gerente/administrador ([precisa]=false), só o motivo (fica registrado em nome dele).
// O servidor é quem confere a senha (e bloqueia a conta depois de 5 erros). Quem usa lê autorizacao() e motivo().
@Component({
  selector: 'app-autorizacao-gerente',
  imports: [FormsModule, Icone],
  template: `
    <label class="motivo">Motivo
      <input #campoMotivo [ngModel]="motivo()" (ngModelChange)="motivo.set($event)" [ngModelOptions]="{ standalone: true }"
             maxlength="200" placeholder="Por que está cancelando?" autocomplete="off" />
    </label>
    @if (sugestoes().length) {
      <div class="sugestoes">
        @for (s of sugestoes(); track s) {
          <button type="button" class="botao pequeno" [class.ligado]="motivo() === s" (click)="escolherMotivo(s)">{{ s }}</button>
        }
      </div>
    }
    @if (precisa()) {
      <fieldset class="gerente">
        <legend><app-icone nome="key-round" /> Autorização do gerente</legend>
        @if (gerentes().length) {
          <div class="campos">
            <select [ngModel]="gerenteId()" (ngModelChange)="gerenteId.set($event)" [ngModelOptions]="{ standalone: true }" aria-label="Gerente">
              <option value="" disabled>Escolha o gerente</option>
              @for (g of gerentes(); track g.id) { <option [value]="g.id">{{ g.nome }}</option> }
            </select>
            <input #campoSenha type="password" [ngModel]="senha()" (ngModelChange)="senha.set($event)" [ngModelOptions]="{ standalone: true }"
                   placeholder="Senha do gerente" autocomplete="new-password" aria-label="Senha do gerente" />
          </div>
          <small>O gerente digita a própria senha (a mesma do login).</small>
        } @else if (carregou()) {
          <small class="ruim">Nenhum gerente cadastrado: o administrador cria em Usuários.</small>
        } @else {
          <small>Carregando os gerentes…</small>
        }
      </fieldset>
    } @else {
      <p class="eu"><app-icone nome="check" /> Você é gerente: o cancelamento fica registrado em seu nome.</p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 10px; }
    .motivo { display: flex; flex-direction: column; gap: 6px; font-weight: 600; font-size: 14px; }
    .sugestoes { display: flex; flex-wrap: wrap; gap: 6px; }
    .gerente {
      margin: 0; padding: 10px 12px 12px; border: 1px solid var(--cor-alerta-borda); border-radius: 10px;
      background: var(--cor-alerta-fundo); display: flex; flex-direction: column; gap: 8px;
    }
    legend { padding: 0 4px; font-weight: 700; font-size: 14px; color: var(--cor-alerta); display: inline-flex; align-items: center; gap: 6px; }
    .campos { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .campos > * { min-width: 0; }
    small { color: var(--cor-texto-suave); font-size: 12px; }
    .ruim { color: var(--cor-erro); }
    .eu { margin: 0; font-size: 14px; color: var(--cor-primaria-escura); display: flex; align-items: center; gap: 6px; }
    @media (max-width: 480px) { .campos { grid-template-columns: 1fr; } }
  `,
})
export class AutorizacaoGerente {
  private readonly api = inject(PdvApi);

  readonly precisa = input(true);                 // quem está logado NÃO é gerente
  readonly sugestoes = input<string[]>([]);       // motivos comuns (um clique)
  readonly motivo = model('');

  protected readonly gerentes = signal<Gerente[]>([]);
  protected readonly carregou = signal(false);
  protected readonly gerenteId = signal('');
  protected readonly senha = signal('');
  private readonly campoMotivo = viewChild<ElementRef<HTMLInputElement>>('campoMotivo');
  private readonly campoSenha = viewChild<ElementRef<HTMLInputElement>>('campoSenha');

  constructor() {
    effect(() => {
      if (this.precisa() && !this.carregou()) void this.carregar();
    });
  }

  private async carregar(): Promise<void> {
    try {
      const lista = await this.api.gerentes();
      this.gerentes.set(lista);
      if (lista.length === 1) this.gerenteId.set(lista[0].id); // um gerente só: já vem escolhido
    } catch {
      this.gerentes.set([]);
    } finally {
      this.carregou.set(true);
    }
  }

  autorizacao(): Autorizacao | null {
    return this.precisa() ? { gerenteId: this.gerenteId(), senha: this.senha() } : null;
  }

  // O que falta antes de mandar ao servidor (null = pronto).
  falta(): string | null {
    if (this.motivo().trim().length < 3) return 'Escreva o motivo (pelo menos 3 letras).';
    if (this.precisa() && !this.gerenteId()) return 'Escolha o gerente que está autorizando.';
    if (this.precisa() && !this.senha()) return 'O gerente precisa digitar a senha.';
    return null;
  }

  // Depois de um erro de senha: limpa e volta o cursor para a senha.
  senhaErrada(): void {
    this.senha.set('');
    setTimeout(() => this.campoSenha()?.nativeElement.focus());
  }

  limpar(motivo = ''): void {
    this.motivo.set(motivo);
    this.senha.set('');
  }

  focar(): void {
    setTimeout(() => (this.motivo() ? this.campoSenha() ?? this.campoMotivo() : this.campoMotivo())?.nativeElement.focus());
  }

  protected escolherMotivo(s: string): void {
    this.motivo.set(s);
    if (this.precisa()) setTimeout(() => this.campoSenha()?.nativeElement.focus());
  }
}

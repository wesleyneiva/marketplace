import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Icone } from '../../../shared/icone';
import { API, Plano, mensagemErro } from '../comum';

// Planos que o dono vende. Mensalidade = preço + caixas além dos incluídos × preço do caixa extra.
@Component({
  selector: 'app-planos',
  imports: [Icone, FormsModule, CurrencyPipe],
  templateUrl: './planos.html',
  styleUrl: './planos.scss',
})
export class Planos {
  private readonly http = inject(HttpClient);
  protected readonly planos = httpResource<Plano[]>(() => `${API}/planos`);

  private readonly dlg = viewChild<ElementRef<HTMLDialogElement>>('dlg');
  protected editando: Plano | null = null;
  protected form = { nome: '', descricao: '', precoMensal: 149, caixasIncluidos: 1, precoCaixaExtra: 49, ativo: true };
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);

  abrir(p: Plano | null): void {
    this.editando = p;
    this.form = p
      ? { nome: p.nome, descricao: p.descricao ?? '', precoMensal: p.precoMensal, caixasIncluidos: p.caixasIncluidos, precoCaixaExtra: p.precoCaixaExtra, ativo: p.ativo }
      : { nome: '', descricao: '', precoMensal: 149, caixasIncluidos: 1, precoCaixaExtra: 49, ativo: true };
    this.erro.set(null);
    this.dlg()?.nativeElement.showModal();
  }

  // Exemplos de mensalidade com o formulário atual (1, 2 e 3 caixas).
  protected exemplo(caixas: number): number {
    return this.form.precoMensal + Math.max(0, caixas - this.form.caixasIncluidos) * this.form.precoCaixaExtra;
  }

  async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const url = this.editando ? `${API}/planos/${this.editando.id}` : `${API}/planos`;
      await firstValueFrom(this.editando ? this.http.put(url, this.form) : this.http.post(url, this.form));
      this.aviso.set(this.editando ? `Plano "${this.form.nome}" salvo. Vale para as próximas mensalidades.` : `Plano "${this.form.nome}" criado.`);
      this.dlg()?.nativeElement.close();
      this.planos.reload();
    } catch (e) {
      this.erro.set(mensagemErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}

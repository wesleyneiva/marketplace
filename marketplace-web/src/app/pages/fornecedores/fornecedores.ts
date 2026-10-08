import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Fornecedor } from '../../core/api/compras.api';

@Component({
  selector: 'app-fornecedores',
  imports: [FormsModule, RouterLink],
  templateUrl: './fornecedores.html',
  styleUrl: '../usuarios/usuarios.scss',
})
export class Fornecedores {
  private readonly http = inject(HttpClient);

  protected readonly incluirInativos = signal(false);
  protected readonly fornecedores = httpResource<Fornecedor[]>(() => ({
    url: '/api/fornecedores', params: { incluirInativos: this.incluirInativos() },
  }));

  private readonly dlg = viewChild<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly editando = signal<Fornecedor | null>(null);
  protected form = this.vazio();
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  abrir(f?: Fornecedor): void {
    this.editando.set(f ?? null);
    this.form = f
      ? { nome: f.nome, cnpj: f.cnpj ?? '', contato: f.contato ?? '', telefone: f.telefone ?? '', email: f.email ?? '', prazoEntregaDias: f.prazoEntregaDias, observacao: f.observacao ?? '' }
      : this.vazio();
    this.erro.set(null);
    this.dlg()?.nativeElement.showModal();
  }

  async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const f = this.editando();
      if (f) await firstValueFrom(this.http.put(`/api/fornecedores/${f.id}`, this.form));
      else await firstValueFrom(this.http.post('/api/fornecedores', this.form));
      this.dlg()?.nativeElement.close();
      this.fornecedores.reload();
    } catch (e) {
      this.erro.set(e instanceof HttpErrorResponse
        ? e.error?.mensagem ?? Object.values<string[]>(e.error?.errors ?? {}).flat().join(' ') ?? 'Erro ao salvar.'
        : 'Erro ao salvar.');
    } finally {
      this.salvando.set(false);
    }
  }

  async alternar(f: Fornecedor): Promise<void> {
    if (f.ativo && !confirm(`Desativar ${f.nome}? Ele sai da sugestão de compra.`)) return;
    await firstValueFrom(this.http.post(`/api/fornecedores/${f.id}/ativo`, {}, { params: { valor: !f.ativo } }));
    this.fornecedores.reload();
  }

  private vazio() {
    return { nome: '', cnpj: '', contato: '', telefone: '', email: '', prazoEntregaDias: 2, observacao: '' };
  }
}

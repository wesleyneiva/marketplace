import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

interface Usuario {
  id: string; nome: string; email: string; perfil: string; ativo: boolean; trocarSenha: boolean; sistema: boolean;
  criadoEm: string; ultimoAcessoEm: string | null;
}

@Component({
  selector: 'app-usuarios',
  imports: [FormsModule, DatePipe],
  templateUrl: './usuarios.html',
  styleUrl: './usuarios.scss',
})
export class Usuarios {
  private readonly http = inject(HttpClient);
  protected readonly eu = inject(AuthService).usuario;

  protected readonly perfis = [
    { valor: 'Caixa', descricao: 'Só o caixa (PDV) e o dashboard' },
    { valor: 'Gerente', descricao: 'Produtos, estoque, compras e relatórios' },
    { valor: 'Administrador', descricao: 'Tudo, inclusive usuários' },
  ];

  protected readonly usuarios = httpResource<Usuario[]>(() => '/api/usuarios');

  // Janela de cadastro/edição
  private readonly dlgUsuario = viewChild<ElementRef<HTMLDialogElement>>('dlgUsuario');
  protected readonly editando = signal<Usuario | null>(null);
  protected nome = '';
  protected email = '';
  protected perfil = 'Caixa';
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  // Janela da senha provisória (aparece UMA vez)
  private readonly dlgSenha = viewChild<ElementRef<HTMLDialogElement>>('dlgSenha');
  protected readonly senhaProvisoria = signal<{ nome: string; email: string; senha: string } | null>(null);
  protected readonly copiado = signal(false);

  protected readonly aviso = signal<string | null>(null);

  abrirNovo(): void {
    this.editando.set(null);
    this.nome = this.email = '';
    this.perfil = 'Caixa';
    this.erro.set(null);
    this.dlgUsuario()?.nativeElement.showModal();
  }

  abrirEdicao(u: Usuario): void {
    this.editando.set(u);
    this.nome = u.nome;
    this.email = u.email;
    this.perfil = u.perfil;
    this.erro.set(null);
    this.dlgUsuario()?.nativeElement.showModal();
  }

  async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const u = this.editando();
      if (u) {
        await firstValueFrom(this.http.put(`/api/usuarios/${u.id}`, { nome: this.nome, perfil: this.perfil }));
        this.mostrarAviso(`${this.nome} atualizado.`);
      } else {
        const r = await firstValueFrom(this.http.post<{ usuario: Usuario; senhaProvisoria: string }>(
          '/api/usuarios', { nome: this.nome, email: this.email, perfil: this.perfil }));
        this.mostrarSenha(r.usuario, r.senhaProvisoria);
      }
      this.dlgUsuario()?.nativeElement.close();
      this.usuarios.reload();
    } catch (e) {
      this.erro.set(this.mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  async alternarAtivo(u: Usuario): Promise<void> {
    if (u.ativo && !confirm(`Desativar ${u.nome}? A pessoa não consegue mais entrar (e quem estiver logado cai em até 1 minuto).`)) return;
    await this.executar(async () => {
      await firstValueFrom(this.http.post(`/api/usuarios/${u.id}/${u.ativo ? 'desativar' : 'reativar'}`, {}));
      this.mostrarAviso(`${u.nome} ${u.ativo ? 'desativado' : 'reativado'}.`);
    });
  }

  async redefinirSenha(u: Usuario): Promise<void> {
    if (!confirm(`Gerar uma nova senha provisória para ${u.nome}? A senha atual deixa de funcionar.`)) return;
    await this.executar(async () => {
      const r = await firstValueFrom(this.http.post<{ usuario: Usuario; senhaProvisoria: string }>(`/api/usuarios/${u.id}/redefinir-senha`, {}));
      this.mostrarSenha(r.usuario, r.senhaProvisoria);
    });
  }

  async copiarSenha(): Promise<void> {
    const s = this.senhaProvisoria();
    if (!s) return;
    try {
      await navigator.clipboard.writeText(s.senha);
      this.copiado.set(true);
    } catch {
      this.copiado.set(false); // sem permissão de área de transferência: a pessoa copia na mão
    }
  }

  fecharSenha(): void {
    this.dlgSenha()?.nativeElement.close();
    this.senhaProvisoria.set(null); // some da tela (e da memória)
  }

  private mostrarSenha(u: Usuario, senha: string): void {
    this.senhaProvisoria.set({ nome: u.nome, email: u.email, senha });
    this.copiado.set(false);
    setTimeout(() => this.dlgSenha()?.nativeElement.showModal());
  }

  private async executar(acao: () => Promise<void>): Promise<void> {
    try {
      await acao();
      this.usuarios.reload();
    } catch (e) {
      alert(this.mensagem(e));
    }
  }

  private mostrarAviso(texto: string): void {
    this.aviso.set(texto);
    setTimeout(() => this.aviso.set(null), 4000);
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      if (e.error?.errors) return Object.values<string[]>(e.error.errors).flat().join(' ');
    }
    return 'Não foi possível concluir.';
  }
}

import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { copiarTexto } from '../../shared/copiar';

// Formatos iguais a Contracts/PlataformaDtos.cs.
interface EmpresaResumo {
  id: number;
  nome: string;
  subdominio: string;
  limiteCaixas: number;
  ativa: boolean;
  demonstracao: boolean;
  criadoEm: string;
  usuarios: number;
  usuariosAtivos: number;
  produtos: number;
  vendas30Dias: number;
  faturamento30Dias: number;
  ultimoAcesso: string | null;
  ultimaVenda: string | null;
  caixasAbertos: number;
}

interface UsuarioDaEmpresa {
  id: string;
  nome: string;
  email: string;
  perfis: string[];
  ativo: boolean;
  trocarSenha: boolean;
  bloqueado: boolean;
  ultimoAcesso: string | null;
}

interface Credencial {
  titulo: string;
  empresa: string;
  nome: string;
  email: string;
  senha: string;
}

const URL = '/api/plataforma/empresas';
export const ENDERECO_CLIENTES = 'https://app.wnlabs.com.br';

// Clientes do SaaS: cadastrar, mudar o plano, suspender/reativar e gerar senha nova para quem esqueceu.
@Component({
  selector: 'app-empresas',
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './empresas.html',
  styleUrl: './empresas.scss',
})
export class Empresas {
  private readonly http = inject(HttpClient);

  protected readonly empresas = httpResource<EmpresaResumo[]>(() => URL);
  protected readonly aviso = signal<string | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected readonly processando = signal<number | null>(null);

  // Usuários da empresa aberta (linha expandida).
  protected readonly aberta = signal<number | null>(null);
  protected readonly usuarios = signal<UsuarioDaEmpresa[]>([]);

  // Janela de cadastro / edição.
  private readonly dlgEmpresa = viewChild<ElementRef<HTMLDialogElement>>('dlgEmpresa');
  protected readonly editando = signal<EmpresaResumo | null>(null);
  protected nome = '';
  protected subdominio = '';
  protected limiteCaixas = 2;
  protected adminNome = '';
  protected adminEmail = '';

  // Senha provisória: mostrada uma vez, já com a mensagem pronta para mandar ao cliente.
  private readonly dlgSenha = viewChild<ElementRef<HTMLDialogElement>>('dlgSenha');
  protected readonly credencial = signal<Credencial | null>(null);
  protected readonly copiado = signal(false);

  abrirNova(): void {
    this.editando.set(null);
    this.nome = '';
    this.subdominio = '';
    this.limiteCaixas = 2;
    this.adminNome = '';
    this.adminEmail = '';
    this.erro.set(null);
    this.dlgEmpresa()?.nativeElement.showModal();
  }

  abrirEdicao(e: EmpresaResumo): void {
    this.editando.set(e);
    this.nome = e.nome;
    this.limiteCaixas = e.limiteCaixas;
    this.erro.set(null);
    this.dlgEmpresa()?.nativeElement.showModal();
  }

  // "Mercado do Zé" → "mercado-do-ze" (só sugestão; o servidor confere).
  protected apelidoSugerido(): string {
    return this.nome.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
      .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40);
  }

  async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const e = this.editando();
      if (e) {
        await firstValueFrom(this.http.put(`${URL}/${e.id}`, { nome: this.nome, limiteCaixas: this.limiteCaixas }));
        this.aviso.set(`"${this.nome}" atualizada.`);
      } else {
        const r = await firstValueFrom(this.http.post<{ empresa: EmpresaResumo; adminEmail: string; senhaProvisoria: string }>(URL, {
          nome: this.nome, subdominio: this.subdominio || null, limiteCaixas: this.limiteCaixas,
          adminNome: this.adminNome, adminEmail: this.adminEmail,
        }));
        this.mostrarSenha({ titulo: 'Cliente cadastrado', empresa: r.empresa.nome, nome: this.adminNome, email: r.adminEmail, senha: r.senhaProvisoria });
        this.aviso.set(`Cliente "${r.empresa.nome}" cadastrado.`);
      }
      this.dlgEmpresa()?.nativeElement.close();
      this.empresas.reload();
    } catch (e) {
      this.erro.set(this.mensagem(e));
    } finally {
      this.salvando.set(false);
    }
  }

  async alternarSituacao(e: EmpresaResumo): Promise<void> {
    if (e.ativa && !confirm(`Suspender "${e.nome}"?\n\nNinguém da empresa consegue entrar, e quem está usando agora sai em até 1 minuto. Os dados ficam guardados.`)) return;
    this.processando.set(e.id);
    try {
      await firstValueFrom(this.http.post(`${URL}/${e.id}/${e.ativa ? 'suspender' : 'reativar'}`, {}));
      this.aviso.set(e.ativa ? `"${e.nome}" suspensa.` : `"${e.nome}" reativada.`);
      this.empresas.reload();
    } catch (err) {
      this.aviso.set(null);
      alert(this.mensagem(err));
    } finally {
      this.processando.set(null);
    }
  }

  async alternarUsuarios(e: EmpresaResumo): Promise<void> {
    if (this.aberta() === e.id) {
      this.aberta.set(null);
      return;
    }
    this.usuarios.set([]);
    this.aberta.set(e.id);
    this.usuarios.set(await firstValueFrom(this.http.get<UsuarioDaEmpresa[]>(`${URL}/${e.id}/usuarios`)));
  }

  async redefinirSenha(e: EmpresaResumo, u: UsuarioDaEmpresa): Promise<void> {
    if (!confirm(`Gerar uma senha provisória nova para ${u.nome} (${u.email})?\n\nA senha atual deixa de funcionar.`)) return;
    try {
      const r = await firstValueFrom(this.http.post<{ email: string; senhaProvisoria: string }>(`${URL}/${e.id}/usuarios/${u.id}/redefinir-senha`, {}));
      this.mostrarSenha({ titulo: 'Senha nova', empresa: e.nome, nome: u.nome, email: r.email, senha: r.senhaProvisoria });
      this.usuarios.set(await firstValueFrom(this.http.get<UsuarioDaEmpresa[]>(`${URL}/${e.id}/usuarios`)));
    } catch (err) {
      alert(this.mensagem(err));
    }
  }

  private mostrarSenha(c: Credencial): void {
    this.copiado.set(false);
    this.credencial.set(c);
    setTimeout(() => this.dlgSenha()?.nativeElement.showModal());
  }

  // Mensagem pronta para colar no WhatsApp do cliente.
  protected mensagemCliente(c: Credencial): string {
    return `Olá, ${c.nome.split(' ')[0]}! Seu acesso ao sistema do ${c.empresa}:\n\n`
      + `🌐 ${ENDERECO_CLIENTES}\n📧 ${c.email}\n🔑 Senha provisória: ${c.senha}\n\n`
      + 'No primeiro acesso o sistema pede para você criar a sua senha.';
  }

  async copiar(): Promise<void> {
    const c = this.credencial();
    if (!c) return;
    this.copiado.set(await copiarTexto(this.mensagemCliente(c)));
  }

  protected tempoDesde(data: string | null): string {
    if (!data) return 'nunca';
    const minutos = Math.round((Date.now() - new Date(data).getTime()) / 60000);
    if (minutos < 2) return 'agora';
    if (minutos < 60) return `há ${minutos} min`;
    const horas = Math.round(minutos / 60);
    if (horas < 24) return `há ${horas} h`;
    const dias = Math.round(horas / 24);
    return dias === 1 ? 'ontem' : `há ${dias} dias`;
  }

  private mensagem(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.error?.mensagem) return e.error.mensagem;
      const erros = e.error?.errors as Record<string, string[]> | undefined;
      if (erros) return Object.values(erros).flat().join(' ');
    }
    return 'Não foi possível concluir. Tente de novo.';
  }
}

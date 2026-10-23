import { Component, WritableSignal, inject, input, signal } from '@angular/core';
import { perfilRamo } from '../../../core/ramos';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CATALOGO_MODULOS } from '../../../core/modulos';
import { Icone } from '../../../shared/icone';
import { Cidade, CidadeBusca, nomeDoFuso } from '../../../shared/cidade-busca';
import { HorarioEditor } from '../../../shared/horario-editor/horario-editor';
import { Horario, horarioParaServidor, horarioParaTela } from '../../../shared/horario-editor/horario';
import { CurrencyPipe } from '@angular/common';
import { DiaPipe } from '../../../shared/dia.pipe';
import { API, Fatura, Plano, mesAno } from '../comum';
import { CampoData } from '../../../shared/campo-data';
import { Confirmacao } from '../../../shared/confirmacao';

// Formato igual a Contracts/PlataformaDtos.cs (ClienteDetalheResponse).
interface Ficha {
  razaoSocial: string | null;
  documento: string | null;
  responsavel: string | null;
  telefone: string | null;
  emailContato: string | null;
  endereco: string | null;
  observacoes: string | null;
}
interface Resumo {
  id: number; nome: string; limiteCaixas: number; ativa: boolean; demonstracao: boolean; criadoEm: string;
  usuarios: number; usuariosAtivos: number; produtos: number; diasComMovimento30: number;
  ultimoAcesso: string | null; ultimoMovimento: string | null; caixasAbertos: number; cidade: string | null;
  vendasAutomaticas: 'Desligado' | 'SoVendas' | 'Completo';
  ramo: string;
}
interface Assinatura {
  planoId: number | null; plano: string | null; diaVencimento: number; testeAte: string | null; cobrarDesde: string | null;
  precoCombinado: number | null; cortesia: boolean; valorMensal: number; motivoSuspensao: string | null;
  faturasAbertas: number; emAberto: number; maiorAtraso: number;
}
interface Detalhe { resumo: Resumo; ficha: Ficha; uf: string | null; fuso: string; horario: Horario; horarioResumo: string; assinatura: Assinatura; modulos: string[] | null }
type Aviso = { ok: boolean; texto: string } | null;

const URL = '/api/plataforma/empresas';

// Ficha do cliente na área do dono: dados cadastrais, plano, cidade, horário e uso do sistema (sem vendas: são do cliente).
@Component({
  selector: 'app-cliente',
  imports: [Icone, FormsModule, DatePipe, RouterLink, CidadeBusca, HorarioEditor, CurrencyPipe, DiaPipe, CampoData],
  templateUrl: './cliente.html',
  styleUrl: './cliente.scss',
})
export class Cliente {
  private readonly confirmacao = inject(Confirmacao);
  protected readonly perfilRamo = perfilRamo;
  private readonly http = inject(HttpClient);
  readonly id = input.required<string>(); // vem da rota /plataforma/clientes/:id

  protected readonly detalhe = httpResource<Detalhe>(() => `${URL}/${this.id()}`);
  protected readonly planos = httpResource<Plano[]>(() => `${API}/planos`);
  protected readonly faturas = httpResource<Fatura[]>(() => `${API}/faturas?situacao=todas&clienteId=${this.id()}`);
  protected readonly mesAno = mesAno;

  protected assinatura = { planoId: null as number | null, diaVencimento: 10, testeAte: '', cobrarDesde: '', precoCombinado: null as number | null, cortesia: false };
  protected readonly avisoAssinatura = signal<Aviso>(null);

  // Prévia da mensalidade com o formulário atual (o servidor confirma ao salvar).
  protected previa(): number | null {
    if (this.assinatura.cortesia) return 0;
    if (this.assinatura.precoCombinado) return this.assinatura.precoCombinado;
    const p = this.planos.value()?.find((x) => x.id === this.assinatura.planoId);
    return p ? p.precoMensal + Math.max(0, this.limiteCaixas - p.caixasIncluidos) * p.precoCaixaExtra : null;
  }

  async salvarAssinatura(): Promise<void> {
    const a = this.assinatura;
    await this.executar('assinatura', this.avisoAssinatura, () => firstValueFrom(this.http.put(`${URL}/${this.id()}/assinatura`, {
      planoId: a.planoId, diaVencimento: a.diaVencimento, testeAte: a.testeAte || null, cobrarDesde: a.cobrarDesde || null,
      precoCombinado: a.precoCombinado || null, cortesia: a.cortesia,
    })), 'Assinatura salva.');
    this.faturas.reload();
  }
  protected readonly nomeDoFuso = nomeDoFuso;

  // Formulários (preenchidos uma vez quando o detalhe chega).
  protected nome = '';
  protected limiteCaixas = 1;
  protected vendasAutomaticas: Resumo['vendasAutomaticas'] = 'Desligado';
  protected ficha: Ficha = { razaoSocial: null, documento: null, responsavel: null, telefone: null, emailContato: null, endereco: null, observacoes: null };
  protected horario: Horario | null = null;
  protected readonly cidade = signal<Cidade | null>(null);
  private carregadoId: number | null = null;

  protected readonly salvando = signal<string | null>(null); // qual seção está salvando
  protected readonly avisoDados = signal<Aviso>(null);
  protected readonly avisoPlano = signal<Aviso>(null);
  protected readonly avisoCidade = signal<Aviso>(null);
  protected readonly avisoHorario = signal<Aviso>(null);
  protected readonly avisoSituacao = signal<Aviso>(null);
  protected readonly avisoModulos = signal<Aviso>(null);

  // Módulos liberados: "todos" (inclusive os que vierem depois) ou um a um. Vale na hora no sistema do cliente.
  protected readonly catalogo = CATALOGO_MODULOS;
  protected todosModulos = true;
  protected modulosMarcados = new Set<string>();

  protected marcarModulo(id: string, marcado: boolean): void {
    if (marcado) this.modulosMarcados.add(id); else this.modulosMarcados.delete(id);
  }

  protected marcarTodosUmAUm(marcar: boolean): void {
    this.modulosMarcados = new Set(marcar ? this.catalogo.map((m) => m.id) : []);
  }

  async salvarModulos(): Promise<void> {
    const lista = this.catalogo.map((m) => m.id).filter((id) => this.modulosMarcados.has(id));
    await this.executar('modulos', this.avisoModulos, () => firstValueFrom(this.http.put(`${URL}/${this.id()}/modulos`,
      { todos: this.todosModulos, modulos: lista })),
      this.todosModulos ? 'Todos os módulos liberados.' : `${lista.length} de ${this.catalogo.length} módulos liberados.`);
  }

  protected preencher(d: Detalhe): boolean {
    if (this.carregadoId !== d.resumo.id) {
      this.nome = d.resumo.nome;
      this.limiteCaixas = d.resumo.limiteCaixas;
      this.vendasAutomaticas = d.resumo.vendasAutomaticas;
      this.ficha = { ...d.ficha };
      this.horario = horarioParaTela(d.horario);
      this.todosModulos = d.modulos === null;
      this.modulosMarcados = new Set(d.modulos ?? this.catalogo.map((m) => m.id));
      const a = d.assinatura;
      this.assinatura = { planoId: a.planoId, diaVencimento: a.diaVencimento, testeAte: a.testeAte ?? '', cobrarDesde: a.cobrarDesde ?? '',
        precoCombinado: a.precoCombinado, cortesia: a.cortesia };
      this.carregadoId = d.resumo.id;
    }
    return true;
  }

  // Dados cadastrais e plano usam o mesmo PUT (o servidor recebe tudo junto).
  async salvarDados(secao: 'dados' | 'plano'): Promise<void> {
    const aviso = secao === 'dados' ? this.avisoDados : this.avisoPlano;
    if (secao === 'plano') this.faturas.reload();
    await this.executar(secao, aviso, () => firstValueFrom(this.http.put(`${URL}/${this.id()}`, {
      nome: this.nome, limiteCaixas: this.limiteCaixas, vendasAutomaticas: this.vendasAutomaticas, ficha: this.ficha,
    })), secao === 'dados' ? 'Dados salvos.' : 'Plano salvo.');
  }

  async salvarCidade(): Promise<void> {
    const c = this.cidade();
    if (!c) return;
    await this.executar('cidade', this.avisoCidade, () => firstValueFrom(this.http.put(`${URL}/${this.id()}/cidade`,
      { nome: c.nome, uf: c.uf, latitude: c.latitude, longitude: c.longitude, fuso: c.fuso })), `Cidade salva: ${c.nome}/${c.uf}.`);
    this.cidade.set(null);
  }

  async salvarHorario(): Promise<void> {
    if (!this.horario) return;
    await this.executar('horario', this.avisoHorario,
      () => firstValueFrom(this.http.put(`${URL}/${this.id()}/horario`, horarioParaServidor(this.horario!))), 'Horário salvo.');
  }

  async alternarSituacao(r: Resumo): Promise<void> {
    if (r.ativa && !(await this.confirmacao.pedir(`Suspender "${r.nome}"?\nNinguém da empresa consegue entrar, e quem está usando agora sai em até 1 minuto. Os dados ficam guardados.`, { botao: 'Suspender', perigo: true }))) return;
    await this.executar('situacao', this.avisoSituacao,
      () => firstValueFrom(this.http.post(`${URL}/${r.id}/${r.ativa ? 'suspender' : 'reativar'}`, {})),
      r.ativa ? 'Cliente suspenso.' : 'Cliente reativado.');
  }

  private async executar(secao: string, aviso: WritableSignal<Aviso>, acao: () => Promise<unknown>, ok: string): Promise<void> {
    this.salvando.set(secao);
    aviso.set(null);
    try {
      await acao();
      aviso.set({ ok: true, texto: ok });
      this.detalhe.reload(); // o formulário não é refeito (carregadoId): só o resumo e os textos se atualizam
    } catch (e) {
      aviso.set({ ok: false, texto: this.mensagem(e) });
    } finally {
      this.salvando.set(null);
    }
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
    return 'Não foi possível salvar. Tente de novo.';
  }
}

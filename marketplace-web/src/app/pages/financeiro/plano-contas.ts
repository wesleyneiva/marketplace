import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CategoriaFinanceira, Grupo, GrupoFinanceiro, Natureza, URL_FINANCEIRO, mensagemDeErro } from '../../core/api/financeiro.api';

// Aba "Plano de contas": as categorias dentro dos grupos fixos (a DRE vai sair dos grupos).
// O cliente cria, renomeia e desativa as categorias; as do sistema (🔒) só podem ser renomeadas.
@Component({
  selector: 'app-plano-contas',
  imports: [FormsModule],
  templateUrl: './plano-contas.html',
  styleUrls: ['./financeiro.scss', './plano-contas.scss'],
})
export class PlanoContas {
  private readonly http = inject(HttpClient);

  protected readonly grupos = httpResource<GrupoFinanceiro[]>(() => `${URL_FINANCEIRO}/grupos`);
  protected readonly categorias = httpResource<CategoriaFinanceira[]>(() => ({
    url: `${URL_FINANCEIRO}/categorias`, params: { incluirInativas: true },
  }));
  protected readonly mostrarInativas = signal(false);

  protected readonly porGrupo = computed(() => {
    const cats = (this.categorias.value() ?? []).filter((c) => c.ativa || this.mostrarInativas());
    return (this.grupos.value() ?? []).map((g) => ({ ...g, itens: cats.filter((c) => c.grupo === g.grupo) }));
  });
  protected readonly inativas = computed(() => (this.categorias.value() ?? []).filter((c) => !c.ativa).length);

  // Edição na própria linha.
  protected readonly editandoId = signal<number | null>(null);
  protected edicao = { nome: '', grupo: 'Administrativas' as Grupo };

  // Nova categoria: um campo por grupo.
  protected novos: Partial<Record<Grupo, { nome: string; natureza: Natureza }>> = {};

  // Retorno junto do grupo / da linha onde a pessoa mexeu.
  protected readonly retorno = signal<{ chave: string; texto: string; ruim?: boolean } | null>(null);
  protected readonly salvando = signal(false);

  protected novo(g: Grupo) {
    return (this.novos[g] ??= { nome: '', natureza: 'Saida' });
  }

  protected async adicionar(g: GrupoFinanceiro): Promise<void> {
    const n = this.novo(g.grupo);
    if (n.nome.trim().length < 2) {
      this.retorno.set({ chave: g.grupo, texto: 'Digite o nome da categoria (2 letras ou mais).', ruim: true });
      return;
    }
    await this.fazer(g.grupo, async () => {
      const c = await firstValueFrom(this.http.post<CategoriaFinanceira>(`${URL_FINANCEIRO}/categorias`,
        { nome: n.nome, grupo: g.grupo, natureza: g.natureza ?? n.natureza }));
      n.nome = '';
      return `"${c.nome}" criada com o código ${c.codigo}.`;
    });
  }

  protected editar(c: CategoriaFinanceira): void {
    this.editandoId.set(c.id);
    this.edicao = { nome: c.nome, grupo: c.grupo };
    this.retorno.set(null);
  }

  protected async salvar(c: CategoriaFinanceira): Promise<void> {
    await this.fazer('c' + c.id, async () => {
      const r = await firstValueFrom(this.http.put<CategoriaFinanceira>(`${URL_FINANCEIRO}/categorias/${c.id}`,
        { nome: this.edicao.nome, grupo: this.edicao.grupo, natureza: c.natureza }));
      this.editandoId.set(null);
      return r.grupo !== c.grupo ? `Movida para "${r.nomeGrupo}" (código ${r.codigo}).` : 'Salvo.';
    });
  }

  protected async ativar(c: CategoriaFinanceira, valor: boolean): Promise<void> {
    await this.fazer('c' + c.id, async () => {
      await firstValueFrom(this.http.post(`${URL_FINANCEIRO}/categorias/${c.id}/ativa`, {}, { params: { valor } }));
      if (!valor) this.mostrarInativas.set(true); // continua à vista, para poder reativar
      return valor ? 'Reativada.' : 'Desativada (os lançamentos antigos continuam com ela).';
    });
  }

  // Grupos para onde uma categoria pode ir (mesma natureza; vendas do PDV nunca).
  protected destinos(c: CategoriaFinanceira): GrupoFinanceiro[] {
    return (this.grupos.value() ?? []).filter((g) => g.grupo !== 'ReceitaVendas' && (g.natureza ?? c.natureza) === c.natureza);
  }

  private async fazer(chave: string, acao: () => Promise<string>): Promise<void> {
    this.salvando.set(true);
    this.retorno.set(null);
    try {
      this.retorno.set({ chave, texto: await acao() });
      this.categorias.reload();
    } catch (e) {
      this.retorno.set({ chave, texto: mensagemDeErro(e), ruim: true });
    } finally {
      this.salvando.set(false);
    }
  }
}

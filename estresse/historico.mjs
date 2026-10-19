// HISTÓRICO para os mercados de teste: vendas passadas (Origem = Historico, sem mexer no estoque), para os
// relatórios, a DRE e o painel trabalharem com o volume de um mercado que já usa o sistema há meses.
// Só no banco marketplace_estresse.
//
//   node estresse/historico.mjs --dias 90 --vendas-dia 400
//
// Espaço: cerca de 1 KB por item vendido. 90 dias × 400 vendas × ~4,5 itens ≈ 160 MB por mercado
// (50 mercados ≈ 8 GB). Confira o disco antes (df -h /).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const executar = promisify(execFile);
const arg = (nome, padrao) => { const i = process.argv.indexOf('--' + nome); return i > 0 ? process.argv[i + 1] : padrao; };
const DIAS = Number(arg('dias', '90'));
const VENDAS_DIA = Number(arg('vendas-dia', '400'));
const estado = JSON.parse(fs.readFileSync(path.join(os.homedir(), 'estresse', 'estado.json'), 'utf8'));

const psql = (sql) => executar('docker', ['exec', 'marketplace-db', 'psql', '-U', 'marketplace', '-d', 'marketplace_estresse', '-v', 'ON_ERROR_STOP=1', '-qtAc', sql], { maxBuffer: 1 << 24 });

console.log(`Histórico: ${DIAS} dias × ${VENDAS_DIA} vendas/dia em ${estado.mercados.length} mercados de teste.`);
for (const m of estado.mercados) {
  const E = Number(m.empresaId);
  const { stdout: ja } = await psql(`select count(*) from "Vendas" where "EmpresaId" = ${E} and "Origem" = 'Historico'`);
  if (Number(ja) > 0) { console.log(`  · Mercado ${m.n}: já tem histórico (${ja.trim()} vendas), pulando.`); continue; }
  const t0 = Date.now();
  await psql(`
    begin;
    create temp table v_hist ("Id" int) on commit drop;
    with s as (
        insert into "SessoesCaixa" ("NumeroCaixa", "UsuarioId", "Status", "AbertaEm", "ValorAbertura", "FechadaEm", "EmpresaId", "LancadaNoFinanceiroEm")
        select 1, u."Id", 'Fechada', now() - interval '${DIAS + 1} days', 0, now() - interval '1 day', ${E}, now()
        from "AspNetUsers" u where u."EmpresaId" = ${E} limit 1
        returning "Id", "UsuarioId"),
    nova as (
      insert into "Vendas" ("SessaoCaixaId", "UsuarioId", "DataHora", "Subtotal", "Desconto", "Total", "ValorPago", "Troco", "Status", "Origem", "EmpresaId")
      select s."Id", s."UsuarioId", date_trunc('day', now()) - make_interval(days => d) + interval '8 hours' + random() * interval '13 hours',
             0, 0, 0, 0, 0, 'Concluida', 'Historico', ${E}
      from s, generate_series(1, ${DIAS}) d, generate_series(1, ${VENDAS_DIA}) k
      returning "Id")
    insert into v_hist select "Id" from nova;
    insert into "ItensVenda" ("VendaId", "ProdutoId", "Descricao", "Unidade", "Quantidade", "PrecoUnitario", "CustoUnitario", "Total", "EmpresaId")
      select v."Id", p."Id", p."Nome", p."Unidade", q.qtd, p."PrecoVenda", p."PrecoCusto", round(q.qtd * p."PrecoVenda", 2), ${E}
      from v_hist v
      cross join lateral (select * from "Produtos" pr where pr."EmpresaId" = ${E} and pr."PrecoVenda" > 0 and v."Id" > 0
                          order by random() limit (1 + floor(random() * 8))::int) p
      cross join lateral (select case when p."Unidade" in ('KG', 'L') then round((0.3 + random() * 2)::numeric, 3) else 1 end as qtd) q;
    update "Vendas" ve set "Subtotal" = t.total, "Total" = t.total, "ValorPago" = t.total
      from (select "VendaId", sum("Total") total from "ItensVenda" i join v_hist v on v."Id" = i."VendaId" group by 1) t
      where ve."Id" = t."VendaId";
    insert into "PagamentosVenda" ("VendaId", "Forma", "Valor", "EmpresaId")
      select ve."Id", (array['Pix','Pix','Debito','Debito','Credito','Dinheiro'])[1 + floor(random() * 6)::int], ve."Total", ${E}
      from "Vendas" ve join v_hist v on v."Id" = ve."Id";
    commit;`);
  console.log(`  ✓ Mercado ${m.n} (empresa #${E}): ${DIAS * VENDAS_DIA} vendas em ${Math.round((Date.now() - t0) / 1000)} s`);
}
const { stdout } = await psql(`select pg_size_pretty(pg_database_size('marketplace_estresse'))`);
console.log(`Banco de estresse agora: ${stdout.trim()}.`);

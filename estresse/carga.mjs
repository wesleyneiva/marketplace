// TESTE DE ESTRESSE: mercados de teste vendendo ao mesmo tempo, em etapas crescentes, contra a API de estresse
// (porta 5400, banco marketplace_estresse). Mede tempo de resposta, erros e o uso da VM, e gera um relatório.
//
//   node estresse/carga.mjs --etapas 10,25,50 --minutos 3 --acelerar 10
//
//   --etapas    quantos mercados ficam ativos em cada etapa (precisam existir: node estresse/mercados.mjs N)
//   --minutos   duração de cada etapa
//   --acelerar  quantas vezes mais rápido que um mercado de verdade. Tudo acelera junto (vendas, avisos de
//               "estou vivo", catálogo, telas do gerente): 50 mercados × 10 = a carga de 500 mercados reais.
//
// Cada mercado = 3 operadores de caixa + 1 gerente, como na vida real:
//   caixa  → abre o caixa, vende (1 venda/min por caixa no ritmo real; 1 a 12 itens, peso em KG, Pix/débito/
//            crédito/dinheiro com troco), pergunta /api/saude a cada 10 s, /api/auth/eu a cada 1 min e
//            baixa o catálogo a cada 10 min (o que o caixa offline faz);
//   gerente → a cada ~2 min abre uma tela: painel, relatórios de 30 dias, posição do estoque, DRE ou vendas do dia.
// Cada mercado usa um IP próprio (cabeçalho da Cloudflare): valem os limites por IP da internet.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const executar = promisify(execFile);
const PASTA = path.join(os.homedir(), 'estresse');
const BASE = 'http://127.0.0.1:5400';
const arg = (nome, padrao) => { const i = process.argv.indexOf('--' + nome); return i > 0 ? process.argv[i + 1] : padrao; };
const ETAPAS = arg('etapas', '10,25,50').split(',').map(Number);
const MINUTOS = Number(arg('minutos', '3'));
const ACELERAR = Number(arg('acelerar', '10'));
const ROTULO = arg('rotulo', '');

try {
  const r = await fetch(BASE + '/api/saude', { signal: AbortSignal.timeout(5000) });
  if (!r.ok) throw new Error('status ' + r.status);
} catch (e) {
  console.error(`A API de estresse não está respondendo (${e.message}). Ligue com: ./estresse/ambiente.sh iniciar`);
  process.exit(1);
}

const estado = JSON.parse(fs.readFileSync(path.join(PASTA, 'estado.json'), 'utf8'));
if (estado.mercados.length < Math.max(...ETAPAS)) {
  console.error(`Há ${estado.mercados.length} mercados de teste; a maior etapa pede ${Math.max(...ETAPAS)}. Rode: node estresse/mercados.mjs ${Math.max(...ETAPAS)}`);
  process.exit(1);
}

// ---------------------------------------------------------------- medições
const amostras = [];   // { t, tipo, ms, status }
const vm = [];         // { t, cpu, memLivreMb, apiMb, load }
const banco = [];      // { t, conexoes, tamanhoMb }
let rodando = true;
const agora = () => Date.now();
const dormir = (ms) => new Promise((r) => setTimeout(r, ms));
const exponencial = (media) => -Math.log(1 - Math.random()) * media; // intervalo "de gente": às vezes rápido, às vezes demora

// Um mercado do teste faz o tráfego de ACELERAR mercados reais: os pedidos saem de ACELERAR "IPs" diferentes,
// senão o limite de 300 pedidos/min por IP (que vale para UMA loja) barraria o teste em vez do sistema.
const ipVirtual = (m) => (ACELERAR <= 1 ? m.ip : `10.78.${m.n % 250}.${1 + Math.floor(Math.random() * Math.min(ACELERAR, 250))}`);

async function chamar(m, cookie, tipo, metodo, url, corpo) {
  const t0 = performance.now();
  let status = 0, json = null, novoCookie = cookie;
  try {
    const r = await fetch(BASE + url, {
      method: metodo,
      headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': tipo === 'login' ? m.ip : ipVirtual(m), ...(cookie ? { Cookie: cookie } : {}) },
      body: corpo ? JSON.stringify(corpo) : undefined,
      signal: AbortSignal.timeout(30000),
    });
    status = r.status;
    const texto = await r.text();
    try { json = JSON.parse(texto); } catch { /* sem corpo */ }
    const sc = r.headers.getSetCookie().map((c) => c.split(';')[0]).join('; ');
    if (sc) novoCookie = sc;
  } catch { status = 0; } // sem resposta (caiu, recusou ou passou de 30 s)
  amostras.push({ t: agora(), tipo, ms: performance.now() - t0, status, msg: status >= 400 ? (json?.mensagem ?? '').slice(0, 90) : undefined });
  return { status, json, cookie: novoCookie };
}

async function entrar(m, email) {
  for (let tentativa = 0; tentativa < 5; tentativa++) {
    const r = await chamar(m, null, 'login', 'POST', '/api/auth/login', { email, senha: estado.senha });
    if (r.status === 200) return r.cookie;
    await dormir(2000 + Math.random() * 3000);
  }
  throw new Error('não conseguiu entrar: ' + email);
}

// ---------------------------------------------------------------- operador de caixa
async function operador(m, email, numero) {
  let cookie = await entrar(m, email);
  const atual = await chamar(m, cookie, 'caixaAtual', 'GET', '/api/caixa/atual');
  if (atual.status !== 200 || !atual.json) await chamar(m, cookie, 'abrirCaixa', 'POST', '/api/caixa/abrir', { numeroCaixa: numero, valorAbertura: 150 });
  let catalogo = [];
  const baixarCatalogo = async () => {
    const r = await chamar(m, cookie, 'catalogo', 'GET', '/api/caixa/catalogo');
    if (Array.isArray(r.json)) catalogo = r.json.filter((p) => p.precoVenda > 0);
  };
  await baixarCatalogo();

  // "Estou vivo" e sessão, como o caixa faz sozinho em segundo plano.
  const ciclos = [
    setInterval(() => chamar(m, null, 'saude', 'GET', '/api/saude'), 10000 / ACELERAR),
    setInterval(() => chamar(m, cookie, 'sessao', 'GET', '/api/auth/eu'), 60000 / ACELERAR),
    setInterval(baixarCatalogo, 600000 / ACELERAR),
  ];
  await dormir(Math.random() * 60000 / ACELERAR); // os caixas não começam todos no mesmo segundo
  while (rodando) {
    if (catalogo.length) {
      const qtd = 1 + Math.min(11, Math.floor(exponencial(4)));
      const usados = new Set(); const itens = [];
      while (itens.length < qtd && usados.size < catalogo.length) {
        const p = catalogo[Math.floor(Math.random() * catalogo.length)];
        if (usados.has(p.id)) continue;
        usados.add(p.id);
        const q = p.unidade === 'KG' || p.unidade === 'L' ? Math.round((0.2 + Math.random() * 2.3) * 1000) / 1000 : (Math.random() < 0.85 ? 1 : 2);
        itens.push({ p, q });
      }
      const total = Math.round(itens.reduce((s, { p, q }) => s + Math.round(q * p.precoVenda * 100) / 100, 0) * 100) / 100;
      const sorteio = Math.random();
      const pagamento = sorteio < 0.4 ? { forma: 'Pix', valor: total } : sorteio < 0.7 ? { forma: 'Debito', valor: total }
        : sorteio < 0.85 ? { forma: 'Credito', valor: total }
        : { forma: 'Dinheiro', valor: [2, 5, 10, 20, 50, 100, 200].find((n) => n >= total) ?? total };
      const r = await chamar(m, cookie, 'venda', 'POST', '/api/vendas', {
        itens: itens.map(({ p, q }) => ({ produtoId: p.id, quantidade: q })), desconto: 0, pagamentos: [pagamento], idLocal: crypto.randomUUID(),
      });
      if (r.status === 401) cookie = await entrar(m, email);
    }
    await dormir(exponencial(60000 / ACELERAR));
  }
  ciclos.forEach(clearInterval);
}

// ---------------------------------------------------------------- gerente
async function gerente(m) {
  let cookie = await entrar(m, m.gerente);
  const hoje = new Date().toLocaleDateString('sv-SE');
  const de = new Date(Date.now() - 29 * 864e5).toLocaleDateString('sv-SE');
  const telas = [
    ['painel', 0.35, () => Promise.all([
      chamar(m, cookie, 'painel', 'GET', '/api/vendas/resumo'),
      chamar(m, cookie, 'painel', 'GET', '/api/estoque/validades?dias=7'),
      chamar(m, cookie, 'painel', 'GET', '/api/contas/resumo'),
      chamar(m, cookie, 'painel', 'GET', '/api/produtos?estoqueBaixo=true&tamanho=5'),
    ])],
    ['relatorios', 0.2, () => Promise.all(['vendas', 'produtos', 'clima'].map((r) =>
      chamar(m, cookie, 'relatorios', 'GET', `/api/relatorios/${r}?de=${de}&ate=${hoje}`)))],
    ['posicao', 0.15, () => chamar(m, cookie, 'posicaoEstoque', 'GET', '/api/estoque/posicao?ordem=valor')],
    ['dre', 0.1, () => chamar(m, cookie, 'dre', 'GET', '/api/financeiro/dre?meses=6')],
    ['vendas', 0.2, () => chamar(m, cookie, 'listaVendas', 'GET', `/api/vendas?data=${hoje}`)],
  ];
  await dormir(Math.random() * 120000 / ACELERAR);
  while (rodando) {
    let s = Math.random();
    const tela = telas.find(([, peso]) => (s -= peso) < 0) ?? telas[0];
    const r = await tela[2]();
    if ([r].flat().some((x) => x.status === 401)) cookie = await entrar(m, m.gerente);
    await dormir(exponencial(120000 / ACELERAR));
  }
}

// ---------------------------------------------------------------- monitor da VM e do banco
const pidApi = Number(fs.readFileSync(path.join(PASTA, 'api.pid'), 'utf8'));
let cpuAntes = null;
function lerCpu() {
  const c = fs.readFileSync('/proc/stat', 'utf8').split('\n')[0].split(/\s+/).slice(1).map(Number);
  const ocioso = c[3] + c[4]; const totalCpu = c.reduce((a, b) => a + b, 0);
  return { ocioso, totalCpu };
}
const monitorVm = setInterval(() => {
  const c = lerCpu();
  const cpu = cpuAntes ? 100 * (1 - (c.ocioso - cpuAntes.ocioso) / (c.totalCpu - cpuAntes.totalCpu)) : null;
  cpuAntes = c;
  const mem = fs.readFileSync('/proc/meminfo', 'utf8');
  const livre = Number(mem.match(/MemAvailable:\s+(\d+)/)[1]) / 1024;
  let apiMb = null; try { apiMb = Number(fs.readFileSync(`/proc/${pidApi}/status`, 'utf8').match(/VmRSS:\s+(\d+)/)[1]) / 1024; } catch { /* API caiu? */ }
  if (cpu !== null) vm.push({ t: agora(), cpu, memLivreMb: livre, apiMb, load: os.loadavg()[0] });
}, 5000);
const monitorBanco = setInterval(async () => {
  try {
    const { stdout } = await executar('docker', ['exec', 'marketplace-db', 'psql', '-U', 'marketplace', '-d', 'marketplace_estresse', '-tAc',
      "select (select count(*) from pg_stat_activity where datname='marketplace_estresse'), pg_database_size('marketplace_estresse')/1048576"]);
    const [conexoes, tamanhoMb] = stdout.trim().split('|').map(Number);
    banco.push({ t: agora(), conexoes, tamanhoMb });
  } catch { /* segue */ }
}, 30000);

// ---------------------------------------------------------------- etapas
const tarefas = [];
const janelas = [];
let ativos = 0;
console.log(`Teste de estresse: etapas ${ETAPAS.join(' → ')} mercados, ${MINUTOS} min cada, ${ACELERAR}x mais rápido que a vida real.`);
for (const alvo of ETAPAS) {
  for (; ativos < alvo; ativos++) {
    const m = estado.mercados[ativos];
    m.caixas.forEach((email, i) => tarefas.push(operador(m, email, i + 1).catch((e) => console.error('caixa', m.n, e.message))));
    tarefas.push(gerente(m).catch((e) => console.error('gerente', m.n, e.message)));
  }
  const inicio = agora();
  console.log(`\n▶ Etapa: ${alvo} mercados (${alvo * 3} caixas) = carga de ${alvo * ACELERAR} mercados reais. ${MINUTOS} min…`);
  await dormir(MINUTOS * 60000);
  janelas.push({ mercados: alvo, inicio: inicio + Math.min(30000, MINUTOS * 15000), fim: agora() }); // 30 s de aquecimento fora da conta
  const r = resumoDaJanela(janelas.at(-1));
  console.log(`  vendas: ${r.vendasPorMin}/min · p95 ${r.tipos.venda?.p95 ?? '–'} ms · erros ${r.erroPct}% (5xx ${r.erro5xx}, sem resposta ${r.semResposta}, limite 429 ${r.barradosPorLimite}) · CPU média ${r.cpuMedia}% (máx ${r.cpuMax}%) · ${r.aprovada ? 'APROVADA' : 'REPROVADA'}`);
}
rodando = false;
console.log('\nEncerrando (esperando as últimas vendas)…');
await Promise.race([Promise.all(tarefas), dormir(15000)]);
clearInterval(monitorVm); clearInterval(monitorBanco);

// ---------------------------------------------------------------- relatório
function pct(lista, f) { if (!lista.length) return null; const o = [...lista].sort((a, b) => a - b); return Math.round(o[Math.min(o.length - 1, Math.floor(o.length * f))]); }
function resumoDaJanela(j) {
  const a = amostras.filter((x) => x.t >= j.inicio && x.t <= j.fim);
  const tipos = {};
  for (const x of a) (tipos[x.tipo] ??= []).push(x);
  const porTipo = Object.fromEntries(Object.entries(tipos).map(([k, l]) => {
    const ms = l.map((x) => x.ms);
    return [k, { pedidos: l.length, p50: pct(ms, 0.5), p95: pct(ms, 0.95), p99: pct(ms, 0.99), max: Math.round(Math.max(...ms)), erros: l.filter((x) => x.status === 0 || x.status >= 500 || x.status === 429).length }];
  }));
  const graves = a.filter((x) => x.status === 0 || x.status >= 500 || x.status === 429);
  const regra = a.filter((x) => x.status >= 400 && x.status < 500 && x.status !== 429 && x.status !== 401);
  const v = vm.filter((x) => x.t >= j.inicio && x.t <= j.fim);
  const minutos = (j.fim - j.inicio) / 60000;
  const vendasOk = (tipos.venda ?? []).filter((x) => x.status === 200 || x.status === 201).length;
  const erroPct = a.length ? Math.round(1000 * graves.length / a.length) / 10 : 0;
  const cpuMedia = v.length ? Math.round(v.reduce((s, x) => s + x.cpu, 0) / v.length) : null;
  const r = {
    mercados: j.mercados, equivalentes: j.mercados * ACELERAR, pedidos: a.length, pedidosPorSeg: Math.round(a.length / (minutos * 60)),
    vendasPorMin: Math.round(vendasOk / minutos), erroPct, errosGraves: graves.length, recusasDeRegra: regra.length,
    exemplosRecusa: [...new Set(regra.map((x) => `${x.tipo} ${x.status} ${x.msg}`))].slice(0, 5),
    barradosPorLimite: a.filter((x) => x.status === 429).length, semResposta: a.filter((x) => x.status === 0).length,
    erro5xx: a.filter((x) => x.status >= 500).length,
    cpuMedia, cpuMax: v.length ? Math.round(Math.max(...v.map((x) => x.cpu))) : null,
    memLivreMinMb: v.length ? Math.round(Math.min(...v.map((x) => x.memLivreMb))) : null,
    apiMaxMb: v.length ? Math.round(Math.max(...v.map((x) => x.apiMb ?? 0))) : null,
    tipos: porTipo,
  };
  r.aprovada = (porTipo.venda?.p95 ?? 99999) < 800 && erroPct < 0.5 && (cpuMedia ?? 100) < 75;
  return r;
}
const resultado = {
  quando: new Date().toISOString(), rotulo: ROTULO, minutosPorEtapa: MINUTOS, acelerar: ACELERAR,
  vmCpus: os.cpus().length, vmMemGb: Math.round(os.totalmem() / 2 ** 30),
  etapas: janelas.map(resumoDaJanela),
  banco: { inicioMb: banco[0]?.tamanhoMb, fimMb: banco.at(-1)?.tamanhoMb, conexoesMax: Math.max(0, ...banco.map((b) => b.conexoes)) },
};
const nome = new Date().toLocaleString('sv-SE').replace(/[: ]/g, '-').slice(0, 16);
fs.mkdirSync(path.join(PASTA, 'resultados'), { recursive: true });
const arqJson = path.join(PASTA, 'resultados', `${nome}.json`);
fs.writeFileSync(arqJson, JSON.stringify(resultado, null, 2));
const arqHtml = arqJson.replace(/\.json$/, '.html');
fs.writeFileSync(arqHtml, relatorioHtml(resultado));
console.log(`\nRelatório: ${arqHtml}\nDados:     ${arqJson}`);

function relatorioHtml(R) {
  const fmt = (n) => (n ?? '–').toLocaleString?.('pt-BR') ?? n;
  const linhas = R.etapas.map((e) => `<tr><td><b>${e.mercados}</b> (${e.mercados * 3} caixas)</td><td>${fmt(e.equivalentes)}</td><td>${fmt(e.vendasPorMin)}</td>
    <td>${fmt(e.tipos.venda?.p50)} / <b>${fmt(e.tipos.venda?.p95)}</b> / ${fmt(e.tipos.venda?.p99)}</td><td>${fmt(e.pedidosPorSeg)}</td><td>${e.erroPct}%</td>
    <td>${fmt(e.cpuMedia)}% <small>(máx ${fmt(e.cpuMax)}%)</small></td><td>${fmt(e.memLivreMinMb)} MB</td><td>${fmt(e.apiMaxMb)} MB</td>
    <td><span class="${e.aprovada ? 'ok' : 'ruim'}">${e.aprovada ? 'Aprovada' : 'Reprovada'}</span></td></tr>`).join('');
  const telas = [...new Set(R.etapas.flatMap((e) => Object.keys(e.tipos)))];
  const detalhe = telas.map((t) => `<tr><td>${t}</td>${R.etapas.map((e) => `<td>${e.tipos[t] ? `${fmt(e.tipos[t].p95)} ms <small>(${fmt(e.tipos[t].pedidos)})</small>` : '–'}</td>`).join('')}</tr>`).join('');
  const maxP95 = Math.max(1, ...R.etapas.map((e) => e.tipos.venda?.p95 ?? 0));
  const barras = R.etapas.map((e, i) => { const h = Math.round(160 * (e.tipos.venda?.p95 ?? 0) / Math.max(maxP95, 800));
    return `<rect x="${40 + i * 90}" y="${190 - h}" width="56" height="${h}" rx="6" fill="${e.aprovada ? '#15803d' : '#b91c1c'}"/><text x="${68 + i * 90}" y="${184 - h}" text-anchor="middle">${e.tipos.venda?.p95 ?? '–'} ms</text><text x="${68 + i * 90}" y="210" text-anchor="middle">${e.mercados}</text>`; }).join('');
  const linha800 = 190 - Math.round(160 * 800 / Math.max(maxP95, 800));
  const recusas = R.etapas.flatMap((e) => e.exemplosRecusa).filter((x, i, a) => a.indexOf(x) === i);
  return `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Teste de estresse do Marcus</title><style>
  body{font-family:"Space Grotesk",Arial,sans-serif;background:#f4f6f5;color:#1c2321;margin:0;padding:32px}main{max-width:1100px;margin:auto}
  h1{margin:0}p.sub{color:#5f6b66;margin:4px 0 20px}.cartao{background:#fff;border:1px solid #e2e8e5;border-radius:12px;padding:16px 20px;margin-bottom:16px;overflow-x:auto}
  table{border-collapse:collapse;width:100%;font-size:14px}th{text-align:left;color:#5f6b66;font-size:12px;text-transform:uppercase;padding:6px 8px;border-bottom:1px solid #e2e8e5}
  td{padding:6px 8px;border-bottom:1px solid #e2e8e5}small{color:#5f6b66}.ok,.ruim{padding:2px 10px;border-radius:99px;font-weight:600;font-size:13px}.ok{background:#dcfce7;color:#166534}.ruim{background:#fee2e2;color:#b91c1c}
  svg text{font:12px Arial;fill:#5f6b66}</style></head><body><main>
  <h1>Teste de estresse do Marcus</h1><p class="sub">${new Date(R.quando).toLocaleString('pt-BR')} · ${R.rotulo ? R.rotulo + ' · ' : ''}${R.minutosPorEtapa} min por etapa · ${R.acelerar}x mais rápido que a vida real · VM ${R.vmCpus} núcleos, ${R.vmMemGb} GB</p>
  <div class="cartao"><table><tr><th>Mercados ativos</th><th>Equivale a (mercados reais)</th><th>Vendas/min</th><th>Venda ms (mediana / p95 / p99)</th><th>Pedidos/s</th><th>Erros</th><th>CPU</th><th>Memória livre (mín.)</th><th>API (memória)</th><th>Resultado</th></tr>${linhas}</table>
  <p><small>Aprovada = 95% das vendas em menos de 0,8 s, menos de 0,5% de erros (sem resposta, 5xx ou 429) e CPU média abaixo de 75%. Os primeiros 30 s de cada etapa ficam fora da conta (aquecimento).</small></p></div>
  <div class="cartao"><b>Tempo de 95% das vendas por etapa</b><br><svg width="${80 + R.etapas.length * 90}" height="220" aria-label="p95 das vendas por etapa"><line x1="30" x2="${50 + R.etapas.length * 90}" y1="${linha800}" y2="${linha800}" stroke="#b45309" stroke-dasharray="4 4"/><text x="34" y="${linha800 - 4}">limite 800 ms</text>${barras}</svg></div>
  <div class="cartao"><b>Tempo de 95% dos pedidos, por tipo</b> <small>(entre parênteses: quantos pedidos)</small><table><tr><th>Tipo</th>${R.etapas.map((e) => `<th>${e.mercados} mercados</th>`).join('')}</tr>${detalhe}</table></div>
  <div class="cartao"><b>Banco</b>: ${fmt(R.banco.inicioMb)} MB → ${fmt(R.banco.fimMb)} MB · até ${fmt(R.banco.conexoesMax)} conexões abertas.
  ${recusas.length ? `<p><b>Recusas de regra de negócio</b> (não contam como erro do sistema):</p><ul>${recusas.map((x) => `<li><small>${x}</small></li>`).join('')}</ul>` : ''}</div>
  </main></body></html>`;
}

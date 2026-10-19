// Cria os MERCADOS DE TESTE no ambiente de estresse (banco marketplace_estresse, API na porta 5400).
// Cada mercado nasce como um cliente de verdade: pelo comando nova-empresa (3 caixas), catálogo do
// Mercadinho Juvenal (~197 produtos) pelo importar-produtos, e 1 administrador + 1 gerente + 3 operadores
// de caixa, todos com a senha provisória já trocada. Pode rodar de novo: só cria os que faltam.
//
//   node estresse/mercados.mjs 50
//
// Guarda os logins em ~/estresse/estado.json (só teste; apagado no "desmontar").
import { execFile } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';

const executar = promisify(execFile);
const PASTA = path.join(os.homedir(), 'estresse');
const BASE = 'http://127.0.0.1:5400';
const CATALOGO = path.join(os.homedir(), 'projetos/marketplace-testes/juvenal/juvenal-produtos.csv');
const ARQ_ESTADO = path.join(PASTA, 'estado.json');
const total = Number(process.argv[2] ?? 10);
const PARALELO = 3;

if (!fs.existsSync(path.join(PASTA, 'estresse.env'))) { console.error('Monte antes: ./estresse/ambiente.sh montar'); process.exit(1); }
const ambiente = { ...process.env };
for (const linha of fs.readFileSync(path.join(PASTA, 'estresse.env'), 'utf8').split('\n')) {
  const m = linha.match(/^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/);
  if (m) ambiente[m[1]] = m[2];
}
if (!String(ambiente.ConnectionStrings__Marketplace).includes('Database=marketplace_estresse')) {
  console.error('Trava de segurança: a conexão não aponta para marketplace_estresse.'); process.exit(1);
}

const estado = fs.existsSync(ARQ_ESTADO) ? JSON.parse(fs.readFileSync(ARQ_ESTADO, 'utf8'))
  : { senha: 'Estresse-' + Math.random().toString(36).slice(2, 8) + 'A9', mercados: [] };
const salvar = () => fs.writeFileSync(ARQ_ESTADO, JSON.stringify(estado, null, 2), { mode: 0o600 });

// Cada mercado tem um "IP de internet" próprio (cabeçalho da Cloudflare): passa pelos mesmos limites
// de tentativas de login e de pedidos por minuto que um mercado de verdade.
export const ipDoMercado = (n) => `10.77.${Math.floor(n / 250)}.${(n % 250) + 1}`;

async function cli(...args) {
  const { stdout } = await executar('dotnet', ['Marketplace.Api.dll', ...args], { cwd: path.join(PASTA, 'app'), env: ambiente, maxBuffer: 1 << 24 });
  return stdout;
}

async function chamar(ip, cookie, metodo, url, corpo) {
  const r = await fetch(BASE + url, {
    method: metodo,
    headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': ip, ...(cookie ? { Cookie: cookie } : {}) },
    body: corpo ? JSON.stringify(corpo) : undefined,
  });
  const texto = await r.text();
  let json = null; try { json = JSON.parse(texto); } catch { /* sem corpo */ }
  if (!r.ok) throw new Error(`${metodo} ${url} → ${r.status} ${json?.mensagem ?? texto.slice(0, 120)}`);
  return { json, cookie: r.headers.getSetCookie().map((c) => c.split(';')[0]).join('; ') || cookie };
}

// Entra com a senha provisória e troca pela senha do teste.
async function ativar(ip, email, provisoria) {
  const { cookie } = await chamar(ip, null, 'POST', '/api/auth/login', { email, senha: provisoria });
  await chamar(ip, cookie, 'POST', '/api/auth/trocar-senha', { senhaAtual: provisoria, novaSenha: estado.senha });
}

async function criarMercado(n) {
  const id3 = String(n).padStart(3, '0');
  const ip = ipDoMercado(n);
  const adminEmail = `admin${id3}@estresse.test`;
  const saida = await cli('nova-empresa', '--nome', `Mercado Estresse ${id3}`, '--subdominio', `estresse${id3}`,
    '--caixas', '3', '--admin-email', adminEmail, '--admin-nome', `Admin ${id3}`);
  const empresaId = Number(saida.match(/#(\d+)/)?.[1]);
  const provisoria = saida.match(/Senha provisória:\s*(\S+)/)?.[1];
  if (!empresaId || !provisoria) throw new Error('nova-empresa não devolveu id/senha:\n' + saida);

  await cli('importar-produtos', '--empresa', String(empresaId), '--arquivo', CATALOGO, '--gravar', 'true');

  await ativar(ip, adminEmail, provisoria);
  const { cookie } = await chamar(ip, null, 'POST', '/api/auth/login', { email: adminEmail, senha: estado.senha });
  const usuarios = { admin: adminEmail, gerente: `gerente${id3}@estresse.test`, caixas: [] };
  const equipe = [['gerente', usuarios.gerente, 'Gerente'],
    ...[1, 2, 3].map((c) => [`caixa${c}`, `caixa${c}-${id3}@estresse.test`, 'Caixa'])];
  for (const [rotulo, email, perfil] of equipe) {
    const { json } = await chamar(ip, cookie, 'POST', '/api/usuarios', { nome: `${rotulo} ${id3}`, email, perfil });
    await ativar(ip, email, json.senhaProvisoria ?? json.senha);
    if (perfil === 'Caixa') usuarios.caixas.push(email);
  }
  return { n, empresaId, ip, ...usuarios };
}

const feitos = new Set(estado.mercados.map((m) => m.n));
const fila = Array.from({ length: total }, (_, i) => i + 1).filter((n) => !feitos.has(n));
console.log(`Mercados de teste: ${feitos.size} prontos, ${fila.length} a criar (de ${PARALELO} em ${PARALELO}).`);
const inicio = Date.now();
let falhas = 0;
async function trabalhador() {
  while (fila.length) {
    const n = fila.shift();
    try {
      const m = await criarMercado(n);
      estado.mercados.push(m); estado.mercados.sort((a, b) => a.n - b.n); salvar();
      console.log(`  ✓ Mercado ${n} (empresa #${m.empresaId}) · ${Math.round((Date.now() - inicio) / 1000)} s`);
    } catch (e) { falhas++; console.error(`  ✗ Mercado ${n}: ${e.message}`); }
  }
}
await Promise.all(Array.from({ length: PARALELO }, trabalhador));

// Estoque "infinito" e validade longe só NOS MERCADOS DE TESTE: o teste mede velocidade, não falta de produto.
const ids = estado.mercados.map((m) => m.empresaId).join(',');
if (ids) {
  const sql = `
    update "Lotes" set "QuantidadeAtual" = 100000, "DataValidade" = current_date + 365 where "EmpresaId" in (${ids});
    update "Produtos" p set "EstoqueAtual" = coalesce((select sum(l."QuantidadeAtual") from "Lotes" l where l."ProdutoId" = p."Id" and l."QuantidadeAtual" > 0), 0)
      where p."EmpresaId" in (${ids}) and p."ControlaValidade";
    update "Produtos" set "EstoqueAtual" = 1000000 where "EmpresaId" in (${ids}) and not "ControlaValidade";`;
  await executar('docker', ['exec', 'marketplace-db', 'psql', '-U', 'marketplace', '-d', 'marketplace_estresse', '-q', '-c', sql]);
}
console.log(`Pronto: ${estado.mercados.length} mercados de teste${falhas ? `, ${falhas} com erro (rode de novo para tentar outra vez)` : ''}.`);
console.log('Próximo passo: node estresse/carga.mjs --etapas 10,25,50 --minutos 3 --acelerar 10');

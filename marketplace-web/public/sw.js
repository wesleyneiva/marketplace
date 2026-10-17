// Service worker do Marcus: deixa as TELAS abrirem sem internet (o caixa offline).
// Só guarda os arquivos do site (HTML, JS, CSS, fontes, ícones). A API (/api) NUNCA passa por aqui: os dados
// do caixa sem internet ficam no IndexedDB (core/offline/caixa-offline.service.ts).
//   • Página (navegação): tenta a internet; sem ela, abre o index.html guardado.
//   • Arquivos com hash no nome (main-ABC.js): guardados na instalação (lista em offline-arquivos.json, gerada pelo
//     publicar.sh) e servidos do cache — o nome muda a cada publicação, então nunca ficam velhos.
// Versão nova publicada: a página registra /sw.js?v=<main-xxx.js> → o navegador instala de novo e troca o cache.
const VERSAO = new URL(self.location.href).searchParams.get('v') || 'dev';
const CACHE = 'marcus-' + VERSAO;

self.addEventListener('install', (evento) => {
  evento.waitUntil((async () => {
    const cache = await caches.open(CACHE);
    let arquivos = [];
    try {
      const r = await fetch('/offline-arquivos.json', { cache: 'no-store' });
      if (r.ok) arquivos = await r.json();
    } catch { /* sem a lista: guarda só o que for sendo aberto */ }
    await cache.addAll(['/index.html', ...arquivos.map((a) => '/' + a)]).catch(() => cache.add('/index.html'));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (evento) => {
  evento.waitUntil((async () => {
    for (const nome of await caches.keys()) if (nome.startsWith('marcus-') && nome !== CACHE) await caches.delete(nome);
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (evento) => {
  const pedido = evento.request;
  const url = new URL(pedido.url);
  if (pedido.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;

  if (pedido.mode === 'navigate') {
    evento.respondWith((async () => {
      try {
        const r = await fetch(pedido);
        if (r.ok) (await caches.open(CACHE)).put('/index.html', r.clone());
        return r;
      } catch {
        return (await caches.match('/index.html')) || Response.error();
      }
    })());
    return;
  }

  evento.respondWith((async () => {
    const guardado = await caches.match(pedido);
    if (guardado) return guardado;
    const r = await fetch(pedido);
    if (r.ok && r.type === 'basic') (await caches.open(CACHE)).put(pedido, r.clone());
    return r;
  })());
});

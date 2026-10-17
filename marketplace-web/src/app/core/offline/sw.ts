import { isDevMode } from '@angular/core';

// Registra o service worker (public/sw.js) que deixa as telas abrirem sem internet. A versão é o nome do main-xxx.js:
// publicou de novo → nome novo → o navegador instala o service worker de novo e troca os arquivos guardados.
// Fora do ar no desenvolvimento (ng serve), para não atrapalhar a recarga automática.
export function registrarServiceWorker(): void {
  if (isDevMode() || !('serviceWorker' in navigator)) return;
  const main = document.querySelector<HTMLScriptElement>('script[src*="main-"]')?.src.split('/').pop() ?? 'sem-versao';
  navigator.serviceWorker.register(`/sw.js?v=${encodeURIComponent(main)}`).catch(() => {
    /* sem service worker: o sistema funciona igual, só não abre sem internet */
  });
}

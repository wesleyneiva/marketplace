// Módulos opcionais do sistema (liga/desliga para TODAS as empresas).
//
// Fiado (caderneta de clientes): desligado por enquanto — mercado grande não vende fiado.
// Desligado, some do menu, a rota /fiado não abre, o caixa não oferece a forma "Fiado" (Alt+5)
// nem o "Receber fiado" (F6). O código, a API e os dados continuam lá: para um mercadinho
// que precise, troque para `true` e publique.
export const MODULOS = {
  fiado: false,
} as const;

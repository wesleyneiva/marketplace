// Módulos do sistema.
//
// 1) MODULOS: liga/desliga para TODAS as empresas (decisão da plataforma).
// Fiado (caderneta de clientes): desligado por enquanto — mercado grande não vende fiado.
// Desligado, some do menu, a rota /fiado não abre, o caixa não oferece a forma "Fiado" (Alt+5)
// nem o "Receber fiado" (F6). O código, a API e os dados continuam lá: para um mercadinho
// que precise, troque para `true` e publique.
export const MODULOS = {
  fiado: false,
} as const;

// 2) Módulos liberados POR CLIENTE: o dono da plataforma escolhe na ficha do cliente (área do dono → Clientes →
// Módulos liberados): todos, ou um a um. A lista chega no login (usuario.empresa.modulos; null = todos) e o menu,
// as rotas e o painel seguem sozinhos. O servidor também barra a API (Seguranca/ModulosLiberados.cs).
// O básico (painel, caixa, produtos, estoque, usuários, configurações) é de todos.
export type Modulo = 'vendas' | 'compras' | 'financeiro' | 'contas' | 'relatorios' | 'importacao' | 'etiquetas' | 'fiado';

export const CATALOGO_MODULOS: { id: Modulo; nome: string; descricao: string }[] = [
  { id: 'vendas', nome: 'Vendas', descricao: 'Tela Vendas da gerência (lista do dia, cupom, cancelamentos). O caixa vende de qualquer jeito.' },
  { id: 'compras', nome: 'Compras e fornecedores', descricao: 'Pedidos de compra, sugestão do dia, entrada por NF-e e fornecedores.' },
  { id: 'financeiro', nome: 'Financeiro', descricao: 'Contas, lançamentos, DRE, fluxo de caixa e extrato do banco.' },
  { id: 'contas', nome: 'Contas a pagar', descricao: 'Boletos e despesas, vencimentos e pagamentos.' },
  { id: 'relatorios', nome: 'Relatórios', descricao: 'Relatórios de vendas, clima e insights.' },
  { id: 'importacao', nome: 'Importar planilha', descricao: 'Cadastro de produtos em lote por .xlsx ou .csv.' },
  { id: 'etiquetas', nome: 'Etiquetas de preço', descricao: 'Etiquetas de gôndola/prateleira com preço e código de barras.' },
  ...(MODULOS.fiado ? [{ id: 'fiado' as const, nome: 'Fiado', descricao: 'Caderneta de clientes e venda a prazo no caixa.' }] : []),
];

// O módulo está liberado para esta empresa? (null/undefined = todos). Fiado também precisa do MODULOS.fiado.
export function moduloLiberado(liberados: string[] | null | undefined, modulo: Modulo): boolean {
  if (modulo === 'fiado' && !MODULOS.fiado) return false;
  return !liberados || liberados.includes(modulo);
}

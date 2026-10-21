// Ramos de comércio atendidos (mesma lista da API: Models/Empresa.cs → Ramos). O sistema é um só: o ramo só muda
// palavras, o que fica à vista (balança, validade) e as categorias iniciais (essas, na API).
// O dono da plataforma escolhe o ramo no cadastro do cliente; chega no login (usuario.empresa.ramo).
// Uso nas telas:  protected readonly ramo = inject(AuthService).ramo;   →   {{ ramo().oNegocio }}, @if (ramo().balanca)
export type Ramo = 'mercado' | 'esporte';

export interface PerfilRamo {
  id: Ramo;
  nome: string;          // para a área do dono
  oNegocio: string;      // "o mercado" / "a loja"
  aberto: string;        // "mercado aberto" / "loja aberta"
  fechado: string;       // "mercado fechado" / "loja fechada"
  seuNegocio: string;    // exemplo de nome em campos ("SEU MERCADO")
  balanca: boolean;      // balança, PLU e etiqueta de peso
  validade: boolean;     // sugerir controle de validade (o produto que já controla continua mostrando)
  etiquetaPreco: string; // nome do formato de etiqueta 3 × 8
  grade: boolean;        // modelos com grade de tamanho e cor (core/api/modelos.api.ts)
  servicos: boolean;     // produtos do tipo serviço (encordoamento): vendem sem estoque
  exemploBusca: string;  // dica do campo de busca do caixa ("3*arroz = 3 unidades")
  exemploProduto: string; // exemplo de nome no cadastro de produto
  semCodigo: string;     // como chamar o produto sem código de barras na lista do caixa
}

export const RAMOS: Record<Ramo, PerfilRamo> = {
  mercado: {
    id: 'mercado', nome: 'Mercado', oNegocio: 'o mercado', aberto: 'mercado aberto', fechado: 'mercado fechado',
    seuNegocio: 'SEU MERCADO', balanca: true, validade: true, etiquetaPreco: 'Gôndola', grade: false, servicos: false,
    exemploBusca: '3*arroz = 3 unidades', exemploProduto: 'Arroz branco tipo 1 5kg', semCodigo: 'a granel',
  },
  esporte: {
    id: 'esporte', nome: 'Loja de esporte', oNegocio: 'a loja', aberto: 'loja aberta', fechado: 'loja fechada',
    seuNegocio: 'SUA LOJA', balanca: false, validade: false, etiquetaPreco: 'Prateleira', grade: true, servicos: true,
    exemploBusca: '3*bola = 3 tubos', exemploProduto: 'Bola Wilson US Open (tubo com 3)', semCodigo: 'sem código',
  },
};

export const LISTA_RAMOS = Object.values(RAMOS);

// Ramo desconhecido ou ausente (versão antiga da API em cache) = mercado, como era antes.
export function perfilRamo(ramo: string | null | undefined): PerfilRamo {
  return RAMOS[ramo as Ramo] ?? RAMOS.mercado;
}

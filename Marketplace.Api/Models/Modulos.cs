namespace Marketplace.Api.Models;

// Módulos que o dono da plataforma libera para cada cliente (área do dono → cliente → Módulos liberados).
// Empresa.Modulos vazio (null) = TODOS, inclusive os que forem criados depois; com lista = só os da lista.
// O básico (painel, caixa, produtos, estoque, usuários, configurações) é de todos e não entra aqui.
// A tela do cliente esconde o que não foi liberado (menu, rotas, cartões do painel) e o servidor barra a API
// de cada módulo (Seguranca/ModulosLiberados.cs) — esconder botão não basta.
public static class Modulos
{
    public const string Vendas = "vendas";         // tela Vendas (gerência); o caixa continua vendendo e cancelando
    public const string Compras = "compras";       // pedidos de compra, entrada por NF-e, fornecedores, compras automáticas
    public const string Financeiro = "financeiro"; // contas, lançamentos, DRE, fluxo de caixa, extrato do banco
    public const string Contas = "contas";         // contas a pagar
    public const string Relatorios = "relatorios"; // relatórios, clima e insights
    public const string Importacao = "importacao"; // importar produtos por planilha
    public const string Etiquetas = "etiquetas";   // etiquetas de gôndola
    public const string Fiado = "fiado";           // caderneta (também depende do MODULOS.fiado do Angular)

    public static readonly string[] Todos = [Vendas, Compras, Financeiro, Contas, Relatorios, Importacao, Etiquetas, Fiado];

    public static bool Liberado(string[]? liberados, string modulo) => liberados is null || liberados.Contains(modulo);

    // Lista que chega da tela: só nomes conhecidos, sem repetir, na ordem de Todos.
    public static string[] Normalizar(IEnumerable<string> lista) => Todos.Where(m => lista.Contains(m)).ToArray();
}

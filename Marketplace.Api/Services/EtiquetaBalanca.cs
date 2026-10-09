using Marketplace.Api.Models;

namespace Marketplace.Api.Services;

// Lê o código de barras da etiqueta da BALANÇA (açougue, hortifrúti, padaria). Padrão usado no Brasil (EAN-13 "de
// uso interno", que sempre começa com 2):
//
//   4 dígitos de código:  2 CCCC 0 VVVVVV D      ex.: 2 0042 0 001599 D → produto 42, R$ 15,99
//   5 dígitos de código:  2 CCCCC VVVVVV D       ex.: 2 00042 001599 D
//
// VVVVVV = preço total em centavos (etiqueta "Preco") ou peso em gramas (etiqueta "Peso"). D = dígito verificador.
public static class EtiquetaBalanca
{
    public record Leitura(int CodigoProduto, decimal Valor);

    // Parece etiqueta de balança? (13 dígitos começando com 2 — o resto do mundo não usa esse prefixo nos produtos.)
    public static bool Parece(string codigo) => codigo.Length == 13 && codigo[0] == '2' && codigo.All(char.IsAsciiDigit);

    public static Leitura? Ler(string codigo, int digitosCodigo, string tipo, out string? erro)
    {
        erro = null;
        if (!Parece(codigo)) { erro = "Não é uma etiqueta de balança."; return null; }
        if (!DigitoVerificadorOk(codigo)) { erro = "Etiqueta com o dígito verificador errado: passe o leitor de novo."; return null; }

        var produto = int.Parse(codigo.Substring(1, digitosCodigo == 5 ? 5 : 4));
        var bruto = int.Parse(codigo.Substring(6, 6));
        var valor = tipo == EtiquetaBalancaTipos.Peso ? bruto / 1000m : bruto / 100m; // gramas → kg · centavos → R$
        return new Leitura(produto, valor);
    }

    // Dígito verificador do EAN-13: pesos 1 e 3 alternados da esquerda para a direita.
    public static bool DigitoVerificadorOk(string codigo)
    {
        var soma = 0;
        for (var i = 0; i < 12; i++) soma += (codigo[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - soma % 10) % 10 == codigo[12] - '0';
    }

    // Monta uma etiqueta (para testes e para a balança simulada da tela de configurações).
    public static string Montar(int codigoProduto, int bruto, int digitosCodigo)
    {
        var corpo = digitosCodigo == 5 ? $"2{codigoProduto:D5}{bruto:D6}" : $"2{codigoProduto:D4}0{bruto:D6}";
        var soma = 0;
        for (var i = 0; i < 12; i++) soma += (corpo[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return corpo + (10 - soma % 10) % 10;
    }
}

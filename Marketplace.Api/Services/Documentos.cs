namespace Marketplace.Api.Services;

// CPF e CNPJ: confere os dígitos verificadores (só números).
public static class Documentos
{
    public static string SoDigitos(string? texto) => new((texto ?? "").Where(char.IsAsciiDigit).ToArray());

    public static bool CpfValido(string cpf) => Controllers.FiadoController.CpfValido(cpf);

    public static bool CnpjValido(string cnpj)
    {
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1) return false;
        int Digito(int tamanho)
        {
            int[] pesos = tamanho == 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
            var resto = Enumerable.Range(0, tamanho).Sum(i => (cnpj[i] - '0') * pesos[i]) % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        return Digito(12) == cnpj[12] - '0' && Digito(13) == cnpj[13] - '0';
    }

    // "51988887777" → "(51) 98888-7777" (para mostrar; guardado só com números).
    public static string? Telefone(string? digitos) => SoDigitos(digitos) switch
    {
        { Length: 11 } d => $"({d[..2]}) {d[2..7]}-{d[7..]}",
        { Length: 10 } d => $"({d[..2]}) {d[2..6]}-{d[6..]}",
        "" => null,
        var d => d,
    };

    // Vazio = sem documento (ok). Senão, CPF (11) ou CNPJ (14) válido.
    public static bool Valido(string digitos) =>
        digitos.Length == 0 || (digitos.Length == 11 ? CpfValido(digitos) : digitos.Length == 14 && CnpjValido(digitos));
}

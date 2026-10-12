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

    // Vazio = sem documento (ok). Senão, CPF (11) ou CNPJ (14) válido.
    public static bool Valido(string digitos) =>
        digitos.Length == 0 || (digitos.Length == 11 ? CpfValido(digitos) : digitos.Length == 14 && CnpjValido(digitos));
}

namespace Marketplace.Api.Services.Pix;

// O que o Mercado Pago e o banco respondem sobre um Pix, no mesmo "idioma": Status = pending, approved, expired,
// refunded ou cancelled. Referencia = a nossa (external_reference no Mercado Pago, txid no banco); EndToEndId = o número
// do Pix recebido (só o banco usa, para devolver).
public sealed record PagamentoPix(string Id, string Status, string? Detalhe, decimal Valor, string? Referencia,
    DateTimeOffset? AprovadoEm, string? CopiaECola, string? EndToEndId = null);

// Erro vindo do provedor (Mercado Pago ou banco), já com a mensagem para a tela.
public class ProvedorPixException(string mensagem) : Exception(mensagem);

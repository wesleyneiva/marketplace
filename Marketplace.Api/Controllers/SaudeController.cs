using Marketplace.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// GET /api/saude → diz se a API está no ar e se consegue falar com o banco.
// Útil para nós (testes) e, depois, para o n8n/agente vigiarem o sistema.
[ApiController]
[Route("api/[controller]")]
public class SaudeController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var bancoOk = await db.Database.CanConnectAsync();

        var resposta = new
        {
            api = "ok",
            banco = bancoOk ? "ok" : "sem conexão",
            horario = DateTimeOffset.Now
        };

        // 200 se está tudo certo; 503 (serviço indisponível) se o banco não respondeu.
        return bancoOk ? Ok(resposta) : StatusCode(StatusCodes.Status503ServiceUnavailable, resposta);
    }
}

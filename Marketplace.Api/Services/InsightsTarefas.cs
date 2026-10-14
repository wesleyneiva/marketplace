using Marketplace.Api.Contracts;
using System.Collections.Concurrent;
using Marketplace.Api.Seguranca;

namespace Marketplace.Api.Services;

// Os insights da IA rodam em SEGUNDO PLANO: quando o Google (Gemini) está sobrecarregado, a resposta pode levar minutos,
// e uma chamada única seria cortada (a Cloudflare corta em 100 s; o navegador também desiste). A tela pede (POST),
// recebe um número e pergunta de tempos em tempos (GET) até ficar pronto. Guardado em memória por 30 minutos.
public class InsightsTarefas(IServiceScopeFactory escopos, ILogger<InsightsTarefas> log)
{
    public record Tarefa(string Id, int EmpresaId, DateTimeOffset Iniciada)
    {
        public string Situacao { get; set; } = "andamento"; // andamento | pronto | erro
        public string? Texto { get; set; }
        public string? Erro { get; set; }
    }

    private readonly ConcurrentDictionary<string, Tarefa> _tarefas = new();

    public Tarefa Iniciar(int empresaId, Periodo periodo)
    {
        foreach (var velha in _tarefas.Values.Where(t => t.Iniciada < DateTimeOffset.UtcNow.AddMinutes(-30)))
            _tarefas.TryRemove(velha.Id, out _);

        var tarefa = new Tarefa(Guid.NewGuid().ToString("N"), empresaId, DateTimeOffset.UtcNow);
        _tarefas[tarefa.Id] = tarefa;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = escopos.CriarEscopo(empresaId);
                var insights = scope.ServiceProvider.GetRequiredService<InsightsService>();
                using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var (dados, prompt) = await insights.MontarAsync(periodo, "", limite.Token);
                var r = await insights.PedirAoN8nAsync(dados, prompt, limite.Token);
                tarefa.Texto = r.Ok ? r.Texto : null;
                tarefa.Erro = r.Ok ? null : r.Mensagem;
                tarefa.Situacao = r.Ok ? "pronto" : "erro";
            }
            catch (Exception e)
            {
                log.LogError(e, "Erro nos insights da empresa {Empresa}", empresaId);
                (tarefa.Erro, tarefa.Situacao) = ("Não foi possível gerar os insights agora. Tente de novo em alguns minutos.", "erro");
            }
        });
        return tarefa;
    }

    // Só a própria empresa enxerga a tarefa dela.
    public Tarefa? Obter(string id, int empresaId) =>
        _tarefas.TryGetValue(id, out var t) && t.EmpresaId == empresaId ? t : null;
}

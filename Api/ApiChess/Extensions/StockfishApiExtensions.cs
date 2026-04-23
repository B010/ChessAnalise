using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class StockfishApiExtensions
{
    public static IServiceCollection AddStockfishIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StockfishOptions>(configuration.GetSection("Stockfish"));
        services.AddSingleton<IStockfishService, StockfishService>();
        return services;
    }

    public static IEndpointRouteBuilder MapStockfishEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/engine/health", async (IStockfishService stockfishService, CancellationToken cancellationToken) =>
        {
            var health = await stockfishService.GetHealthAsync(cancellationToken);
            return health.Ready
                ? Results.Ok(health)
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("GetEngineHealth");

        endpoints.MapPost("/api/engine/evaluate", async (
            EvaluateFenRequest? request,
            IStockfishService stockfishService,
            IOptions<StockfishOptions> stockfishOptions,
            CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Fen))
            {
                return Results.BadRequest(new { message = "Informe um FEN valido para avaliacao." });
            }

            try
            {
                var result = await stockfishService.EvaluateFenAsync(request.Fen, request.Depth, request.MoveTimeMs, cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (FileNotFoundException ex)
            {
                return Results.Problem($"Stockfish nao encontrado em '{ex.FileName}'.", statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (TimeoutException)
            {
                return Results.Problem("Timeout na avaliacao da engine. Ajuste depth/movetime.", statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (OperationCanceledException)
            {
                return cancellationToken.IsCancellationRequested
                    ? Results.StatusCode(499)
                    : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception ex)
            {
                var options = stockfishOptions.Value;
                return Results.Problem(
                    $"Falha ao avaliar posicao com Stockfish. EnginePath: {options.EnginePath ?? "(nao configurado)"}. Erro: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("EvaluateFen");

        return endpoints;
    }
}

public interface IStockfishService : IAsyncDisposable
{
    Task<StockfishHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken);
    Task<StockfishEvaluationResult> EvaluateFenAsync(string fen, int? depth, int? moveTimeMs, CancellationToken cancellationToken);
}

public sealed record StockfishHealthSnapshot(
    bool Enabled,
    bool Ready,
    string? EnginePath,
    string? Message);

public sealed record StockfishEvaluationResult(
    string Fen,
    int? Cp,
    int? Mate,
    string BestMove,
    string? Ponder,
    int? Depth,
    string RawScore,
    long EvaluatedAtUnix);

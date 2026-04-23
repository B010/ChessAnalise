public sealed record EvaluateFenRequest(
    string Fen,
    int? Depth,
    int? MoveTimeMs);

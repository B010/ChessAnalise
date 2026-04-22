public sealed record RecentGameDto(
    long PlayedAtUnix,
    string Opponent,
    string Color,
    string Result,
    string ResultCode,
    string TimeClass,
    string OpeningFamily,
    string Opening,
    double? Accuracy,
    int FullMoves,
    string? GameUrl);

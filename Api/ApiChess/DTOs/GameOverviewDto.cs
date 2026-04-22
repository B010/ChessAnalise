public sealed record GameOverviewDto(
    long PlayedAtUnix,
    string Opponent,
    string Color,
    string Result,
    string ResultCode,
    string TimeClass,
    string? TimeControl,
    string OpeningFamily,
    string Opening,
    double? Accuracy,
    int FullMoves,
    string? GameUrl);

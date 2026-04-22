public sealed record WeeklyTrendDto(
    long WeekStartUnix,
    int Games,
    int Wins,
    int Draws,
    int Losses,
    double ScoreRate,
    double? AverageAccuracy);

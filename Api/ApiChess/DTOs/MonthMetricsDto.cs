public sealed record MonthMetricsDto(
    string Month,
    int Games,
    int Wins,
    int Draws,
    int Losses,
    double ScoreRate,
    double? AverageAccuracy);

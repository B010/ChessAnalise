public sealed record TimeClassStatsDto(
    string TimeClass,
    int Games,
    int Wins,
    int Draws,
    int Losses,
    double ScoreRate,
    double? AverageAccuracy);

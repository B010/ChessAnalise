public sealed record MonthComparisonDto(
    MonthMetricsDto? Current,
    MonthMetricsDto? Previous,
    double? ScoreRateDelta,
    double? AccuracyDelta,
    int? GamesDelta);

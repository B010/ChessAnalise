public sealed record AskAnalysisResponse(
    string Question,
    string Answer,
    int SampleSize,
    string? TimeClassFilter,
    long GeneratedAtUnix);

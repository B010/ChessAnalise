public sealed record GameAnalysisResponse(
    GameOverviewDto Overview,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Mistakes,
    IReadOnlyList<string> Improvements,
    string AiComment);

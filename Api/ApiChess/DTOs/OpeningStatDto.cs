public sealed record OpeningStatDto(
    string Name,
    int Games,
    int Wins,
    int Draws,
    int Losses,
    double ScoreRate,
    double LossRate,
    double SufferingIndex);

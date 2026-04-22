public sealed record SuccessSummaryDto(
    string BestColor,
    OpeningStatDto? BestOpening,
    PiecePressureDto? SafestPiece,
    PhasePerformanceDto? StrongestPhase,
    string? BestAccuracySide,
    IReadOnlyList<string> Highlights);

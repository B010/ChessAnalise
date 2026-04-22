public sealed record SevenDayPlanDto(
    string TimeClassFocus,
    double? BaselineAccuracy,
    IReadOnlyList<PlanDayItemDto> Days);

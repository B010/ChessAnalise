public sealed record ProfileStatsSummaryDto(
    int? RapidRating,
    int? BlitzRating,
    int? BulletRating,
    ModeStatsDto? Rapid,
    ModeStatsDto? Blitz,
    ModeStatsDto? Bullet);

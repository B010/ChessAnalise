public sealed record PlayerProfileDto(
    string Username,
    string? Name,
    string? Title,
    string? CountryUrl,
    string? Avatar,
    string? Url,
    int? Followers,
    long? JoinedUnix,
    long? LastOnlineUnix,
    string? Status);

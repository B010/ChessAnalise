public sealed record GameChatRequest(
    string? GameUrl,
    string? Question,
    IReadOnlyList<GameChatMessageInputDto>? History);

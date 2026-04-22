public sealed record GameChatResponse(
    string Question,
    string Answer,
    int HistoryCount,
    long GeneratedAtUnix);

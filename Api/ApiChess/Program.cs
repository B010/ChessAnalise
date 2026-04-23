using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("ChessCom", client =>
{
    client.BaseAddress = new Uri("https://api.chess.com/pub/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ChessAnalise/1.0");
    client.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontDev", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("FrontDev");
app.UseHttpsRedirection();

app.MapGet("/api/ping", () => Results.Ok("PONG")).WithName("Ping");

app.MapGet("/api/players/{username}", async (string username, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    try
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername is null)
        {
            return Results.BadRequest(new { message = "Informe um nickname valido." });
        }

        var client = httpClientFactory.CreateClient("ChessCom");
        var escapedUsername = Uri.EscapeDataString(normalizedUsername);

        var profileResponse = await GetWithRetryAsync(client, $"player/{escapedUsername}", cancellationToken);
        if (profileResponse is null)
        {
            return Results.Problem("Nao foi possivel consultar o perfil no Chess.com.", statusCode: StatusCodes.Status502BadGateway);
        }

        if (profileResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Jogador nao encontrado no Chess.com." });
        }

        if (!profileResponse.IsSuccessStatusCode)
        {
            return Results.Problem("Nao foi possivel consultar o perfil no Chess.com.");
        }

        await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var profileJson = await JsonDocument.ParseAsync(profileStream, cancellationToken: cancellationToken);

        var statsResponse = await GetWithRetryAsync(client, $"player/{escapedUsername}/stats", cancellationToken);
        JsonDocument? statsJson = null;
        if (statsResponse is not null && statsResponse.IsSuccessStatusCode)
        {
            await using var statsStream = await statsResponse.Content.ReadAsStreamAsync(cancellationToken);
            statsJson = await JsonDocument.ParseAsync(statsStream, cancellationToken: cancellationToken);
        }

        var player = BuildPlayerProfile(profileJson.RootElement, normalizedUsername);
        var profileStats = BuildProfileStats(statsJson?.RootElement);
        statsJson?.Dispose();

        return Results.Ok(new PlayerQuickResponse(player, profileStats));
    }
    catch (OperationCanceledException)
    {
        return cancellationToken.IsCancellationRequested
            ? Results.StatusCode(499)
            : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Falha de rede ao consultar o Chess.com.", statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("GetPlayerQuick");

app.MapGet("/api/players/{username}/analysis", async (
    string username,
    string? timeClass,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername is null)
        {
            return Results.BadRequest(new { message = "Informe um nickname valido." });
        }

        var client = httpClientFactory.CreateClient("ChessCom");
        var escapedUsername = Uri.EscapeDataString(normalizedUsername);
        var normalizedTimeClass = NormalizeTimeClass(timeClass);
        var cacheKey = $"analysis:{normalizedUsername.ToLowerInvariant()}:{normalizedTimeClass ?? "all"}";

        // if (memoryCache.TryGetValue(cacheKey, out PlayerDeepAnalysisResponse? cachedResponse) && cachedResponse is not null)
        // {
        //     return Results.Ok(cachedResponse);
        // }

        var profileResponse = await GetWithRetryAsync(client, $"player/{escapedUsername}", cancellationToken);
        if (profileResponse is null)
        {
            return Results.Problem("Nao foi possivel consultar o perfil no Chess.com.", statusCode: StatusCodes.Status502BadGateway);
        }

        if (profileResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Jogador nao encontrado no Chess.com." });
        }

        if (!profileResponse.IsSuccessStatusCode)
        {
            return Results.Problem("Nao foi possivel consultar o perfil no Chess.com.");
        }

        await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var profileJson = await JsonDocument.ParseAsync(profileStream, cancellationToken: cancellationToken);

        var statsResponse = await GetWithRetryAsync(client, $"player/{escapedUsername}/stats", cancellationToken);
        JsonDocument? statsJson = null;
        if (statsResponse is not null && statsResponse.IsSuccessStatusCode)
        {
            await using var statsStream = await statsResponse.Content.ReadAsStreamAsync(cancellationToken);
            statsJson = await JsonDocument.ParseAsync(statsStream, cancellationToken: cancellationToken);
        }

        var player = BuildPlayerProfile(profileJson.RootElement, normalizedUsername);
        var profileStats = BuildProfileStats(statsJson?.RootElement);

        var games = await FetchRecentGamesAsync(client, normalizedUsername, cancellationToken);
        var filteredGames = FilterGamesByTimeClass(games, normalizedTimeClass);
        if (filteredGames.Count == 0)
        {
            return Results.NotFound(new { message = "Sem partidas suficientes para o filtro selecionado." });
        }

        var byColor = BuildByColor(filteredGames);
        var openings = BuildOpenings(filteredGames);
        var piecePressure = BuildPiecePressure(filteredGames);
        var phasePressure = BuildPhasePressure(filteredGames);
        var phasePerformance = BuildPhasePerformance(filteredGames);
        var accuracy = BuildAccuracy(filteredGames);
        var successSummary = BuildSuccessSummary(byColor, openings, piecePressure, phasePerformance, accuracy);
        var recentGames = BuildRecentGames(filteredGames);
        var weeklyTrend = BuildWeeklyTrend(filteredGames);
        var timeClassBreakdown = BuildTimeClassBreakdown(games);
        var opponentRanges = BuildOpponentRangeSummary(filteredGames);
        var overallScore = BuildOverallScore(byColor, accuracy, openings, piecePressure);
        var openingRecommendations = BuildOpeningRecommendations(openings);
        var monthComparison = BuildMonthComparison(filteredGames);
        var confidence = BuildConfidenceSummary(filteredGames.Count, openings, normalizedTimeClass);
        var trainingPlan = BuildSevenDayTrainingPlan(successSummary, openings, piecePressure, phasePressure, accuracy, normalizedTimeClass);

        var themedTips = await BuildThemeTipsAsync(
            httpClientFactory,
            configuration,
            normalizedUsername,
            filteredGames.Count,
            openings,
            phasePressure,
            piecePressure,
            successSummary,
            cancellationToken);

        var aiTip = await BuildAiTipAsync(
            httpClientFactory,
            configuration,
            normalizedUsername,
            filteredGames.Count,
            profileStats,
            openings,
            byColor,
            piecePressure,
            phasePressure,
            successSummary,
            recentGames,
            accuracy,
            cancellationToken);

        statsJson?.Dispose();

        var response = new PlayerDeepAnalysisResponse(
            Player: player,
            ProfileStats: profileStats,
            Accuracy: accuracy,
            ByColor: byColor,
            Openings: openings,
            PiecePressure: piecePressure,
            PhasePressure: phasePressure,
            PhasePerformance: phasePerformance,
            SuccessSummary: successSummary,
            RecentGames: recentGames,
            OverallScore: overallScore,
            WeeklyTrend: weeklyTrend,
            TimeClassBreakdown: timeClassBreakdown,
            OpponentRanges: opponentRanges,
            OpeningRecommendations: openingRecommendations,
            TrainingPlan: trainingPlan,
            MonthComparison: monthComparison,
            ThemedTips: themedTips,
            Confidence: confidence,
            AiTip: aiTip,
            SampleSize: filteredGames.Count,
            TimeClassFilter: normalizedTimeClass,
            DataWindowMonths: 3,
            GeneratedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        memoryCache.Set(cacheKey, response, TimeSpan.FromMinutes(10));

        return Results.Ok(response);
    }
    catch (OperationCanceledException)
    {
        return cancellationToken.IsCancellationRequested
            ? Results.StatusCode(499)
            : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Falha de rede ao consultar o Chess.com.", statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("GetPlayerDeepAnalysis");

app.MapPost("/api/players/{username}/analysis/ask", async (
    string username,
    AskAnalysisRequest? request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername is null)
        {
            return Results.BadRequest(new { message = "Informe um nickname valido." });
        }

        var question = request?.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return Results.BadRequest(new { message = "Informe uma pergunta para a IA." });
        }

        if (question.Length > 420)
        {
            return Results.BadRequest(new { message = "Pergunta muito longa. Use ate 420 caracteres." });
        }

        var normalizedTimeClass = NormalizeTimeClass(request?.TimeClass);
        var client = httpClientFactory.CreateClient("ChessCom");
        var games = await FetchRecentGamesAsync(client, normalizedUsername, cancellationToken);
        var filteredGames = FilterGamesByTimeClass(games, normalizedTimeClass);

        if (filteredGames.Count == 0)
        {
            return Results.NotFound(new { message = "Sem partidas suficientes para responder com contexto." });
        }

        var profileStats = BuildProfileStats(null);
        var byColor = BuildByColor(filteredGames);
        var openings = BuildOpenings(filteredGames);
        var piecePressure = BuildPiecePressure(filteredGames);
        var phasePressure = BuildPhasePressure(filteredGames);
        var phasePerformance = BuildPhasePerformance(filteredGames);
        var accuracy = BuildAccuracy(filteredGames);
        var successSummary = BuildSuccessSummary(byColor, openings, piecePressure, phasePerformance, accuracy);
        var recentGames = BuildRecentGames(filteredGames);

        var answer = await BuildAnalysisQuestionAnswerAsync(
            httpClientFactory,
            configuration,
            normalizedUsername,
            question,
            filteredGames.Count,
            profileStats,
            openings,
            byColor,
            piecePressure,
            phasePressure,
            successSummary,
            recentGames,
            accuracy,
            cancellationToken);

        return Results.Ok(new AskAnalysisResponse(
            Question: question,
            Answer: answer,
            SampleSize: filteredGames.Count,
            TimeClassFilter: normalizedTimeClass,
            GeneratedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }
    catch (OperationCanceledException)
    {
        return cancellationToken.IsCancellationRequested
            ? Results.StatusCode(499)
            : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Falha de rede ao consultar o Chess.com.", statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("AskPlayerAnalysis");

app.MapGet("/api/players/{username}/game-analysis", async (
    string username,
    string gameUrl,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername is null)
        {
            return Results.BadRequest(new { message = "Informe um nickname valido." });
        }

        if (string.IsNullOrWhiteSpace(gameUrl))
        {
            return Results.BadRequest(new { message = "Informe a URL da partida para analise." });
        }

        var client = httpClientFactory.CreateClient("ChessCom");
        var games = await FetchRecentGamesAsync(client, normalizedUsername, cancellationToken);
        var target = FindGameByUrl(games, gameUrl);

        if (target is null)
        {
            return Results.NotFound(new { message = "Partida nao encontrada nos ultimos 3 meses." });
        }

        var openings = BuildOpenings(games);
        var piecePressure = BuildPiecePressure(games);
        var phasePressure = BuildPhasePressure(games);
        var overview = BuildGameOverview(target);
        var strengths = BuildGameStrengths(target, openings);
        var mistakes = BuildGameMistakes(target, openings, phasePressure, piecePressure);
        var improvements = BuildGameImprovements(target, openings, phasePressure, piecePressure);

        var aiComment = await BuildGameAiCommentAsync(
            httpClientFactory,
            configuration,
            normalizedUsername,
            overview,
            strengths,
            mistakes,
            improvements,
            target.SanMoves,
            cancellationToken);

        return Results.Ok(new GameAnalysisResponse(
            Overview: overview,
            Strengths: strengths,
            Mistakes: mistakes,
            Improvements: improvements,
            AiComment: aiComment));
    }
    catch (OperationCanceledException)
    {
        return cancellationToken.IsCancellationRequested
            ? Results.StatusCode(499)
            : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Falha de rede ao consultar o Chess.com.", statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("GetGameAnalysis");

app.MapPost("/api/players/{username}/game-analysis/chat", async (
    string username,
    GameChatRequest? request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        var normalizedUsername = NormalizeUsername(username);
        if (normalizedUsername is null)
        {
            return Results.BadRequest(new { message = "Informe um nickname valido." });
        }

        var gameUrl = request?.GameUrl?.Trim();
        if (string.IsNullOrWhiteSpace(gameUrl))
        {
            return Results.BadRequest(new { message = "Informe a URL da partida para conversar com a IA." });
        }

        var question = request?.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return Results.BadRequest(new { message = "Digite uma pergunta para o chat." });
        }

        if (question.Length > 700)
        {
            return Results.BadRequest(new { message = "Pergunta muito longa. Use ate 700 caracteres." });
        }

        var history = (request?.History ?? Array.Empty<GameChatMessageInputDto>())
            .Where(m => m is not null)
            .Select(m => new GameChatMessageInputDto(
                Role: string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                Content: (m.Content ?? string.Empty).Trim()))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(18)
            .ToList();

        var client = httpClientFactory.CreateClient("ChessCom");
        var games = await FetchRecentGamesAsync(client, normalizedUsername, cancellationToken);
        var target = FindGameByUrl(games, gameUrl);

        if (target is null)
        {
            return Results.NotFound(new { message = "Partida nao encontrada nos ultimos 3 meses." });
        }

        var openings = BuildOpenings(games);
        var piecePressure = BuildPiecePressure(games);
        var phasePressure = BuildPhasePressure(games);
        var overview = BuildGameOverview(target);
        var strengths = BuildGameStrengths(target, openings);
        var mistakes = BuildGameMistakes(target, openings, phasePressure, piecePressure);
        var improvements = BuildGameImprovements(target, openings, phasePressure, piecePressure);

        var answer = await BuildGameChatAnswerAsync(
            httpClientFactory,
            configuration,
            normalizedUsername,
            overview,
            strengths,
            mistakes,
            improvements,
            question,
            history,
            target.SanMoves,
            cancellationToken);

        return Results.Ok(new GameChatResponse(
            Question: question,
            Answer: answer,
            HistoryCount: history.Count + 2,
            GeneratedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }
    catch (OperationCanceledException)
    {
        return cancellationToken.IsCancellationRequested
            ? Results.StatusCode(499)
            : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Falha de rede ao consultar o Chess.com.", statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("ChatGameAnalysis");

app.Run();

static string? NormalizeUsername(string username)
{
    var normalized = username.Trim();
    return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
}

static string? NormalizeTimeClass(string? timeClass)
{
    if (string.IsNullOrWhiteSpace(timeClass))
    {
        return null;
    }

    var normalized = timeClass.Trim().ToLowerInvariant();
    return normalized switch
    {
        "rapid" or "blitz" or "bullet" or "daily" => normalized,
        "all" => null,
        _ => null
    };
}

static List<ParsedGame> FilterGamesByTimeClass(IEnumerable<ParsedGame> games, string? timeClass)
{
    return string.IsNullOrWhiteSpace(timeClass)
        ? games.ToList()
        : games.Where(g => g.TimeClass.Equals(timeClass, StringComparison.OrdinalIgnoreCase)).ToList();
}

static ParsedGame? FindGameByUrl(IEnumerable<ParsedGame> games, string gameUrl)
{
    var normalized = gameUrl.Trim();
    return games.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.GameUrl) &&
        string.Equals(g.GameUrl.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
}

static async Task<HttpResponseMessage?> GetWithRetryAsync(HttpClient client, string uri, CancellationToken cancellationToken)
{
    const int maxAttempts = 3;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var response = await client.GetAsync(uri, cancellationToken);
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (attempt == maxAttempts)
                {
                    return response;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                continue;
            }

            return response;
        }
        catch (HttpRequestException) when (attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }
    }

    return null;
}

static async Task<List<ParsedGame>> FetchRecentGamesAsync(HttpClient client, string username, CancellationToken cancellationToken)
{
    var escapedUsername = Uri.EscapeDataString(username);
    var archivesResponse = await GetWithRetryAsync(client, $"player/{escapedUsername}/games/archives", cancellationToken);
    if (archivesResponse is null || !archivesResponse.IsSuccessStatusCode)
    {
        return [];
    }

    await using var archivesStream = await archivesResponse.Content.ReadAsStreamAsync(cancellationToken);
    using var archivesJson = await JsonDocument.ParseAsync(archivesStream, cancellationToken: cancellationToken);
    if (!archivesJson.RootElement.TryGetProperty("archives", out var archivesElement) || archivesElement.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    var archiveUrls = archivesElement
        .EnumerateArray()
        .Select(x => x.GetString())
        .OfType<string>()
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .TakeLast(3)
        .ToArray();

    var parsedGames = new List<ParsedGame>();
    foreach (var archiveUrl in archiveUrls)
    {
        var gamesResponse = await GetWithRetryAsync(client, archiveUrl, cancellationToken);
        if (gamesResponse is null || !gamesResponse.IsSuccessStatusCode)
        {
            continue;
        }

        await using var gamesStream = await gamesResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var gamesJson = await JsonDocument.ParseAsync(gamesStream, cancellationToken: cancellationToken);

        if (!gamesJson.RootElement.TryGetProperty("games", out var gamesElement) || gamesElement.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        foreach (var game in gamesElement.EnumerateArray())
        {
            if (!TryParseGame(game, username, out var parsed))
            {
                continue;
            }

            parsedGames.Add(parsed);
        }
    }

    return parsedGames
        .OrderByDescending(x => x.EndTimeUnix)
        .Take(150)
        .ToList();
}

static bool TryParseGame(JsonElement game, string username, out ParsedGame parsed)
{
    parsed = default!;

    var whiteUsername = GetNestedString(game, "white", "username");
    var blackUsername = GetNestedString(game, "black", "username");
    var isWhite = string.Equals(whiteUsername, username, StringComparison.OrdinalIgnoreCase);
    var isBlack = string.Equals(blackUsername, username, StringComparison.OrdinalIgnoreCase);

    if (!isWhite && !isBlack)
    {
        return false;
    }

    if (GetString(game, "rules") is string rules && !rules.Equals("chess", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var side = isWhite ? "white" : "black";
    var resultCode = GetNestedString(game, side, "result") ?? string.Empty;
    var outcome = MapOutcome(resultCode);
    var opponentUsername = isWhite ? blackUsername : whiteUsername;
    var timeClass = GetString(game, "time_class") ?? "desconhecido";
    var timeControl = GetString(game, "time_control");
    var gameUrl = GetString(game, "url");
    var playerRating = GetNestedInt(game, side, "rating");
    var opponentRating = GetNestedInt(game, isWhite ? "black" : "white", "rating");

    var opening = NormalizeOpeningName(
        GetString(game, "eco")
        ?? ExtractOpeningFromEcoUrl(GetString(game, "eco_url"))
        ?? "Sem abertura identificada");

    var pgn = GetString(game, "pgn") ?? string.Empty;
    var sanMoves = ExtractSanMoves(pgn);

    var pieceCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < sanMoves.Count; i++)
    {
        var playerMove = isWhite ? i % 2 == 0 : i % 2 == 1;
        if (!playerMove)
        {
            continue;
        }

        var piece = GetPieceFromSan(sanMoves[i]);
        pieceCounters[piece] = pieceCounters.GetValueOrDefault(piece) + 1;
    }

    double? accuracy = null;
    if (game.TryGetProperty("accuracies", out var accuraciesNode))
    {
        var accuracyValue = isWhite ? GetString(accuraciesNode, "white") : GetString(accuraciesNode, "black");
        if (accuracyValue is not null && double.TryParse(accuracyValue, out var parsedAccuracy))
        {
            if (parsedAccuracy > 100)
            {
                parsedAccuracy /= 100;
            }

            accuracy = parsedAccuracy;
        }
    }

    parsed = new ParsedGame(
        IsWhite: isWhite,
        Outcome: outcome,
        Opening: opening,
        PlayerResultCode: resultCode,
        OpponentUsername: opponentUsername,
        TimeClass: timeClass,
        TimeControl: timeControl,
        GameUrl: gameUrl,
        PlayerRating: playerRating,
        OpponentRating: opponentRating,
        PlayerAccuracy: accuracy,
        PlayerPieceMoves: pieceCounters,
        SanMoves: sanMoves,
        PlyCount: sanMoves.Count,
        EndTimeUnix: GetLong(game, "end_time") ?? 0);

    return true;
}

static GameOutcome MapOutcome(string playerResult)
{
    if (playerResult.Equals("win", StringComparison.OrdinalIgnoreCase))
    {
        return GameOutcome.Win;
    }

    var drawCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "agreed",
        "repetition",
        "stalemate",
        "insufficient",
        "50move",
        "timevsinsufficient"
    };

    return drawCodes.Contains(playerResult) ? GameOutcome.Draw : GameOutcome.Loss;
}

static string GetPhaseFromPlyCount(int plyCount)
{
    if (plyCount <= 20)
    {
        return "Abertura";
    }

    if (plyCount <= 60)
    {
        return "Meio-jogo";
    }

    return "Final";
}

static PlayerProfileDto BuildPlayerProfile(JsonElement profileRoot, string fallbackUsername)
{
    return new PlayerProfileDto(
        Username: GetString(profileRoot, "username") ?? fallbackUsername,
        Name: GetString(profileRoot, "name"),
        Title: GetString(profileRoot, "title"),
        CountryUrl: GetString(profileRoot, "country"),
        Avatar: GetString(profileRoot, "avatar"),
        Url: GetString(profileRoot, "url"),
        Followers: GetInt(profileRoot, "followers"),
        JoinedUnix: GetLong(profileRoot, "joined"),
        LastOnlineUnix: GetLong(profileRoot, "last_online"),
        Status: GetString(profileRoot, "status"));
}

static ProfileStatsSummaryDto BuildProfileStats(JsonElement? statsRoot)
{
    var rapid = BuildModeStats(statsRoot, "chess_rapid");
    var blitz = BuildModeStats(statsRoot, "chess_blitz");
    var bullet = BuildModeStats(statsRoot, "chess_bullet");

    return new ProfileStatsSummaryDto(
        RapidRating: rapid?.Rating,
        BlitzRating: blitz?.Rating,
        BulletRating: bullet?.Rating,
        Rapid: rapid,
        Blitz: blitz,
        Bullet: bullet);
}

static ColorSplitDto BuildByColor(IEnumerable<ParsedGame> games)
{
    return new ColorSplitDto(
        White: BuildColorStats(games.Where(g => g.IsWhite)),
        Black: BuildColorStats(games.Where(g => !g.IsWhite)));
}

static ColorStatsDto BuildColorStats(IEnumerable<ParsedGame> games)
{
    var list = games.ToList();
    var wins = list.Count(g => g.Outcome == GameOutcome.Win);
    var draws = list.Count(g => g.Outcome == GameOutcome.Draw);
    var losses = list.Count(g => g.Outcome == GameOutcome.Loss);
    var total = list.Count;

    var winRate = total > 0
        ? Math.Round((double)wins / total * 100, 1)
        : 0;

    return new ColorStatsDto(wins, draws, losses, total, winRate);
}

static OpeningSummaryDto BuildOpenings(IEnumerable<ParsedGame> games)
{
    var openingStats = games
        .GroupBy(x => NormalizeOpeningFamily(x.Opening))
        .Select(group =>
        {
            var wins = group.Count(x => x.Outcome == GameOutcome.Win);
            var draws = group.Count(x => x.Outcome == GameOutcome.Draw);
            var losses = group.Count(x => x.Outcome == GameOutcome.Loss);
            var total = group.Count();
            var scoreRate = total > 0
                ? Math.Round(((wins + (draws * 0.5)) / total) * 100, 1)
                : 0;
            var lossRate = total > 0
                ? Math.Round((double)losses / total * 100, 1)
                : 0;

            // Penaliza amostras pequenas para evitar supervalorizar 1-2 derrotas.
            var confidenceWeight = Math.Min(1d, total / 8d);
            var sufferingIndex = Math.Round(lossRate * confidenceWeight, 1);

            return new OpeningStatDto(group.Key, total, wins, draws, losses, scoreRate, lossRate, sufferingIndex);
        })
        .Where(x => x.Games >= 2)
        .ToList();

    var bestCandidates = openingStats.Where(x => x.Games >= 6).ToList();
    if (bestCandidates.Count == 0)
    {
        bestCandidates = openingStats.Where(x => x.Games >= 4).ToList();
    }
    if (bestCandidates.Count == 0)
    {
        bestCandidates = openingStats;
    }

    var best = bestCandidates
        .OrderByDescending(x => x.ScoreRate)
        .ThenByDescending(x => x.Games)
        .Take(4)
        .ToList();

    var bestNames = best.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    var worstCandidates = openingStats
        .Where(x => x.Games >= 6)
        .Where(x => x.Losses > 0)
        .ToList();

    if (worstCandidates.Count == 0)
    {
        worstCandidates = openingStats
            .Where(x => x.Games >= 4)
            .Where(x => x.Losses > 0)
            .ToList();
    }

    if (worstCandidates.Count == 0)
    {
        worstCandidates = openingStats.Where(x => x.Losses > 0).ToList();
    }

    if (worstCandidates.Count == 0)
    {
        worstCandidates = openingStats;
    }

    var worstWithoutBest = worstCandidates
        .Where(x => !bestNames.Contains(x.Name))
        .ToList();

    if (worstWithoutBest.Count >= 2)
    {
        worstCandidates = worstWithoutBest;
    }

    var worst = worstCandidates
        .OrderByDescending(x => x.SufferingIndex)
        .ThenByDescending(x => x.LossRate)
        .ThenByDescending(x => x.Games)
        .Take(4)
        .ToList();

    return new OpeningSummaryDto(best, worst);
}

static List<PiecePressureDto> BuildPiecePressure(IEnumerable<ParsedGame> games)
{
    var pieces = new[] { "Peao", "Cavalo", "Bispo", "Torre", "Dama", "Rei" };
    var gamesList = games.ToList();

    var list = new List<PiecePressureDto>();
    foreach (var piece in pieces)
    {
        var movesInLoss = gamesList
            .Where(g => g.Outcome == GameOutcome.Loss)
            .Sum(g => g.PlayerPieceMoves.GetValueOrDefault(piece));

        var totalMoves = gamesList
            .Sum(g => g.PlayerPieceMoves.GetValueOrDefault(piece));

        if (totalMoves == 0)
        {
            continue;
        }

        var riskRate = Math.Round((double)movesInLoss / totalMoves * 100, 1);
        list.Add(new PiecePressureDto(piece, totalMoves, movesInLoss, riskRate));
    }

    return list
        .OrderByDescending(x => x.RiskRate)
        .ToList();
}

static List<PhasePressureDto> BuildPhasePressure(IEnumerable<ParsedGame> games)
{
    return games
        .Where(g => g.Outcome == GameOutcome.Loss)
        .GroupBy(g => GetPhaseFromPlyCount(g.PlyCount))
        .Select(group => new PhasePressureDto(group.Key, group.Count()))
        .OrderByDescending(x => x.Losses)
        .ToList();
}

static IReadOnlyList<RecentGameDto> BuildRecentGames(IEnumerable<ParsedGame> games)
{
    return games
        .OrderByDescending(g => g.EndTimeUnix)
        .Take(5)
        .Select(g => new RecentGameDto(
            PlayedAtUnix: g.EndTimeUnix,
            Opponent: g.OpponentUsername ?? "Desconhecido",
            Color: g.IsWhite ? "Brancas" : "Pretas",
            Result: MapOutcomeLabel(g.Outcome),
            ResultCode: g.PlayerResultCode,
            TimeClass: g.TimeClass,
            OpeningFamily: NormalizeOpeningFamily(g.Opening),
            Opening: g.Opening,
            Accuracy: g.PlayerAccuracy,
            FullMoves: Math.Max(1, (int)Math.Ceiling(g.PlyCount / 2d)),
            GameUrl: g.GameUrl))
        .ToList();
}

static string MapOutcomeLabel(GameOutcome outcome)
{
    return outcome switch
    {
        GameOutcome.Win => "Vitoria",
        GameOutcome.Draw => "Empate",
        _ => "Derrota"
    };
}

static List<PhasePerformanceDto> BuildPhasePerformance(IEnumerable<ParsedGame> games)
{
    return games
        .GroupBy(g => GetPhaseFromPlyCount(g.PlyCount))
        .Select(group =>
        {
            var wins = group.Count(x => x.Outcome == GameOutcome.Win);
            var draws = group.Count(x => x.Outcome == GameOutcome.Draw);
            var losses = group.Count(x => x.Outcome == GameOutcome.Loss);
            var total = group.Count();
            var scoreRate = total > 0
                ? Math.Round(((wins + (draws * 0.5)) / total) * 100, 1)
                : 0;

            return new PhasePerformanceDto(group.Key, total, wins, draws, losses, scoreRate);
        })
        .OrderByDescending(x => x.ScoreRate)
        .ThenByDescending(x => x.Games)
        .ToList();
}

static SuccessSummaryDto BuildSuccessSummary(
    ColorSplitDto byColor,
    OpeningSummaryDto openings,
    IReadOnlyList<PiecePressureDto> piecePressure,
    IReadOnlyList<PhasePerformanceDto> phasePerformance,
    AccuracySummaryDto accuracy)
{
    var bestColor = byColor.White.WinRate >= byColor.Black.WinRate ? "Brancas" : "Pretas";
    var bestOpening = openings.Best.FirstOrDefault();
    var safestPiece = piecePressure.OrderBy(x => x.RiskRate).FirstOrDefault();
    var strongestPhase = phasePerformance.FirstOrDefault();

    string? bestAccuracySide = null;
    if (accuracy.WhiteAverage is not null || accuracy.BlackAverage is not null)
    {
        bestAccuracySide = (accuracy.WhiteAverage ?? double.MinValue) >= (accuracy.BlackAverage ?? double.MinValue)
            ? "Brancas"
            : "Pretas";
    }

    var highlights = new List<string>
    {
        $"Melhor desempenho por cor: {bestColor}."
    };

    if (bestOpening is not null)
    {
        highlights.Add($"Abertura mais eficiente: {bestOpening.Name} ({bestOpening.ScoreRate}% em {bestOpening.Games} jogos)." );
    }

    if (safestPiece is not null)
    {
        highlights.Add($"Peca mais segura nas decisoes recentes: {safestPiece.Piece}." );
    }

    if (strongestPhase is not null)
    {
        highlights.Add($"Fase mais forte: {strongestPhase.Phase} ({strongestPhase.ScoreRate}% de aproveitamento)." );
    }

    if (bestAccuracySide is not null)
    {
        highlights.Add($"Melhor precisao media com: {bestAccuracySide}." );
    }

    return new SuccessSummaryDto(
        BestColor: bestColor,
        BestOpening: bestOpening,
        SafestPiece: safestPiece,
        StrongestPhase: strongestPhase,
        BestAccuracySide: bestAccuracySide,
        Highlights: highlights.Take(5).ToArray());
}

static AccuracySummaryDto BuildAccuracy(IEnumerable<ParsedGame> games)
{
    var list = games.ToList();
    var all = list.Where(g => g.PlayerAccuracy is not null).Select(g => g.PlayerAccuracy!.Value).ToList();
    var whites = list.Where(g => g.IsWhite && g.PlayerAccuracy is not null).Select(g => g.PlayerAccuracy!.Value).ToList();
    var blacks = list.Where(g => !g.IsWhite && g.PlayerAccuracy is not null).Select(g => g.PlayerAccuracy!.Value).ToList();

    return new AccuracySummaryDto(
        OverallAverage: AvgOrNull(all),
        WhiteAverage: AvgOrNull(whites),
        BlackAverage: AvgOrNull(blacks),
        GamesWithAccuracy: all.Count);
}

static OverallScoreDto BuildOverallScore(
    ColorSplitDto byColor,
    AccuracySummaryDto accuracy,
    OpeningSummaryDto openings,
    IReadOnlyList<PiecePressureDto> piecePressure)
{
    var totalGames = byColor.White.TotalGames + byColor.Black.TotalGames;
    var wins = byColor.White.Wins + byColor.Black.Wins;
    var draws = byColor.White.Draws + byColor.Black.Draws;
    var scoreRate = totalGames > 0 ? ((wins + (draws * 0.5)) / totalGames) * 100 : 0;
    var accuracyRate = accuracy.OverallAverage ?? 75;
    var riskPenalty = piecePressure.FirstOrDefault()?.RiskRate ?? 15;
    var openingBalance = openings.Best.FirstOrDefault()?.ScoreRate ?? 60;

    var value = Math.Round((scoreRate * 0.4) + (accuracyRate * 0.35) + ((100 - riskPenalty) * 0.15) + (openingBalance * 0.10), 1);
    var level = value switch
    {
        >= 85 => "Excelente",
        >= 72 => "Bom",
        >= 60 => "Em evolucao",
        _ => "Instavel"
    };

    return new OverallScoreDto(value, level, "Indice combinado entre resultado, precisao e consistencia.");
}

static IReadOnlyList<WeeklyTrendDto> BuildWeeklyTrend(IEnumerable<ParsedGame> games)
{
    return games
        .GroupBy(g => GetWeekStartUnix(g.EndTimeUnix))
        .OrderBy(group => group.Key)
        .TakeLast(12)
        .Select(group =>
        {
            var wins = group.Count(x => x.Outcome == GameOutcome.Win);
            var draws = group.Count(x => x.Outcome == GameOutcome.Draw);
            var losses = group.Count(x => x.Outcome == GameOutcome.Loss);
            var total = group.Count();
            var scoreRate = total > 0
                ? Math.Round(((wins + (draws * 0.5)) / total) * 100, 1)
                : 0;
            var avgAccuracy = AvgOrNull(group.Where(x => x.PlayerAccuracy is not null).Select(x => x.PlayerAccuracy!.Value).ToList());

            return new WeeklyTrendDto(group.Key, total, wins, draws, losses, scoreRate, avgAccuracy);
        })
        .ToList();
}

static long GetWeekStartUnix(long endTimeUnix)
{
    var dt = DateTimeOffset.FromUnixTimeSeconds(endTimeUnix).UtcDateTime.Date;
    var diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
    var monday = dt.AddDays(-diff);
    return new DateTimeOffset(monday, TimeSpan.Zero).ToUnixTimeSeconds();
}

static IReadOnlyList<TimeClassStatsDto> BuildTimeClassBreakdown(IEnumerable<ParsedGame> games)
{
    return games
        .GroupBy(g => g.TimeClass)
        .Select(group =>
        {
            var wins = group.Count(x => x.Outcome == GameOutcome.Win);
            var draws = group.Count(x => x.Outcome == GameOutcome.Draw);
            var losses = group.Count(x => x.Outcome == GameOutcome.Loss);
            var total = group.Count();
            var scoreRate = total > 0 ? Math.Round(((wins + (draws * 0.5)) / total) * 100, 1) : 0;
            var avgAcc = AvgOrNull(group.Where(x => x.PlayerAccuracy is not null).Select(x => x.PlayerAccuracy!.Value).ToList());
            return new TimeClassStatsDto(group.Key, total, wins, draws, losses, scoreRate, avgAcc);
        })
        .OrderByDescending(x => x.Games)
        .ToList();
}

static OpponentRangeSummaryDto BuildOpponentRangeSummary(IEnumerable<ParsedGame> games)
{
    var buckets = new List<OpponentRangeDto>();
    var grouped = games
        .Where(g => g.PlayerRating is not null && g.OpponentRating is not null)
        .GroupBy(g => GetOpponentRangeLabel((g.OpponentRating!.Value - g.PlayerRating!.Value)))
        .ToList();

    foreach (var group in grouped)
    {
        var wins = group.Count(x => x.Outcome == GameOutcome.Win);
        var draws = group.Count(x => x.Outcome == GameOutcome.Draw);
        var losses = group.Count(x => x.Outcome == GameOutcome.Loss);
        var total = group.Count();
        var scoreRate = total > 0 ? Math.Round(((wins + (draws * 0.5)) / total) * 100, 1) : 0;
        buckets.Add(new OpponentRangeDto(group.Key, total, wins, draws, losses, scoreRate));
    }

    return new OpponentRangeSummaryDto(buckets.OrderByDescending(x => x.Games).ToList());
}

static string GetOpponentRangeLabel(int ratingDiff)
{
    return ratingDiff switch
    {
        >= 200 => "Acima (+200 ou mais)",
        <= -200 => "Abaixo (-200 ou mais)",
        _ => "Parelho (-199 a +199)"
    };
}

static IReadOnlyList<OpeningRecommendationDto> BuildOpeningRecommendations(OpeningSummaryDto openings)
{
    var list = new List<OpeningRecommendationDto>();

    list.AddRange(openings.Best.Take(3).Select(o => new OpeningRecommendationDto(
        o.Name,
        "Manter",
        $"Continue no repertorio principal ({o.ScoreRate}% em {o.Games} jogos).",
        GetConfidenceLabel(o.Games))));

    list.AddRange(openings.Worst.Take(3).Select(o => new OpeningRecommendationDto(
        o.Name,
        o.SufferingIndex >= 20 ? "Evitar por enquanto" : "Revisar",
        $"Foco tatico nessa familia. Indice de sofrimento {o.SufferingIndex}%.",
        GetConfidenceLabel(o.Games))));

    return list;
}

static MonthComparisonDto BuildMonthComparison(IEnumerable<ParsedGame> games)
{
    var grouped = games
        .GroupBy(g => DateTimeOffset.FromUnixTimeSeconds(g.EndTimeUnix).UtcDateTime.ToString("yyyy-MM"))
        .OrderBy(g => g.Key)
        .ToList();

    if (grouped.Count == 0)
    {
        return new MonthComparisonDto(null, null, null, null, null);
    }

    var current = BuildMonthMetrics(grouped.Last());
    var previous = grouped.Count >= 2 ? BuildMonthMetrics(grouped[^2]) : null;

    double? scoreDelta = previous is null ? null : Math.Round(current.ScoreRate - previous.ScoreRate, 1);
    double? accuracyDelta = previous is null || current.AverageAccuracy is null || previous.AverageAccuracy is null
        ? null
        : Math.Round(current.AverageAccuracy.Value - previous.AverageAccuracy.Value, 1);

    return new MonthComparisonDto(current, previous, scoreDelta, accuracyDelta, previous is null ? null : current.Games - previous.Games);
}

static MonthMetricsDto BuildMonthMetrics(IGrouping<string, ParsedGame> group)
{
    var wins = group.Count(x => x.Outcome == GameOutcome.Win);
    var draws = group.Count(x => x.Outcome == GameOutcome.Draw);
    var losses = group.Count(x => x.Outcome == GameOutcome.Loss);
    var total = group.Count();
    var scoreRate = total > 0 ? Math.Round(((wins + (draws * 0.5)) / total) * 100, 1) : 0;
    var avgAcc = AvgOrNull(group.Where(x => x.PlayerAccuracy is not null).Select(x => x.PlayerAccuracy!.Value).ToList());
    return new MonthMetricsDto(group.Key, total, wins, draws, losses, scoreRate, avgAcc);
}

static ConfidenceSummaryDto BuildConfidenceSummary(int sampleSize, OpeningSummaryDto openings, string? timeClassFilter)
{
    var openingGames = openings.Worst.FirstOrDefault()?.Games ?? openings.Best.FirstOrDefault()?.Games ?? 0;
    return new ConfidenceSummaryDto(
        SampleLabel: GetConfidenceLabel(sampleSize),
        OpeningsLabel: GetConfidenceLabel(openingGames),
        FilterLabel: string.IsNullOrWhiteSpace(timeClassFilter) ? "Todos os ritmos" : timeClassFilter);
}

static string GetConfidenceLabel(int games)
{
    return games switch
    {
        >= 30 => "Alta",
        >= 12 => "Media",
        >= 5 => "Baixa",
        _ => "Muito baixa"
    };
}

static SevenDayPlanDto BuildSevenDayTrainingPlan(
    SuccessSummaryDto successSummary,
    OpeningSummaryDto openings,
    IReadOnlyList<PiecePressureDto> piecePressure,
    IReadOnlyList<PhasePressureDto> phasePressure,
    AccuracySummaryDto accuracy,
    string? timeClassFilter)
{
    var weakestOpening = openings.Worst.FirstOrDefault()?.Name ?? "abertura principal";
    var riskyPiece = piecePressure.FirstOrDefault()?.Piece ?? "pecas menores";
    var weakPhase = phasePressure.FirstOrDefault()?.Phase ?? "Meio-jogo";
    var strengthsAnchor = successSummary.BestOpening?.Name ?? "sua melhor abertura";

    var items = new List<PlanDayItemDto>
    {
        new("Dia 1", "Base de abertura", $"Revisar 3 ideias da familia {weakestOpening} e montar plano simples de lances."),
        new("Dia 2", "Precisao", $"Analisar 2 derrotas recentes e marcar o primeiro momento de queda de avaliacao."),
        new("Dia 3", "Peca critica", $"Treinar 20 taticas com foco em decisoes envolvendo {riskyPiece}."),
        new("Dia 4", "Fase fragil", $"Fazer 3 exercicios especificos de {weakPhase} com controle de tempo."),
        new("Dia 5", "Forca atual", $"Jogar e revisar uma partida repetindo principios da sua forca: {strengthsAnchor}."),
        new("Dia 6", "Consistencia", "Jogar 3 partidas no ritmo foco e revisar apenas erros forçados."),
        new("Dia 7", "Fechamento", "Comparar desempenho da semana e ajustar repertorio para o proximo ciclo.")
    };

    return new SevenDayPlanDto(timeClassFilter ?? "all", accuracy.OverallAverage, items);
}

static async Task<ThemeTipsDto> BuildThemeTipsAsync(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    string username,
    int sampleSize,
    OpeningSummaryDto openings,
    IReadOnlyList<PhasePressureDto> phasePressure,
    IReadOnlyList<PiecePressureDto> piecePressure,
    SuccessSummaryDto successSummary,
    CancellationToken cancellationToken)
{
    var fallback = BuildThemeTipsFallback(openings, phasePressure, piecePressure, successSummary);
    var apiKey = configuration["OpenAiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    try
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var prompt = $"""
        Gere 4 dicas curtas em portugues separadas por || nos temas: abertura || meio-jogo || final || decisao.
        Jogador: {username}. Amostra: {sampleSize}.
        Melhor abertura: {successSummary.BestOpening?.Name}. Pior abertura: {openings.Worst.FirstOrDefault()?.Name}.
        Fase critica: {phasePressure.FirstOrDefault()?.Phase}. Peca sob risco: {piecePressure.FirstOrDefault()?.Piece}.
        """;

        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.4,
            messages = new object[]
            {
                new { role = "system", content = "Seja objetivo." },
                new { role = "user", content = prompt }
            }
        };

        var response = await http.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return fallback;
        }

        var parts = content.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return fallback;
        }

        return new ThemeTipsDto(parts[0], parts[1], parts[2], parts[3]);
    }
    catch
    {
        return fallback;
    }
}

static ThemeTipsDto BuildThemeTipsFallback(
    OpeningSummaryDto openings,
    IReadOnlyList<PhasePressureDto> phasePressure,
    IReadOnlyList<PiecePressureDto> piecePressure,
    SuccessSummaryDto successSummary)
{
    var worstOpening = openings.Worst.FirstOrDefault()?.Name ?? "abertura principal";
    var weakPhase = phasePressure.FirstOrDefault()?.Phase ?? "meio-jogo";
    var riskyPiece = piecePressure.FirstOrDefault()?.Piece ?? "peca principal";

    return new ThemeTipsDto(
        Opening: $"Mantenha {successSummary.BestOpening?.Name ?? "sua linha mais estavel"} e revise planos da familia {worstOpening}.",
        Middlegame: $"No {weakPhase}, reduza lances forçados e priorize melhoria de peca antes de ataque.",
        Endgame: "Treine finais tecnicos basicos (rei e peoes, torres) 15 min por dia para conversao limpa.",
        Decision: $"Antes de jogar com {riskyPiece}, valide 2 lances do adversario para reduzir erros nao forçados.");
}

static GameOverviewDto BuildGameOverview(ParsedGame game)
{
    return new GameOverviewDto(
        PlayedAtUnix: game.EndTimeUnix,
        Opponent: game.OpponentUsername ?? "Desconhecido",
        Color: game.IsWhite ? "Brancas" : "Pretas",
        Result: MapOutcomeLabel(game.Outcome),
        ResultCode: game.PlayerResultCode,
        TimeClass: game.TimeClass,
        TimeControl: game.TimeControl,
        OpeningFamily: NormalizeOpeningFamily(game.Opening),
        Opening: game.Opening,
        Accuracy: game.PlayerAccuracy,
        FullMoves: Math.Max(1, (int)Math.Ceiling(game.PlyCount / 2d)),
        GameUrl: game.GameUrl);
}

static IReadOnlyList<string> BuildGameStrengths(ParsedGame game, OpeningSummaryDto openings)
{
    var strengths = new List<string>();
    if (game.Outcome == GameOutcome.Win)
    {
        strengths.Add("Converteu a partida em resultado positivo.");
    }

    if (game.PlayerAccuracy is double acc && acc >= 85)
    {
        strengths.Add($"Precisao alta na partida ({Math.Round(acc, 1)}%).");
    }

    if (openings.Best.Any(x => x.Name.Equals(NormalizeOpeningFamily(game.Opening), StringComparison.OrdinalIgnoreCase)))
    {
        strengths.Add("Partida em familia de abertura onde voce historicamente performa bem.");
    }

    if (strengths.Count == 0)
    {
        strengths.Add("Manteve estrutura competitiva durante boa parte da partida.");
    }

    return strengths;
}

static IReadOnlyList<string> BuildGameMistakes(
    ParsedGame game,
    OpeningSummaryDto openings,
    IReadOnlyList<PhasePressureDto> phasePressure,
    IReadOnlyList<PiecePressureDto> piecePressure)
{
    var mistakes = new List<string>();
    var phase = GetPhaseFromPlyCount(game.PlyCount);

    if (game.Outcome == GameOutcome.Loss)
    {
        mistakes.Add($"Resultado final negativo ({game.PlayerResultCode}).");
    }

    if (game.PlayerAccuracy is double acc && acc < 78)
    {
        mistakes.Add($"Precisao abaixo do ideal ({Math.Round(acc, 1)}%).");
    }

    if (phasePressure.FirstOrDefault()?.Phase == phase)
    {
        mistakes.Add($"Erros concentrados na fase em que voce mais sofre: {phase}.");
    }

    if (openings.Worst.Any(x => x.Name.Equals(NormalizeOpeningFamily(game.Opening), StringComparison.OrdinalIgnoreCase)))
    {
        mistakes.Add("Abertura da partida esta entre as familias de maior sofrimento estatistico.");
    }

    var riskyPiece = piecePressure.FirstOrDefault();
    if (riskyPiece is not null)
    {
        mistakes.Add($"Atencao especial nas decisoes com {riskyPiece.Piece} em momentos taticos.");
    }

    return mistakes.Take(4).ToList();
}

static IReadOnlyList<string> BuildGameImprovements(
    ParsedGame game,
    OpeningSummaryDto openings,
    IReadOnlyList<PhasePressureDto> phasePressure,
    IReadOnlyList<PiecePressureDto> piecePressure)
{
    var improvements = new List<string>();
    var worstOpening = openings.Worst.FirstOrDefault();
    if (worstOpening is not null)
    {
        improvements.Add($"Revisar planos da familia {worstOpening.Name} com 3 ideias-chave antes do proximo jogo.");
    }

    var weakPhase = phasePressure.FirstOrDefault()?.Phase;
    if (weakPhase is not null)
    {
        improvements.Add($"Fazer treino curto focado em {weakPhase} (20 min) para reduzir quedas de avaliacao.");
    }

    var riskyPiece = piecePressure.FirstOrDefault()?.Piece;
    if (riskyPiece is not null)
    {
        improvements.Add($"Em lances com {riskyPiece}, aplique checklist: ameaca, troca, e casa de fuga.");
    }

    if (game.PlayerAccuracy is double acc && acc < 82)
    {
        improvements.Add("Reanalisar a partida e marcar o primeiro lance que muda o rumo, criando uma regra pratica para evitar repeticao.");
    }

    return improvements.Take(4).ToList();
}

static async Task<string> BuildGameAiCommentAsync(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    string username,
    GameOverviewDto overview,
    IReadOnlyList<string> strengths,
    IReadOnlyList<string> mistakes,
    IReadOnlyList<string> improvements,
    IReadOnlyList<string> sanMoves,
    CancellationToken cancellationToken)
{
    var fallback = $"Partida de {overview.Result.ToLowerInvariant()} em {overview.TimeClass}. Melhorias imediatas: {string.Join(" ", improvements.Take(2))}";
    var apiKey = configuration["OpenAiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    try
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        var movesContext = BuildMovesContextForAi(sanMoves);
        var prompt = $"""
        Analise esta partida de xadrez em portugues em ate 4 frases.
        Jogador: {username}
        Contexto: {overview.Result} de {overview.Color} na abertura {overview.OpeningFamily}, precisao {overview.Accuracy}%.
        Lances da partida (resumo): {movesContext}
        Pontos fortes: {string.Join(" | ", strengths)}
        Erros: {string.Join(" | ", mistakes)}
        Melhorias: {string.Join(" | ", improvements)}
        """;

        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.78,
            frequency_penalty = 0.35,
            presence_penalty = 0.2,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce e um treinador humano de xadrez: fale de forma natural, sem texto engessado e sem repetir estrutura fixa. Use os dados so como base, mas responda como conversa real com o jogador."
                },
                new { role = "user", content = prompt }
            }
        };

        var response = await http.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(content) ? fallback : content.Trim();
    }
    catch
    {
        return fallback;
    }
}

static async Task<string> BuildGameChatAnswerAsync(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    string username,
    GameOverviewDto overview,
    IReadOnlyList<string> strengths,
    IReadOnlyList<string> mistakes,
    IReadOnlyList<string> improvements,
    string question,
    IReadOnlyList<GameChatMessageInputDto> history,
    IReadOnlyList<string> sanMoves,
    CancellationToken cancellationToken)
{
    var fallback = BuildGameChatFallback(question, overview, strengths, improvements, mistakes, sanMoves);
    var apiKey = configuration["OpenAiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    try
    {
        var movesContext = BuildMovesContextForAi(sanMoves);
        var historyText = history.Count == 0
            ? "Sem historico anterior."
            : string.Join("\n", history.Select((m, i) =>
                $"{(string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Coach" : "Jogador")} {i + 1}: {m.Content}"));

        var prompt = $"""
        Voce esta em um chat de acompanhamento de uma unica partida.

        Jogador: {username}
        Partida: {overview.Result} de {overview.Color} contra {overview.Opponent} em {overview.TimeClass}
        Abertura: {overview.OpeningFamily}
        Precisao: {overview.Accuracy}%
        Lances da partida (resumo): {movesContext}
        Pontos fortes: {string.Join(" | ", strengths)}
        Erros: {string.Join(" | ", mistakes)}
        Melhorias: {string.Join(" | ", improvements)}

        Historico do chat:
        {historyText}

        Nova pergunta do jogador:
        {question}

        Responda como treinador humano, sem repetir template, em portugues natural.
        Seja pratico e conectado ao historico da conversa.
        """;

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.9,
            frequency_penalty = 0.5,
            presence_penalty = 0.3,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce e um coach de xadrez em conversa continua com o jogador. Evite repeticao e respostas genericas."
                },
                new { role = "user", content = prompt }
            }
        };

        var body = JsonSerializer.Serialize(payload);
        var response = await http.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content) ? fallback : content.Trim();
    }
    catch
    {
        return fallback;
    }
}

static string BuildGameChatFallback(
    string question,
    GameOverviewDto overview,
    IReadOnlyList<string> strengths,
    IReadOnlyList<string> improvements,
    IReadOnlyList<string> mistakes,
    IReadOnlyList<string> sanMoves)
{
    var q = question.ToLowerInvariant();
    var openingMoves = string.Join(' ', sanMoves.Take(8));
    var middlePivot = Math.Max(0, (sanMoves.Count / 2) - 3);
    var middleMoves = string.Join(' ', sanMoves.Skip(middlePivot).Take(6));
    var endingMoves = string.Join(' ', sanMoves.Skip(Math.Max(0, sanMoves.Count - 8)).Take(8));
    var accuracyText = overview.Accuracy is null ? "sem dado de precisao" : $"{overview.Accuracy:0.0}% de precisao";

    if (q.Contains("resum") || q.Contains("resumo"))
    {
        var summaryParts = new List<string>
        {
            $"Resumo da partida: voce jogou de {overview.Color} contra {overview.Opponent} em {overview.TimeClass} e terminou com {overview.Result.ToLowerInvariant()} ({overview.ResultCode}).",
            $"Foi uma {overview.OpeningFamily} com cerca de {overview.FullMoves} lances e {accuracyText}."
        };

        if (!string.IsNullOrWhiteSpace(openingMoves))
        {
            summaryParts.Add($"No inicio, o jogo seguiu por {openingMoves}.");
        }

        if (!string.IsNullOrWhiteSpace(middleMoves))
        {
            summaryParts.Add($"No meio, os lances mais importantes foram {middleMoves}.");
        }

        if (!string.IsNullOrWhiteSpace(endingMoves))
        {
            summaryParts.Add($"No trecho final, apareceu {endingMoves}.");
        }

        var keyPoint = mistakes.FirstOrDefault() ?? "faltou consolidar o plano na fase critica";
        var nextStep = improvements.FirstOrDefault() ?? "revisar os lances que mudaram a avaliacao";
        summaryParts.Add($"Ponto-chave: {keyPoint}. Proximo passo pratico: {nextStep}.");
        return string.Join(' ', summaryParts);
    }

    var firstImprovement = improvements.FirstOrDefault() ?? "revisar os lances criticos da partida";
    var firstMistake = mistakes.FirstOrDefault() ?? "houve um momento de perda de controle tatico";
    var firstStrength = strengths.FirstOrDefault() ?? "manteve bons recursos em parte da partida";
    return $"Sobre sua pergunta: nesta partida ({overview.Result} contra {overview.Opponent}), voce teve como ponto positivo que {firstStrength.ToLowerInvariant()}, mas o ponto critico foi que {firstMistake.ToLowerInvariant()}. Para a proxima partida, eu focaria em {firstImprovement.ToLowerInvariant()}.";
}

static string BuildMovesContextForAi(IReadOnlyList<string> sanMoves)
{
    if (sanMoves.Count == 0)
    {
        return "Sem PGN/lances disponiveis na fonte.";
    }

    var openingSlice = sanMoves.Take(12);
    var endingSlice = sanMoves.Skip(Math.Max(0, sanMoves.Count - 12));
    var middleStart = Math.Max(0, (sanMoves.Count / 2) - 4);
    var middleSlice = sanMoves.Skip(middleStart).Take(8);

    static string JoinWithNumbers(IEnumerable<string> moves, int startPly)
    {
        var list = moves.ToList();
        if (list.Count == 0)
        {
            return "-";
        }

        var numbered = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            numbered.Add($"{startPly + i + 1}.{list[i]}");
        }

        return string.Join(' ', numbered);
    }

    var openingText = JoinWithNumbers(openingSlice, 0);
    var middleText = JoinWithNumbers(middleSlice, middleStart);
    var endingText = JoinWithNumbers(endingSlice, Math.Max(0, sanMoves.Count - 12));

    return $"Inicio: {openingText} | Meio: {middleText} | Final: {endingText}";
}

static async Task<string> BuildAiTipAsync(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    string username,
    int sampleSize,
    ProfileStatsSummaryDto profileStats,
    OpeningSummaryDto openings,
    ColorSplitDto byColor,
    IReadOnlyList<PiecePressureDto> piecePressure,
    IReadOnlyList<PhasePressureDto> phasePressure,
    SuccessSummaryDto successSummary,
    IReadOnlyList<RecentGameDto> recentGames,
    AccuracySummaryDto accuracy,
    CancellationToken cancellationToken)
{
    var fallback = BuildRuleBasedTip(profileStats, openings, byColor, piecePressure, phasePressure, successSummary, accuracy, sampleSize);
    var apiKey = configuration["OpenAiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    try
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var prompt = BuildAiPrompt(username, sampleSize, profileStats, openings, byColor, piecePressure, phasePressure, successSummary, recentGames, accuracy);
        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.82,
            frequency_penalty = 0.45,
            presence_penalty = 0.3,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce e um coach de xadrez conversando com um aluno. Responda em portugues natural, variando o jeito de explicar, evitando respostas roboticas e sem repetir formula pronta."
                },
                new { role = "user", content = prompt }
            }
        };

        var body = JsonSerializer.Serialize(payload);
        var response = await http.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content) ? fallback : content.Trim();
    }
    catch
    {
        return fallback;
    }
}

static string BuildAiPrompt(
    string username,
    int sampleSize,
    ProfileStatsSummaryDto profileStats,
    OpeningSummaryDto openings,
    ColorSplitDto byColor,
    IReadOnlyList<PiecePressureDto> piecePressure,
    IReadOnlyList<PhasePressureDto> phasePressure,
    SuccessSummaryDto successSummary,
    IReadOnlyList<RecentGameDto> recentGames,
    AccuracySummaryDto accuracy)
{
    var bestOpening = openings.Best.FirstOrDefault();
    var worstOpening = openings.Worst.FirstOrDefault();
    var riskiestPiece = piecePressure.FirstOrDefault();
    var worstPhase = phasePressure.FirstOrDefault();
    var recentGamesText = recentGames.Count > 0
        ? string.Join(" | ", recentGames.Select(g => $"{g.Result} vs {g.Opponent} ({g.Color}, {g.TimeClass}, {g.OpeningFamily})"))
        : "Sem jogos recentes detalhados.";

    return $"""
    Jogador: {username}
    Amostra: {sampleSize} partidas recentes.
    Ratings: Rapid {profileStats.RapidRating}, Blitz {profileStats.BlitzRating}, Bullet {profileStats.BulletRating}
    Brancas win rate: {byColor.White.WinRate}% | Pretas win rate: {byColor.Black.WinRate}%
    Melhor abertura: {bestOpening?.Name} ({bestOpening?.ScoreRate}%)
    Pior abertura: {worstOpening?.Name} ({worstOpening?.ScoreRate}%)
    Cor de melhor desempenho: {successSummary.BestColor}
    Fase mais forte: {successSummary.StrongestPhase?.Phase}
    Peca sob pressao: {riskiestPiece?.Piece} ({riskiestPiece?.RiskRate}%)
    Fase critica: {worstPhase?.Phase}
    Precisao media: {accuracy.OverallAverage}%
    Ultimos {recentGames.Count} jogos: {recentGamesText}

    Gere uma dica pratica e personalizada para as proximas semanas.
    Evite formato fixo; pode usar 1 a 2 paragrafos curtos se fizer sentido.
    Foque no que mais muda resultado com menor esforco.
    """;
}

static async Task<string> BuildAnalysisQuestionAnswerAsync(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    string username,
    string question,
    int sampleSize,
    ProfileStatsSummaryDto profileStats,
    OpeningSummaryDto openings,
    ColorSplitDto byColor,
    IReadOnlyList<PiecePressureDto> piecePressure,
    IReadOnlyList<PhasePressureDto> phasePressure,
    SuccessSummaryDto successSummary,
    IReadOnlyList<RecentGameDto> recentGames,
    AccuracySummaryDto accuracy,
    CancellationToken cancellationToken)
{
    var fallback = BuildQuestionFallback(question, sampleSize, openings, byColor, phasePressure, piecePressure, successSummary, recentGames, accuracy);
    var apiKey = configuration["OpenAiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    try
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var baseContext = BuildAiPrompt(username, sampleSize, profileStats, openings, byColor, piecePressure, phasePressure, successSummary, recentGames, accuracy);
        var prompt = $"""
        Use o contexto abaixo para responder a pergunta do jogador em portugues de forma natural, personalizada e pratica.
        Evite resposta padrao e nao siga estrutura fixa.
        Se houver mais de um caminho valido, diga qual voce priorizaria primeiro e por que.

        Contexto do jogador:
        {baseContext}

        Pergunta do jogador:
        {question}
        """;

        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.88,
            frequency_penalty = 0.5,
            presence_penalty = 0.32,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce e um treinador de xadrez experiente, direto e humano. Responda sem tom corporativo e sem frases repetidas. Nunca invente numeros; quando faltar dado, assuma incerteza e sugira proximo passo."
                },
                new { role = "user", content = prompt }
            }
        };

        var body = JsonSerializer.Serialize(payload);
        var response = await http.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content) ? fallback : content.Trim();
    }
    catch
    {
        return fallback;
    }
}

static string BuildQuestionFallback(
    string question,
    int sampleSize,
    OpeningSummaryDto openings,
    ColorSplitDto byColor,
    IReadOnlyList<PhasePressureDto> phasePressure,
    IReadOnlyList<PiecePressureDto> piecePressure,
    SuccessSummaryDto successSummary,
    IReadOnlyList<RecentGameDto> recentGames,
    AccuracySummaryDto accuracy)
{
    var normalized = question.ToLowerInvariant();
    var bestOpening = openings.Best.FirstOrDefault();
    var worstOpening = openings.Worst.FirstOrDefault();
    var weakPhase = phasePressure.FirstOrDefault()?.Phase ?? "meio-jogo";
    var riskyPiece = piecePressure.FirstOrDefault()?.Piece ?? "dama";
    var recentLine = recentGames.Count > 0
        ? $"Nos ultimos jogos, destaque para {recentGames[0].Result} contra {recentGames[0].Opponent} em {recentGames[0].TimeClass}."
        : "Sem jogos recentes detalhados para citar.";

    if (normalized.Contains("abertura"))
    {
        return $"Sobre sua pergunta de abertura: nos {sampleSize} jogos recentes, voce rende melhor em {bestOpening?.Name ?? "algumas linhas especificas"}. Eu priorizaria revisar {worstOpening?.Name ?? "as linhas que mais te punem"} com foco em planos tipicos, nao em decorar lances. {recentLine}";
    }

    if (normalized.Contains("pretas") || normalized.Contains("brancas") || normalized.Contains("cor"))
    {
        return $"Pela sua pergunta sobre cor: voce esta com {byColor.White.WinRate}% de score de brancas e {byColor.Black.WinRate}% de pretas. Eu manteria a base da cor mais forte e reduziria complexidade na cor mais fraca por algumas semanas para estabilizar resultado. {recentLine}";
    }

    if (normalized.Contains("final") || normalized.Contains("meio") || normalized.Contains("fase"))
    {
        return $"Boa pergunta sobre fase de jogo: hoje seu gargalo principal esta em {weakPhase}. Um ajuste que costuma funcionar rapido e treino curto diario com posicoes desse tema + revisao do primeiro lance que muda a partida. {recentLine}";
    }

    return $"Pelo seu historico recente ({sampleSize} partidas), eu atacaria primeiro os erros de {weakPhase}, com cuidado extra nas decisoes envolvendo {riskyPiece}. Continue explorando {successSummary.BestOpening?.Name ?? "as estruturas que mais te favorecem"} e acompanhe se a precisao media ({accuracy.OverallAverage?.ToString("0.0") ?? "-"}%) sobe nas proximas sessoes. {recentLine}";
}

static string BuildRuleBasedTip(
    ProfileStatsSummaryDto profileStats,
    OpeningSummaryDto openings,
    ColorSplitDto byColor,
    IReadOnlyList<PiecePressureDto> piecePressure,
    IReadOnlyList<PhasePressureDto> phasePressure,
    SuccessSummaryDto successSummary,
    AccuracySummaryDto accuracy,
    int sampleSize)
{
    var tips = new List<string>();

    if (successSummary.BestOpening is not null)
    {
        tips.Add($"Voce esta acertando bem na abertura {successSummary.BestOpening.Name}, mantendo {successSummary.BestOpening.ScoreRate}% de aproveitamento.");
    }

    if (sampleSize < 20)
    {
        tips.Add("Amostra curta: jogue mais partidas para deixar a leitura estatistica mais confiavel.");
    }

    if (byColor.White.WinRate - byColor.Black.WinRate > 7)
    {
        tips.Add("Seu desempenho de pretas esta abaixo do de brancas: priorize repertorio simples e estruturas solidas de resposta.");
    }

    var worstOpening = openings.Worst.FirstOrDefault();
    if (worstOpening is not null)
    {
        tips.Add($"Sua abertura mais sensivel hoje e {worstOpening.Name}; vale revisar planos tipicos e armadilhas dessa linha.");
    }

    var riskyPiece = piecePressure.FirstOrDefault();
    if (riskyPiece is not null)
    {
        tips.Add($"O maior indice de risco apareceu com {riskyPiece.Piece}; trabalhe exercicios de calculo antes de mover essa peca em posicoes tensas.");
    }

    var phase = phasePressure.FirstOrDefault();
    if (phase is not null)
    {
        tips.Add($"A fase mais critica foi {phase.Phase}; inclua 20 minutos por dia de estudo focado nesse momento da partida.");
    }

    if (accuracy.OverallAverage is double acc && acc < 80)
    {
        tips.Add("Sua precisao media pode subir com revisao curta das ultimas derrotas, focando no primeiro lance que muda a avaliacao.");
    }

    if (tips.Count == 0)
    {
        tips.Add("Seu perfil esta equilibrado; foque em consistencia de calculo e consolidacao de finais tecnicos para o proximo salto de nivel.");
    }

    return $"Sem chave da IA ativa no momento, entao vou te responder de forma direta pelos dados: {string.Join(" ", tips.Take(3))}";
}

static ModeStatsDto? BuildModeStats(JsonElement? root, string mode)
{
    if (root is null || !root.Value.TryGetProperty(mode, out var modeNode))
    {
        return null;
    }

    var rating = GetNestedInt(modeNode, "last", "rating");
    var win = GetNestedInt(modeNode, "record", "win");
    var loss = GetNestedInt(modeNode, "record", "loss");
    var draw = GetNestedInt(modeNode, "record", "draw");
    var total = SafeTotal(win, loss, draw);
    var winRate = total > 0 && win is not null
        ? (double?)Math.Round((double)win.Value / total.Value * 100, 1)
        : null;

    return new ModeStatsDto(rating, win, loss, draw, total, winRate);
}

static int? SafeTotal(int? win, int? loss, int? draw)
{
    if (win is null && loss is null && draw is null)
    {
        return null;
    }

    return (win ?? 0) + (loss ?? 0) + (draw ?? 0);
}

static List<string> ExtractSanMoves(string pgn)
{
    if (string.IsNullOrWhiteSpace(pgn))
    {
        return [];
    }

    var lines = pgn
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => !line.TrimStart().StartsWith('['));

    var plain = string.Join(' ', lines);
    plain = Regex.Replace(plain, "\\{[^}]*\\}", " ");
    plain = Regex.Replace(plain, "\\([^)]*\\)", " ");

    var tokens = plain.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var resultTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1-0", "0-1", "1/2-1/2", "*" };
    var moves = new List<string>();

    foreach (var raw in tokens)
    {
        var token = raw.Trim();
        if (string.IsNullOrWhiteSpace(token) || resultTokens.Contains(token))
        {
            continue;
        }

        if (Regex.IsMatch(token, "^\\d+\\.(\\.\\.)?$") || Regex.IsMatch(token, "^\\d+\\.+"))
        {
            continue;
        }

        moves.Add(token);
    }

    return moves;
}

static string GetPieceFromSan(string san)
{
    if (string.IsNullOrWhiteSpace(san))
    {
        return "Peao";
    }

    var normalized = san.Replace("+", string.Empty).Replace("#", string.Empty).Trim();

    if (normalized.StartsWith("O-O", StringComparison.OrdinalIgnoreCase))
    {
        return "Rei";
    }

    var first = normalized[0];
    return first switch
    {
        'K' => "Rei",
        'Q' => "Dama",
        'R' => "Torre",
        'B' => "Bispo",
        'N' => "Cavalo",
        _ => "Peao"
    };
}

static string? ExtractOpeningFromEcoUrl(string? ecoUrl)
{
    if (string.IsNullOrWhiteSpace(ecoUrl))
    {
        return null;
    }

    var segment = ecoUrl.Split('/').LastOrDefault();
    if (string.IsNullOrWhiteSpace(segment))
    {
        return null;
    }

    return Uri.UnescapeDataString(segment).Replace('-', ' ');
}

static string NormalizeOpeningName(string opening)
{
    if (opening.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
        var fromUrl = ExtractOpeningFromEcoUrl(opening);
        return string.IsNullOrWhiteSpace(fromUrl) ? opening : fromUrl;
    }

    return opening;
}

static string NormalizeOpeningFamily(string opening)
{
    var normalized = opening.Trim();
    normalized = Regex.Replace(normalized, "\\s+", " ");
    normalized = Regex.Replace(normalized, "\\s+\\d.*$", string.Empty);

    var lower = normalized.ToLowerInvariant();
    var directFamilies = new (string Pattern, string Family)[]
    {
        ("petrov|petroff", "Petrov Defense"),
        ("four knights", "Four Knights Game"),
        ("sicilian defense", "Sicilian Defense"),
        ("french defense", "French Defense"),
        ("caro[- ]kann", "Caro-Kann Defense"),
        ("ruy lopez", "Ruy Lopez"),
        ("italian game", "Italian Game"),
        ("queens pawn opening", "Queen's Pawn Opening"),
        ("queens gambit", "Queen's Gambit"),
        ("kings indian defense", "King's Indian Defense"),
        ("nimzo[- ]indian defense", "Nimzo-Indian Defense"),
        ("english opening", "English Opening"),
        ("scandinavian defense", "Scandinavian Defense"),
        ("pirc defense", "Pirc Defense"),
        ("bishop.?s opening", "Bishop's Opening")
    };

    foreach (var family in directFamilies)
    {
        if (Regex.IsMatch(lower, family.Pattern, RegexOptions.IgnoreCase))
        {
            return family.Family;
        }
    }

    var markers = new[] { " Defense", " Game", " Gambit", " Opening", " System", " Attack" };
    foreach (var marker in markers)
    {
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return normalized[..(index + marker.Length)].Trim();
        }
    }

    var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return words.Length <= 4
        ? normalized
        : string.Join(' ', words.Take(4));
}

static double? AvgOrNull(List<double> values)
{
    return values.Count > 0 ? Math.Round(values.Average(), 1) : null;
}

static string? GetString(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value))
    {
        return null;
    }

    if (value.ValueKind == JsonValueKind.String)
    {
        return value.GetString();
    }

    if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
    {
        return value.ToString();
    }

    return null;
}

static string? GetNestedString(JsonElement element, params string[] path)
{
    var current = element;
    foreach (var segment in path)
    {
        if (!current.TryGetProperty(segment, out current))
        {
            return null;
        }
    }

    return current.ValueKind switch
    {
        JsonValueKind.String => current.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => current.ToString(),
        _ => null
    };
}

static int? GetInt(JsonElement element, string property)
{
    return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed)
        ? parsed
        : null;
}

static long? GetLong(JsonElement element, string property)
{
    return element.TryGetProperty(property, out var value) && value.TryGetInt64(out var parsed)
        ? parsed
        : null;
}

static int? GetNestedInt(JsonElement element, params string[] path)
{
    var current = element;
    foreach (var segment in path)
    {
        if (!current.TryGetProperty(segment, out current))
        {
            return null;
        }
    }

    return current.TryGetInt32(out var parsed) ? parsed : null;
}

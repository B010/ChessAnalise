using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient("ChessCom", client =>
{
    client.BaseAddress = new Uri("https://api.chess.com/pub/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ChessAnalise/1.0");
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

        var profileResponse = await client.GetAsync($"player/{escapedUsername}", cancellationToken);
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

        var statsResponse = await client.GetAsync($"player/{escapedUsername}/stats", cancellationToken);
        JsonDocument? statsJson = null;
        if (statsResponse.IsSuccessStatusCode)
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

        var client = httpClientFactory.CreateClient("ChessCom");
        var escapedUsername = Uri.EscapeDataString(normalizedUsername);

        var profileResponse = await client.GetAsync($"player/{escapedUsername}", cancellationToken);
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

        var statsResponse = await client.GetAsync($"player/{escapedUsername}/stats", cancellationToken);
        JsonDocument? statsJson = null;
        if (statsResponse.IsSuccessStatusCode)
        {
            await using var statsStream = await statsResponse.Content.ReadAsStreamAsync(cancellationToken);
            statsJson = await JsonDocument.ParseAsync(statsStream, cancellationToken: cancellationToken);
        }

        var player = BuildPlayerProfile(profileJson.RootElement, normalizedUsername);
        var profileStats = BuildProfileStats(statsJson?.RootElement);

        var games = await FetchRecentGamesAsync(client, normalizedUsername, cancellationToken);
        var byColor = BuildByColor(games);
        var openings = BuildOpenings(games);
        var piecePressure = BuildPiecePressure(games);
        var phasePressure = BuildPhasePressure(games);
        var phasePerformance = BuildPhasePerformance(games);
        var accuracy = BuildAccuracy(games);
        var successSummary = BuildSuccessSummary(byColor, openings, piecePressure, phasePerformance, accuracy);

        var aiTip = await BuildAiTipAsync(
            httpClientFactory,
            configuration,
            normalizedUsername,
            games.Count,
            profileStats,
            openings,
            byColor,
            piecePressure,
            phasePressure,
            successSummary,
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
            AiTip: aiTip,
            SampleSize: games.Count,
            DataWindowMonths: 3,
            GeneratedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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

app.Run();

static string? NormalizeUsername(string username)
{
    var normalized = username.Trim();
    return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
}

static async Task<List<ParsedGame>> FetchRecentGamesAsync(HttpClient client, string username, CancellationToken cancellationToken)
{
    var escapedUsername = Uri.EscapeDataString(username);
    var archivesResponse = await client.GetAsync($"player/{escapedUsername}/games/archives", cancellationToken);
    if (!archivesResponse.IsSuccessStatusCode)
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
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .TakeLast(3)
        .ToArray();

    var parsedGames = new List<ParsedGame>();
    foreach (var archiveUrl in archiveUrls)
    {
        var gamesResponse = await client.GetAsync(archiveUrl, cancellationToken);
        if (!gamesResponse.IsSuccessStatusCode)
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
        PlayerAccuracy: accuracy,
        PlayerPieceMoves: pieceCounters,
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
    AccuracySummaryDto accuracy,
    CancellationToken cancellationToken)
{
    var fallback = BuildRuleBasedTip(profileStats, openings, byColor, piecePressure, phasePressure, successSummary, accuracy, sampleSize);
    var apiKey = configuration["OPENAI_API_KEY"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    try
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var prompt = BuildAiPrompt(username, sampleSize, profileStats, openings, byColor, piecePressure, phasePressure, successSummary, accuracy);
        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0.5,
            messages = new object[]
            {
                new { role = "system", content = "Voce e um coach de xadrez. Responda em portugues de forma objetiva, em no maximo 3 frases." },
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
    AccuracySummaryDto accuracy)
{
    var bestOpening = openings.Best.FirstOrDefault();
    var worstOpening = openings.Worst.FirstOrDefault();
    var riskiestPiece = piecePressure.FirstOrDefault();
    var worstPhase = phasePressure.FirstOrDefault();

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

    Gere uma dica pratica em ate 3 frases equilibrando pontos fortes e pontos de evolucao para as proximas semanas.
    """;
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

    return string.Join(" ", tips.Take(3));
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

enum GameOutcome
{
    Win,
    Draw,
    Loss
}

sealed record ParsedGame(
    bool IsWhite,
    GameOutcome Outcome,
    string Opening,
    string PlayerResultCode,
    double? PlayerAccuracy,
    Dictionary<string, int> PlayerPieceMoves,
    int PlyCount,
    long EndTimeUnix);

public sealed record PlayerQuickResponse(PlayerProfileDto Player, ProfileStatsSummaryDto ProfileStats);

public sealed record PlayerDeepAnalysisResponse(
    PlayerProfileDto Player,
    ProfileStatsSummaryDto ProfileStats,
    AccuracySummaryDto Accuracy,
    ColorSplitDto ByColor,
    OpeningSummaryDto Openings,
    IReadOnlyList<PiecePressureDto> PiecePressure,
    IReadOnlyList<PhasePressureDto> PhasePressure,
    IReadOnlyList<PhasePerformanceDto> PhasePerformance,
    SuccessSummaryDto SuccessSummary,
    string AiTip,
    int SampleSize,
    int DataWindowMonths,
    long GeneratedAtUnix);

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

public sealed record ProfileStatsSummaryDto(
    int? RapidRating,
    int? BlitzRating,
    int? BulletRating,
    ModeStatsDto? Rapid,
    ModeStatsDto? Blitz,
    ModeStatsDto? Bullet);

public sealed record ModeStatsDto(
    int? Rating,
    int? Wins,
    int? Losses,
    int? Draws,
    int? TotalGames,
    double? WinRate);

public sealed record ColorSplitDto(ColorStatsDto White, ColorStatsDto Black);

public sealed record ColorStatsDto(int Wins, int Draws, int Losses, int TotalGames, double WinRate);

public sealed record OpeningSummaryDto(IReadOnlyList<OpeningStatDto> Best, IReadOnlyList<OpeningStatDto> Worst);

public sealed record OpeningStatDto(
    string Name,
    int Games,
    int Wins,
    int Draws,
    int Losses,
    double ScoreRate,
    double LossRate,
    double SufferingIndex);

public sealed record PiecePressureDto(string Piece, int TotalMoves, int MovesInLosses, double RiskRate);

public sealed record PhasePressureDto(string Phase, int Losses);

public sealed record PhasePerformanceDto(string Phase, int Games, int Wins, int Draws, int Losses, double ScoreRate);

public sealed record SuccessSummaryDto(
    string BestColor,
    OpeningStatDto? BestOpening,
    PiecePressureDto? SafestPiece,
    PhasePerformanceDto? StrongestPhase,
    string? BestAccuracySide,
    IReadOnlyList<string> Highlights);

public sealed record AccuracySummaryDto(double? OverallAverage, double? WhiteAverage, double? BlackAverage, int GamesWithAccuracy);

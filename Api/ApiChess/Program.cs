using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("ChessCom", client =>
{
    client.BaseAddress = new Uri("https://api.chess.com/pub/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ChessAnalise/1.0 (+https://localhost)");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/players/{username}", async (string username, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new { message = "username is required" });
    }

    var normalized = username.Trim().ToLowerInvariant();
    var client = httpClientFactory.CreateClient("ChessCom");

    var profileResponse = await client.GetAsync($"player/{normalized}", cancellationToken);
    if (profileResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { message = "Player not found" });
    }

    if (!profileResponse.IsSuccessStatusCode)
    {
        return Results.Problem("Failed to fetch player profile from Chess.com", statusCode: (int)profileResponse.StatusCode);
    }

    var statsResponse = await client.GetAsync($"player/{normalized}/stats", cancellationToken);
    if (!statsResponse.IsSuccessStatusCode)
    {
        return Results.Problem("Failed to fetch player stats from Chess.com", statusCode: (int)statsResponse.StatusCode);
    }

    var profileJson = JsonNode.Parse(await profileResponse.Content.ReadAsStringAsync(cancellationToken));
    var statsJson = JsonNode.Parse(await statsResponse.Content.ReadAsStringAsync(cancellationToken));

    var rapid = statsJson?["chess_rapid"];
    var blitz = statsJson?["chess_blitz"];
    var bullet = statsJson?["chess_bullet"];

    var rapidRecord = rapid?["record"];
    var blitzRecord = blitz?["record"];
    var bulletRecord = bullet?["record"];

    var totalWins = SafeInt(rapidRecord?["win"]) + SafeInt(blitzRecord?["win"]) + SafeInt(bulletRecord?["win"]);
    var totalLosses = SafeInt(rapidRecord?["loss"]) + SafeInt(blitzRecord?["loss"]) + SafeInt(bulletRecord?["loss"]);
    var totalDraws = SafeInt(rapidRecord?["draw"]) + SafeInt(blitzRecord?["draw"]) + SafeInt(bulletRecord?["draw"]);
    var totalGames = totalWins + totalLosses + totalDraws;

    var result = new
    {
        username = profileJson?["username"]?.GetValue<string>() ?? normalized,
        name = profileJson?["name"]?.GetValue<string>(),
        country = profileJson?["country"]?.GetValue<string>(),
        avatar = profileJson?["avatar"]?.GetValue<string>(),
        joined = profileJson?["joined"]?.GetValue<long>(),
        lastOnline = profileJson?["last_online"]?.GetValue<long>(),
        followers = profileJson?["followers"]?.GetValue<int>() ?? 0,
        ratings = new
        {
            rapid = SafeInt(rapid?["last"]?["rating"]),
            blitz = SafeInt(blitz?["last"]?["rating"]),
            bullet = SafeInt(bullet?["last"]?["rating"])
        },
        totals = new
        {
            wins = totalWins,
            losses = totalLosses,
            draws = totalDraws,
            games = totalGames,
            winRate = totalGames == 0 ? 0 : Math.Round((double)totalWins / totalGames * 100, 2)
        },
        stats = new
        {
            rapid = new
            {
                rating = SafeInt(rapid?["last"]?["rating"]),
                best = SafeInt(rapid?["best"]?["rating"]),
                wins = SafeInt(rapidRecord?["win"]),
                losses = SafeInt(rapidRecord?["loss"]),
                draws = SafeInt(rapidRecord?["draw"])
            },
            blitz = new
            {
                rating = SafeInt(blitz?["last"]?["rating"]),
                best = SafeInt(blitz?["best"]?["rating"]),
                wins = SafeInt(blitzRecord?["win"]),
                losses = SafeInt(blitzRecord?["loss"]),
                draws = SafeInt(blitzRecord?["draw"])
            },
            bullet = new
            {
                rating = SafeInt(bullet?["last"]?["rating"]),
                best = SafeInt(bullet?["best"]?["rating"]),
                wins = SafeInt(bulletRecord?["win"]),
                losses = SafeInt(bulletRecord?["loss"]),
                draws = SafeInt(bulletRecord?["draw"])
            }
        }
    };

    return Results.Ok(result);
})
.WithName("GetPlayerAnalysisData");

app.Run();

static int SafeInt(JsonNode? node)
{
    if (node is null)
    {
        return 0;
    }

    return node.GetValue<int>();
}

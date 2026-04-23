using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

public sealed class StockfishService : IStockfishService
{
    private static readonly Regex ScoreRegex = new(@"score\s+(cp|mate)\s+(-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DepthRegex = new(@"\bdepth\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly StockfishOptions _options;
    private readonly ILogger<StockfishService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;

    public StockfishService(IOptions<StockfishOptions> options, ILogger<StockfishService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StockfishHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new StockfishHealthSnapshot(false, false, _options.EnginePath, "Stockfish desabilitado por configuracao.");
        }

        if (string.IsNullOrWhiteSpace(_options.EnginePath))
        {
            return new StockfishHealthSnapshot(true, false, null, "EnginePath nao configurado.");
        }

        try
        {
            await EnsureStartedAsync(cancellationToken);
            return new StockfishHealthSnapshot(true, true, _options.EnginePath, "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no health check do Stockfish.");
            return new StockfishHealthSnapshot(true, false, _options.EnginePath, ex.Message);
        }
    }

    public async Task<StockfishEvaluationResult> EvaluateFenAsync(string fen, int? depth, int? moveTimeMs, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Stockfish esta desabilitado na configuracao.");
        }

        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new ArgumentException("FEN obrigatorio.", nameof(fen));
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);

            await SendAsync($"position fen {fen.Trim()}", cancellationToken);

            var finalDepth = depth.GetValueOrDefault(_options.Depth);
            var finalMoveTime = moveTimeMs.GetValueOrDefault(_options.MoveTimeMs);

            if (finalMoveTime > 0)
            {
                await SendAsync($"go movetime {finalMoveTime}", cancellationToken);
            }
            else
            {
                await SendAsync($"go depth {Math.Max(1, finalDepth)}", cancellationToken);
            }

            var timeoutMs = Math.Max(1000, _options.CommandTimeoutMs);
            var lines = await ReadUntilAsync(
                line => line.StartsWith("bestmove", StringComparison.OrdinalIgnoreCase),
                timeoutMs,
                cancellationToken);

            var bestMoveLine = lines.LastOrDefault(l => l.StartsWith("bestmove", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Stockfish nao retornou bestmove.");

            var infoLines = lines.Where(l => l.StartsWith("info", StringComparison.OrdinalIgnoreCase)).ToList();
            var scoreLine = infoLines.LastOrDefault(l => l.Contains(" score ", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var score = ParseScore(scoreLine);
            var parsedDepth = ParseDepth(infoLines.LastOrDefault() ?? string.Empty);

            var bestParts = bestMoveLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bestMove = bestParts.Length >= 2 ? bestParts[1] : "(none)";
            var ponder = bestParts.Length >= 4 && bestParts[2].Equals("ponder", StringComparison.OrdinalIgnoreCase)
                ? bestParts[3]
                : null;

            return new StockfishEvaluationResult(
                Fen: fen.Trim(),
                Cp: score.Cp,
                Mate: score.Mate,
                BestMove: bestMove,
                Ponder: ponder,
                Depth: parsedDepth,
                RawScore: score.Raw,
                EvaluatedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch
        {
            await RestartProcessInternalAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_input is not null)
            {
                try
                {
                    await _input.WriteLineAsync("quit");
                    await _input.FlushAsync();
                }
                catch
                {
                    // Ignora erros no encerramento.
                }
            }

            _process?.Kill(entireProcessTree: true);
            _process?.Dispose();
            _process = null;
            _input = null;
            _output = null;
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _input is not null && _output is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.EnginePath))
        {
            throw new InvalidOperationException("EnginePath nao configurado.");
        }

        if (!File.Exists(_options.EnginePath))
        {
            throw new FileNotFoundException("Executavel do Stockfish nao encontrado.", _options.EnginePath);
        }

        await RestartProcessInternalAsync();

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.EnginePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nao foi possivel iniciar o processo do Stockfish.");

        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;

        await SendAsync("uci", cancellationToken);
        await ReadUntilAsync(line => line.StartsWith("uciok", StringComparison.OrdinalIgnoreCase), _options.CommandTimeoutMs, cancellationToken);

        if (_options.HashMb > 0)
        {
            await SendAsync($"setoption name Hash value {_options.HashMb}", cancellationToken);
        }

        if (_options.Threads > 0)
        {
            await SendAsync($"setoption name Threads value {_options.Threads}", cancellationToken);
        }

        await SendAsync("isready", cancellationToken);
        await ReadUntilAsync(line => line.StartsWith("readyok", StringComparison.OrdinalIgnoreCase), _options.CommandTimeoutMs, cancellationToken);
        _logger.LogInformation("Stockfish iniciado com sucesso.");
    }

    private async Task RestartProcessInternalAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Sem acao.
        }

        _process.Dispose();
        _process = null;
        _input = null;
        _output = null;
        await Task.CompletedTask;
    }

    private async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        if (_input is null)
        {
            throw new InvalidOperationException("Entrada do Stockfish indisponivel.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _input.WriteLineAsync(command);
        await _input.FlushAsync();
    }

    private async Task<List<string>> ReadUntilAsync(Func<string, bool> stop, int timeoutMs, CancellationToken cancellationToken)
    {
        if (_output is null)
        {
            throw new InvalidOperationException("Saida do Stockfish indisponivel.");
        }

        var lines = new List<string>();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs)));

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();

            var readTask = _output.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));
            if (completed != readTask)
            {
                throw new TimeoutException("Timeout aguardando resposta do Stockfish.");
            }

            var line = await readTask;
            if (line is null)
            {
                throw new InvalidOperationException("Processo Stockfish finalizou inesperadamente.");
            }

            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            lines.Add(trimmed);
            if (stop(trimmed))
            {
                return lines;
            }
        }
    }

    private static (int? Cp, int? Mate, string Raw) ParseScore(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return (null, null, "n/a");
        }

        var match = ScoreRegex.Match(line);
        if (!match.Success)
        {
            return (null, null, line);
        }

        var type = match.Groups[1].Value.ToLowerInvariant();
        var value = int.Parse(match.Groups[2].Value);

        return type switch
        {
            "cp" => (value, null, $"cp {value}"),
            "mate" => (null, value, $"mate {value}"),
            _ => (null, null, line)
        };
    }

    private static int? ParseDepth(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = DepthRegex.Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var depth)
            ? depth
            : null;
    }
}

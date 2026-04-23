public sealed class StockfishOptions
{
    public bool Enabled { get; set; } = false;
    public string? EnginePath { get; set; }
    public int Depth { get; set; } = 12;
    public int MoveTimeMs { get; set; } = 0;
    public int HashMb { get; set; } = 128;
    public int Threads { get; set; } = 1;
    public int CommandTimeoutMs { get; set; } = 8000;
}

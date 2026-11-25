namespace MusicBell.Core.Models;

public enum PlayMethod
{
    SearchBox,
    DirectUrl,
    Failed
}

public sealed record PlayResult
{
    public bool Success { get; init; }
    public PlayMethod Method { get; init; }
    public string Message { get; init; } = "";
    public string Query { get; init; } = "";
    public TimeSpan Elapsed { get; init; }

    public static PlayResult Ok(string query, PlayMethod method, string message) => new()
    {
        Success = true, Query = query, Method = method, Message = message
    };

    public static PlayResult Fail(string query, string message) => new()
    {
        Success = false, Query = query, Method = PlayMethod.Failed, Message = message
    };
}

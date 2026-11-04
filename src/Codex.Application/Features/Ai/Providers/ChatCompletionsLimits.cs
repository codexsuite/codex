namespace Codex.Application.Features.Ai.Providers;

// The timing and size bounds of one provider call: the wait between retries before the first chunk, the longest silence
// between SSE lines, and the most a response may hold. Defaults are production values; a test passes small ones.
public sealed record ChatCompletionsLimits(TimeSpan RetryDelay, TimeSpan IdleTimeout, int MaxResponseBytes)
{
    public const int Retries = 2;

    public static ChatCompletionsLimits Default { get; } = new(TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(60), 4 * 1024 * 1024);
}

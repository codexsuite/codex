namespace Codex.WebApi.Tests.Telemetry;

// Sets process environment variables and puts them back on dispose. The telemetry module reads the endpoint while
// Program.cs runs, before a test's in-memory settings exist, so the endpoint can only go in through the environment.
// The fixtures using this are [NonParallelizable]: every host starting at the same time would see these too.
internal sealed class ProcessEnvironment : IDisposable
{
    private readonly Dictionary<string, string?> _previous = [];

    public ProcessEnvironment Set(string name, string? value)
    {
        _previous.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
        return this;
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}

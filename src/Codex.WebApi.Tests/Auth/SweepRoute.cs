namespace Codex.WebApi.Tests.Auth;

// One id-bearing /api route: its template as the endpoint data source prints it, what it needs, and a URL naming JD's item.
public sealed record SweepRoute(string Method, string Template, SweepNeeds Needs, Func<SweepIds, string> Url)
{
    public string Key => $"{Method} {Template}";

    public override string ToString() => Key;
}

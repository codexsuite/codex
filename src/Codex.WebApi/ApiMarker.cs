using System.Reflection;

namespace Codex.WebApi;

// Names the API assembly without naming Program: the test host is a
// WebApplicationFactory<ApiMarker>, and the Agoda.IoC scan starts here.
public sealed class ApiMarker
{
    private ApiMarker()
    {
    }

    public static Assembly Assembly { get; } = typeof(ApiMarker).Assembly;
}

using System.Reflection;

namespace Codex.Application;

// Names the Application assembly for the module scan and the Agoda.IoC scan.
public sealed class ApplicationMarker
{
    private ApplicationMarker()
    {
    }

    public static Assembly Assembly { get; } = typeof(ApplicationMarker).Assembly;
}

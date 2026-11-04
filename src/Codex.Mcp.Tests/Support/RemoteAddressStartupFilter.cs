using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Codex.Mcp.Tests.Support;

// A test double for "a request from another machine": every request to the test host arrives on 127.0.0.1, so a
// request carrying the test header gets that header's address as its remote address, ahead of the app's pipeline.
public sealed class RemoteAddressStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var address))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
            }
            return nextMiddleware(context);
        });
        next(app);
    };
}

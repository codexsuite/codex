using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests;

[TestFixture]
public class ApiErrorTests : ApiFixture
{
    // The test host maps its own throwing endpoints (a startup filter and a controller part that exist only
    // in this test); production has no such endpoints.
    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
    {
        services.AddTransient<IStartupFilter, ThrowingEndpointsStartupFilter>();
        services.AddControllers().AddApplicationPart(typeof(ApiErrorTests).Assembly);
    };

    [Test]
    public async Task Endpoint_WhenNotFoundThrown_ShouldReturn404WithApiErrorShape()
    {
        var response = await Client.GetAsync("/test-errors/not_found");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Endpoint_WhenConflictThrown_ShouldReturn409WithApiErrorShape()
    {
        var response = await Client.GetAsync("/test-errors/conflict");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    public async Task Endpoint_WhenValidationThrown_ShouldReturn400WithApiErrorShape()
    {
        var response = await Client.GetAsync("/test-errors/validation");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Endpoint_WhenRefusalThrown_ShouldReturn422WithApiErrorShape()
    {
        var response = await Client.GetAsync("/test-errors/blocked");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Blocked);
    }

    [Test]
    public async Task Endpoint_WhenNotConfiguredThrown_ShouldReturn503WithApiErrorShape()
    {
        var response = await Client.GetAsync("/test-errors/not_configured");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotConfigured);
    }

    [Test]
    public async Task Endpoint_WhenUnhandledExceptionThrown_ShouldReturn500WithGenericApiError()
    {
        var response = await Client.GetAsync("/test-errors/crash");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var error = await ApiErrors.ReadApiErrorAsync(response);
        error.Code.ShouldBe(ApiErrorCodes.Internal);
        error.Error.ShouldNotContain("secret connection detail");
    }

    [Test]
    public async Task Controller_WhenModelInvalid_ShouldReturn400WithValidationApiError()
    {
        var response = await Client.PostAsJsonAsync("/test-errors/model", new { title = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Controller_WhenANonNullablePropertyIsMissing_ShouldLeaveValidationToTheService()
    {
        var response = await Client.PostAsJsonAsync("/test-errors/unchecked", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Controller_WhenTheResultIsNull_ShouldReturn200WithNoBody()
    {
        var response = await Client.GetAsync("/test-errors/null");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldBeNull();
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [TestCase("", "")]
    [TestCase("%20", " ")]
    public async Task Controller_WhenAQueryValueIsBlank_ShouldPassItOnUnchanged(string sent, string received)
    {
        var echo = await Client.GetFromJsonAsync<JsonElement>($"/test-errors/echo?q={sent}");

        echo.GetProperty("q").GetString().ShouldBe(received);
    }

    [Test]
    public async Task Controller_WhenConflictThrown_ShouldReturn409WithApiErrorShape()
    {
        var response = await Client.GetAsync("/test-errors/controller-conflict");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    public async Task Api_WhenNoEndpointMatches_ShouldReturn404WithApiErrorShape()
    {
        var response = await Client.GetAsync("/api/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Api_WhenMethodNotMapped_ShouldReturnApiErrorShape()
    {
        var response = await Client.DeleteAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task MinimalApi_WhenBodyIsMalformed_ShouldReturn400WithValidationApiError()
    {
        using var body = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/test-errors/bind", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task MinimalApi_WhenRouteValueUnparsable_ShouldReturnApiErrorShape()
    {
        var response = await Client.GetAsync("/api/test-errors/things/not-a-guid");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    // A real controller route (#98: feature endpoints are controllers), so MVC's own binding and routing failures
    // keep the status and ApiError code the minimal APIs gave. A wrong method or content type matches no endpoint
    // but the /api fallback, so it is 404 rather than 405 or 415 (MvcServices).
    [Test]
    public async Task Controller_WhenBodyIsMalformed_ShouldReturn400WithValidationApiError()
    {
        using var body = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/ai/integrations", body);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Controller_WhenBodyIsMissing_ShouldReturn400WithValidationApiError()
    {
        using var body = new StringContent("", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/ai/integrations", body);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Controller_WhenBodyIsNotJson_ShouldReturn404WithNotFoundApiError()
    {
        using var body = new StringContent("kind=openai", System.Text.Encoding.UTF8, "text/plain");

        var response = await Client.PostAsync("/api/ai/integrations", body);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Controller_WhenRouteValueUnparsable_ShouldReturn404WithNotFoundApiError()
    {
        var response = await Client.DeleteAsync("/api/ai/integrations/not-a-guid");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Controller_WhenMethodNotMapped_ShouldReturn404WithNotFoundApiError()
    {
        var response = await Client.DeleteAsync("/api/ai/models");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    public sealed record BindThing(string Title);

    private sealed class ThrowingEndpointsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map("/test-errors/not_found", branch => branch.Run(_ => throw ApiException.NotFound("Nothing here.")));
            app.Map("/test-errors/conflict", branch => branch.Run(_ => throw ApiException.Conflict("Stale version.")));
            app.Map("/test-errors/validation", branch => branch.Run(_ => throw ApiException.Validation("Title is required.")));
            app.Map("/test-errors/blocked", branch => branch.Run(_ => throw ApiException.Blocked("Still a copyright risk.")));
            app.Map("/test-errors/not_configured", branch => branch.Run(_ => throw ApiException.NotConfigured("AI drafting is not configured.")));
            app.Map("/test-errors/crash", branch => branch.Run(_ => throw new InvalidOperationException("secret connection detail")));
            // Minimal API endpoints, so the framework's own binding failures go through the real pipeline.
            app.Map("/api/test-errors", branch =>
            {
                branch.UseRouting();
                branch.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/bind", (BindThing thing) => Results.Ok(thing));
                    endpoints.MapGet("/things/{id:guid}", (Guid id) => Results.Ok(id));
                });
            });
            next(app);
        };
    }
}

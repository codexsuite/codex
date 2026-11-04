using Codex.Application.Features.Ai.Integrations;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Ai.Providers;

// Sends one provider request with the shared policy: the SSRF guard before every attempt, a retry (twice, short fixed
// delay) of 429/5xx/network failures, and the fixed-wording ApiException for anything else. The response comes back
// with headers read and the body unread; the caller owns it. Retries stop here, before any body has been consumed.
public static class ProviderHttp
{
    private const int ErrorBodyLimit = 16 * 1024;

    public static async Task<HttpResponseMessage> SendAsync(
        IHttpClientFactory clients,
        IAiEndpointGuard guard,
        ProviderEndpoint endpoint,
        Func<HttpRequestMessage> build,
        ChatCompletionsLimits limits,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(endpoint.ClientName);
        var attempt = 0;
        while (true)
        {
            if (endpoint.NeedsGuard)
            {
                await guard.RequireAllowedAsync(endpoint.BaseUrl, cancellationToken);
            }

            ProviderFailure failure;
            HttpResponseMessage? response = null;
            try
            {
                using var request = build();
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                var body = await ReadErrorBodyAsync(response, limits.IdleTimeout, cancellationToken);
                failure = ProviderFailures.Classify(response.StatusCode, body);
                logger.LogWarning(
                    "{Provider} request failed: HTTP {Status}, request {RequestId}", endpoint.Name, (int)response.StatusCode, RequestId(response));
            }
            catch (HttpRequestException e)
            {
                failure = ProviderFailure.Unreachable;
                logger.LogWarning(e, "{Provider} request failed: {Reason}", endpoint.Name, e.HttpRequestError);
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                failure = ProviderFailure.Unreachable;
                logger.LogWarning(e, "{Provider} request failed: timed out", endpoint.Name);
            }
            finally
            {
                if (response is { IsSuccessStatusCode: false })
                {
                    response.Dispose();
                }
            }

            if (!ProviderFailures.IsRetryable(failure) || attempt >= ChatCompletionsLimits.Retries)
            {
                throw ProviderFailures.ToException(endpoint.Name, failure);
            }

            attempt++;
            await Task.Delay(limits.RetryDelay, cancellationToken);
        }
    }

    // A body that never finishes is cut off after `timeout`; the expiry surfaces as an OperationCanceledException the
    // caller did not ask for, which SendAsync treats as "could not be reached".
    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            var buffer = new byte[ErrorBodyLimit];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, limit.Token);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static string? RequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;
}

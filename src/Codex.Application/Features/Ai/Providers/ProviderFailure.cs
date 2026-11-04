using System.Net;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Ai.Providers;

// Why a provider call failed, in the few buckets the author can act on. Fixed wording per bucket: nothing from the
// provider's body (which may echo the request) and never the key reaches a message.
public enum ProviderFailure
{
    Rejected,
    NoCredit,
    Busy,
    BadRequest,
    Unreachable,
}

public static class ProviderFailures
{
    // A 429 or 400 whose body says the account is out of credit is a billing problem, not a rate limit.
    public static ProviderFailure Classify(HttpStatusCode status, string body)
    {
        var code = (int)status;
        if (code is 401 or 403)
        {
            return ProviderFailure.Rejected;
        }

        if (code == 402 || SaysOutOfCredit(body))
        {
            return ProviderFailure.NoCredit;
        }

        if (code == 429 || code >= 500)
        {
            return ProviderFailure.Busy;
        }

        return ProviderFailure.BadRequest;
    }

    // Retrying cannot help a rejected key, a billing problem or a request the provider refuses.
    public static bool IsRetryable(ProviderFailure failure) => failure is ProviderFailure.Busy or ProviderFailure.Unreachable;

    public static ApiException ToException(string provider, ProviderFailure failure) => failure switch
    {
        ProviderFailure.Rejected => ApiException.NotConfigured($"{provider} rejected the API key. Check it in Settings → AI."),
        ProviderFailure.NoCredit => ApiException.NotConfigured($"{provider} reports no credit left on this account. Check the plan and credits with the provider."),
        ProviderFailure.Busy => Unavailable($"{provider} is busy right now. Try again in a moment."),
        ProviderFailure.BadRequest => Unavailable($"{provider} could not use this request. Check the model and the beat, then try again."),
        _ => Unavailable($"{provider} could not be reached. Try again in a moment."),
    };

    // TODO: a dedicated `provider_unavailable` error code (contracts are frozen this wave) replaces internal_error here.
    public static ApiException Unavailable(string message) => new(ApiErrorCodes.Internal, message);

    private static bool SaysOutOfCredit(string body) =>
        body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
        || body.Contains("insufficient credit", StringComparison.OrdinalIgnoreCase)
        || body.Contains("insufficient_credit", StringComparison.OrdinalIgnoreCase)
        || body.Contains("more credits", StringComparison.OrdinalIgnoreCase)
        || body.Contains("billing_hard_limit", StringComparison.OrdinalIgnoreCase);
}

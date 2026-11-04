using Codex.Application.Network;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agoda.IoC.Core;
using Codex.Contracts.Errors;
using Codex.Contracts.Sources;

namespace Codex.Application.Features.References;

// The production ISourceLookup: ISBN through Open Library, DOI through Crossref, a web address by fetching it.
// Every call goes through a named HttpClient (see ReferenceServices) with a time limit; a response is read up to a
// size limit. A web address is only fetched when every address its host resolves to is public, and a redirect is
// reported (never silently followed) after the same check on the next hop.
[RegisterSingleton]
public sealed partial class HttpSourceLookup(IHttpClientFactory clients, IHostResolver resolver) : ISourceLookup
{
    public const string OpenLibraryClient = "source-lookup-openlibrary";
    public const string CrossrefClient = "source-lookup-crossref";
    public const string WebClient = "source-lookup-web";

    public const int MaxJsonBytes = 1_048_576;
    public const int MaxPageBytes = 262_144;
    public const int MaxRedirects = 5;
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(10);

    private static readonly int[] RedirectStatuses = [301, 302, 303, 307, 308];

    public async Task<SourceLookupResult> LookupAsync(string kind, string value, CancellationToken cancellationToken) =>
        kind switch
        {
            SourceLookupKinds.Isbn => await IsbnAsync(value, cancellationToken),
            SourceLookupKinds.Doi => await DoiAsync(value, cancellationToken),
            SourceLookupKinds.Url => await UrlAsync(value, cancellationToken),
            _ => throw new ArgumentException($"Unknown lookup kind '{kind}'.", nameof(kind)),
        };

    private async Task<SourceLookupResult> IsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://openlibrary.org/api/books?bibkeys=ISBN:{Uri.EscapeDataString(isbn)}&format=json&jscmd=data");
        using var response = await SendAsync(OpenLibraryClient, uri, cancellationToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return SourceLookupResult.NotFound();
        }

        using var json = await ReadJsonAsync(response, cancellationToken);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty($"ISBN:{isbn}", out var book))
        {
            return SourceLookupResult.NotFound();
        }

        var title = Text(book, "title");
        if (title is null)
        {
            return SourceLookupResult.NotFound();
        }

        var authors = Names(book, "authors", "name");
        var publisher = book.TryGetProperty("publishers", out var publishers) && publishers.ValueKind == JsonValueKind.Array
            ? publishers.EnumerateArray().Select(p => Text(p, "name")).FirstOrDefault(p => p is not null)
            : null;
        return SourceLookupResult.Found(new SourceMetadata(title, authors, YearIn(Text(book, "publish_date")), publisher, null));
    }

    private async Task<SourceLookupResult> DoiAsync(string doi, CancellationToken cancellationToken)
    {
        var path = string.Join('/', doi.Split('/').Select(Uri.EscapeDataString));
        using var response = await SendAsync(CrossrefClient, new Uri($"https://api.crossref.org/works/{path}"), cancellationToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return SourceLookupResult.NotFound();
        }

        using var json = await ReadJsonAsync(response, cancellationToken);
        if (json is null || !json.RootElement.TryGetProperty("message", out var message))
        {
            return SourceLookupResult.NotFound();
        }

        var title = First(message, "title");
        if (title is null)
        {
            return SourceLookupResult.NotFound();
        }

        var authors = message.TryGetProperty("author", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(CrossrefName).Where(n => n.Length > 0).ToList()
            : [];
        return SourceLookupResult.Found(new SourceMetadata(
            title, authors, CrossrefYear(message), First(message, "container-title") ?? Text(message, "publisher"), Text(message, "URL")));
    }

    private async Task<SourceLookupResult> UrlAsync(string value, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var original) || original.Scheme is not ("http" or "https"))
        {
            throw ApiException.Validation("Enter a web address starting with http:// or https://.");
        }

        var current = original;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            await RequireOnlyPublicAddressesAsync(current, cancellationToken);
            using var response = await SendAsync(WebClient, current, cancellationToken);
            if (response is null)
            {
                return SourceLookupResult.NotFound();
            }

            if (RedirectStatuses.Contains((int)response.StatusCode) && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (current.Scheme is not ("http" or "https"))
                {
                    return SourceLookupResult.NotFound();
                }

                continue;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone || (int)response.StatusCode >= 500)
            {
                return SourceLookupResult.NotFound();
            }

            if (current != original)
            {
                return SourceLookupResult.Redirect(current.AbsoluteUri);
            }

            var title = response.IsSuccessStatusCode ? await PageTitleAsync(response, cancellationToken) : null;
            return SourceLookupResult.Found(new SourceMetadata(
                title ?? original.Host, [], null, original.Host.StartsWith("www.", StringComparison.Ordinal) ? original.Host[4..] : original.Host, original.AbsoluteUri));
        }

        return SourceLookupResult.NotFound();
    }

    private async Task RequireOnlyPublicAddressesAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await resolver.ResolveAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (System.Net.Sockets.SocketException)
        {
            throw ApiException.Validation($"The address {uri.Host} could not be found.");
        }

        if (addresses.Length == 0 || addresses.Any(a => !PublicAddress.IsPublic(a)))
        {
            throw ApiException.Validation($"The address {uri.Host} is not on the public internet, so it cannot be looked up.");
        }
    }

    // Null when nothing answered in time or at all: the caller treats that as "not found".
    private async Task<HttpResponseMessage?> SendAsync(string clientName, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            return await clients.CreateClient(clientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await ReadCappedAsync(response, MaxJsonBytes, truncate: false, cancellationToken);
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> PageTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not null && !type.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = await ReadCappedAsync(response, MaxPageBytes, truncate: true, cancellationToken);
        var match = TitlePattern().Match(Encoding.UTF8.GetString(bytes));
        if (!match.Success)
        {
            return null;
        }

        var title = Whitespace().Replace(WebUtility.HtmlDecode(match.Groups[1].Value), " ").Trim();
        return title.Length == 0 ? null : title[..Math.Min(title.Length, 300)];
    }

    // Reads at most `max` bytes. Past that, a page is cut short (its title is near the top) and an API answer is refused.
    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, int max, bool truncate, CancellationToken cancellationToken)
    {
        if (!truncate && response.Content.Headers.ContentLength > max)
        {
            throw TooLarge();
        }

        // HttpClient.Timeout ends once the headers arrive, so the body gets its own limit against a slow trickle.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BodyTimeout);
        cancellationToken = timeout.Token;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max)
            {
                if (truncate)
                {
                    await buffer.WriteAsync(chunk.AsMemory(0, (int)(max - buffer.Length)), cancellationToken);
                    break;
                }

                throw TooLarge();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static ApiException TooLarge() => ApiException.Validation("The look-up answer was too large to read.");

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static string? First(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!.Trim()).FirstOrDefault(s => s.Length > 0)
            : null;

    private static List<string> Names(JsonElement element, string list, string field) =>
        element.TryGetProperty(list, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(i => Text(i, field)).OfType<string>().ToList()
            : [];

    // "Family, Given" for a person, the bare name for an organisation.
    private static string CrossrefName(JsonElement author)
    {
        var family = Text(author, "family");
        var given = Text(author, "given");
        if (family is null)
        {
            return Text(author, "name") ?? string.Empty;
        }

        return given is null ? family : $"{family}, {given}";
    }

    private static int? CrossrefYear(JsonElement message)
    {
        foreach (var field in new[] { "issued", "published-print", "published-online", "created" })
        {
            if (message.TryGetProperty(field, out var date)
                && date.TryGetProperty("date-parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0
                && parts[0].ValueKind == JsonValueKind.Array && parts[0].GetArrayLength() > 0
                && parts[0][0].ValueKind == JsonValueKind.Number)
            {
                return parts[0][0].GetInt32();
            }
        }

        return null;
    }

    private static int? YearIn(string? text) =>
        text is not null && YearPattern().Match(text) is { Success: true } m ? int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : null;

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\b\d{4}\b")]
    private static partial Regex YearPattern();
}

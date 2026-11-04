using System.Net;
using System.Net.Http.Json;
using System.Text;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Storage;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

// Team Topologies' sent request is granted with the signed letter; the credit line reaches the bibliography data.
[TestFixture]
public class PermissionGrantedTests : PermissionApiFixture
{
    private readonly InMemoryBlobStore _blobs = new();
    private static readonly Guid RequestId = SeedIds.PermissionRequests.TeamTopologies;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(_blobs);

    [Test, Order(1)]
    public async Task Grant_WithoutAFile_ShouldBe400()
    {
        using var response = await GrantAsync(RequestId, null, null, "application/pdf", "Used by permission.");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(2)]
    public async Task Grant_WithAnOversizeFile_ShouldBe400()
    {
        using var response = await GrantAsync(RequestId, "letter.pdf", new byte[(10 * 1024 * 1024) + 1], "application/pdf");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(3)]
    public async Task Grant_WithADisallowedType_ShouldBe400()
    {
        using var response = await GrantAsync(RequestId, "letter.exe", [1, 2, 3], "application/x-msdownload");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetSourceAsync(SeedIds.References.TeamTopologies)).Permission!.Status.ShouldBe("sent");
    }

    [Test, Order(4)]
    public async Task Grant_ShouldKeepTheLetterAndTheCreditLineAndClearTheRisk()
    {
        using var response = await GrantAsync(RequestId, "../it revolution.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 signed"), "application/pdf", "Used by permission of IT Revolution.");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        var permission = source.Permission.ShouldNotBeNull();
        (permission.Status, permission.CreditLine).ShouldBe(("granted", "Used by permission of IT Revolution."));
        permission.RespondedLabel.ShouldStartWith("Granted ");
        permission.Attachment.ShouldNotBeNull().FileName.ShouldBe("it_revolution.pdf");
        source.Verdict.Status.ShouldBe("needs_check");
        (await GetSummaryAsync()).Risks.ShouldBe(2);
    }

    [Test, Order(5)]
    public async Task Letter_ShouldDownloadFromTheBlobStore()
    {
        var blob = await _blobs.GetAsync($"books/{SeedIds.Books.ProductEngineering}/permissions/{RequestId}/it_revolution.pdf", CancellationToken.None);
        using var response = await Client.GetAsync($"/api/permission-requests/{RequestId}/letter");

        blob.ShouldNotBeNull();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("%PDF-1.4 signed");
    }

    [Test, Order(5)]
    public async Task Letter_ShouldDownloadAsAnAttachmentWithTheKeptTypeAndSafeName()
    {
        using var response = await Client.GetAsync($"/api/permission-requests/{RequestId}/letter");

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var disposition = response.Content.Headers.ContentDisposition.ShouldNotBeNull();
        (disposition.DispositionType, disposition.FileNameStar).ShouldBe(("attachment", "it_revolution.pdf"));
    }

    [Test, Order(6)]
    public async Task Csl_ShouldCarryTheCreditLine()
    {
        var csl = (await Client.GetFromJsonAsync<List<SourceCslDto>>($"/api/books/{SeedIds.Books.ProductEngineering}/sources/csl"))!;

        csl.Single(s => s.SourceId == SeedIds.References.TeamTopologies).CreditLine.ShouldBe("Used by permission of IT Revolution.");
    }

    [Test, Order(7)]
    public async Task Transitions_AfterGranted_ShouldBe409()
    {
        using var again = await GrantAsync(RequestId, "letter.pdf", [1], "application/pdf");
        using var refuse = await PostAsync(RequestId, "refused", new RefusePermissionRequest("Changed our minds"));
        using var withdraw = await PostAsync(RequestId, "withdraw");

        (await again.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        (await refuse.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        (await withdraw.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }
}

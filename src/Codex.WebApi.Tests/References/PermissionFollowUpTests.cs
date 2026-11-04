using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Codex.Contracts;
using Codex.Core;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Storage;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

// J25: Team Topologies' sent request has its reminder come due, is chased, then answered; the letter can be replaced.
[TestFixture]
public class PermissionFollowUpTests : PermissionApiFixture
{
    private static readonly Guid RequestId = SeedIds.PermissionRequests.TeamTopologies;
    private static readonly Guid Source = SeedIds.References.TeamTopologies;
    private readonly InMemoryBlobStore _blobs = new();

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(_blobs);

    private async Task<HttpResponseMessage> PatchAsync(PatchPermissionRequest request) =>
        await Client.PatchAsJsonAsync($"/api/permission-requests/{RequestId}", request);

    private async Task<SourceRowDto> RowAsync() =>
        (await Client.GetFromJsonAsync<SourceRowDto[]>($"/api/books/{SeedIds.Books.ProductEngineering}/sources"))!.Single(r => r.Id == Source);

    [Test, Order(1)]
    public async Task Sources_WhenTheReminderIsInTheFuture_ShouldLabelTheRowWithTheDaysLeftAndCountNothingDue()
    {
        var row = await RowAsync();

        (row.ReminderLabel, row.ReminderDue).ShouldBe(("Reminder due in 3 days", false));
        (await GetSummaryAsync()).RemindersDue.ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task Sources_WhenTheReminderHasPassed_ShouldMarkTheRowAndCountOneReplyOverdue()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE permission_request SET remind_at = now() - interval '1 day' WHERE id = @RequestId", new { RequestId });

        var row = await RowAsync();
        var summary = await GetSummaryAsync();

        (row.ReminderLabel, row.ReminderDue).ShouldBe(("Reminder due", true));
        (summary.RemindersDue, summary.Badge).ShouldBe((1, 3));
        var permission = (await GetSourceAsync(Source)).Permission.ShouldNotBeNull();
        permission.RemindDue.ShouldBeTrue();
        permission.FollowUp.ShouldNotBeNull().ShouldContain("Team Topologies");
    }

    [Test, Order(3)]
    public async Task Patch_WithADateInThePast_ShouldBe400AndKeepTheReminder()
    {
        var yesterday = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone).AddDays(-1).ToString("yyyy-MM-dd");

        using var response = await PatchAsync(new PatchPermissionRequest(null, null, yesterday));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetSummaryAsync()).RemindersDue.ShouldBe(1);
    }

    [Test, Order(4)]
    public async Task MarkFollowedUp_ShouldStampTheChaserAndPushTheOverdueReminderAWeekOn()
    {
        var source = await TransitionAsync(RequestId, "followed-up");

        var permission = source.Permission.ShouldNotBeNull();
        permission.FollowedUpLabel.ShouldStartWith("Followed up ");
        permission.RemindDue.ShouldBeFalse();
        (await GetSummaryAsync()).RemindersDue.ShouldBe(0);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT last_reminded_at IS NOT NULL AND remind_at > now() FROM permission_request WHERE id = @RequestId", new { RequestId })).ShouldBeTrue();
        source.History[0].Text.ShouldBe("followed up on the permission request");
    }

    [Test, Order(5)]
    public async Task Patch_WithRemindInDays_ShouldSetTheReminderFromTodayAndRejectNonsense()
    {
        using var response = await PatchAsync(new PatchPermissionRequest(null, null, null, 7));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var permission = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!.Permission.ShouldNotBeNull();
        var expected = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone).AddDays(7).ToString("yyyy-MM-dd");
        permission.RemindOn.ShouldBe(expected);

        using var bad = await PatchAsync(new PatchPermissionRequest(null, null, null, 0));
        (await bad.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [TestCase("30/10/2026")]
    [TestCase("2026-10-30T00:00")]
    [TestCase("2026-10-30+07:00")]
    [TestCase("2026-02-30")]
    [Test, Order(5)]
    public async Task Patch_WithAMalformedRemindOn_ShouldBe400AndKeepTheReminder(string remindOn)
    {
        using var response = await PatchAsync(new PatchPermissionRequest(null, null, remindOn));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(5)]
    public async Task Patch_WithARemindOnBeyondAYear_ShouldBe400()
    {
        var tooFar = Today().AddDays(366).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var response = await PatchAsync(new PatchPermissionRequest(null, null, tooFar));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("365");
    }

    [Test, Order(5)]
    public async Task Patch_WithARemindOnExactlyAYearOut_ShouldBeAccepted()
    {
        var limit = Today().AddDays(365).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var response = await PatchAsync(new PatchPermissionRequest(null, null, limit));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!.Permission.ShouldNotBeNull().RemindOn.ShouldBe(limit);
    }

    private static DateOnly Today() => DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone);

    [Test, Order(6)]
    public async Task Grant_WithANegativeFee_ShouldBe400AndLeaveItSent()
    {
        var form = new MultipartFormDataContent { { new StringContent("-1"), "fee" } };
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "letter", "grant.pdf");

        using var response = await Client.PostAsync($"/api/permission-requests/{RequestId}/granted", form);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetSourceAsync(Source)).Permission!.Status.ShouldBe("sent");
    }

    [Test, Order(7)]
    public async Task Grant_WithAFee_ShouldKeepItAndClearTheRiskAndTheReminder()
    {
        var form = new MultipartFormDataContent { { new StringContent("25.50"), "fee" }, { new StringContent("Reprinted by permission of IT Revolution"), "creditLine" } };
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "letter", "grant.pdf");

        using var response = await Client.PostAsync($"/api/permission-requests/{RequestId}/granted", form);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        source.Permission!.FeeAmount.ShouldBe(25.5m);
        source.Verdict.Status.ShouldBe("needs_check");
        (await GetSummaryAsync()).Risks.ShouldBe(2);
        (await RowAsync()).ReminderLabel.ShouldBeNull();
    }

    [Test, Order(8)]
    public async Task FollowedUp_WhenGranted_ShouldBe409()
    {
        using var response = await PostAsync(RequestId, "followed-up");

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(9)]
    public async Task AttachLetter_WhenOneIsKept_ShouldBe409()
    {
        using var response = await AttachAsync("grant-v2.pdf");

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(10)]
    public async Task RemoveLetter_ThenAttachAnother_ShouldLeaveExactlyOneAttachment()
    {
        using var removed = await Client.DeleteAsync($"/api/permission-requests/{RequestId}/letter");
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<SourceDetailDto>())!.Permission!.Attachment.ShouldBeNull();
        using var gone = await Client.DeleteAsync($"/api/permission-requests/{RequestId}/letter");
        (await gone.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);

        using var attached = await AttachAsync("grant-v2.pdf");

        attached.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await attached.Content.ReadFromJsonAsync<SourceDetailDto>())!.Permission!.Attachment.ShouldNotBeNull().FileName.ShouldBe("grant-v2.pdf");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_attachment WHERE permission_request_id = @RequestId AND kind = 'permission_letter' AND archived_at IS NULL",
            new { RequestId })).ShouldBe(1);
        var archived = (await db.QueryAsync<(DateTime? AssetArchivedAt, string StorageKey)>(
            """
            SELECT s.archived_at, s.storage_key FROM reference_attachment t JOIN asset s ON s.id = t.asset_id
            WHERE t.permission_request_id = @RequestId AND t.archived_at IS NOT NULL
            """, new { RequestId })).ToList();
        var old = archived.ShouldHaveSingleItem();
        old.AssetArchivedAt.ShouldNotBeNull();
        (await _blobs.ExistsAsync(old.StorageKey, CancellationToken.None)).ShouldBeTrue();
    }

    private Task<HttpResponseMessage> AttachAsync(string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 v2"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "letter", fileName);
        return Client.PostAsync($"/api/permission-requests/{RequestId}/letter", form);
    }
}

using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// /account: your own display name and password.
[TestFixture]
public class AccountTests : ApiFixture
{
    private const string Email = "account@example.test";
    private const string Password = "account-password";
    private Guid _userId;

    [OneTimeSetUp]
    public async Task CreateUserAsync() => _userId = await TestUsers.CreateAsync(ConnectionString, Email, "Old Name", Password);

    [Test]
    public async Task PatchMe_ShouldRenameTheSignedInUser()
    {
        Client.ActingAs(_userId);

        var response = await Client.PatchAsJsonAsync("/api/auth/me", new UpdateAccountRequest("  New Name "));

        (await response.Content.ReadFromJsonAsync<MeDto>())!.DisplayName.ShouldBe("New Name");
        (await Client.GetFromJsonAsync<MeDto>("/api/auth/me"))!.DisplayName.ShouldBe("New Name");
    }

    [TestCase("not-the-password", "a-new-long-password", "The current password is not right.")]
    [TestCase(Password, "short", "The new password needs at least 10 characters.")]
    [TestCase(Password, Password, "Choose a new password that differs from the current one.")]
    public async Task ChangePassword_WhenRefused_ShouldBe400AndKeepTheSession(string current, string next, string message)
    {
        using var client = await AuthClient.SignedInAsync(Host, Email, Password);

        var response = await client.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(current, next));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).ShouldBe(new ApiError(message, ApiErrorCodes.Validation));
        (await client.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

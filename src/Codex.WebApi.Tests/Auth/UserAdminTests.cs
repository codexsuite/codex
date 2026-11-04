using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// /api/admin/users: JD (the seeded admin) manages users; anyone else gets 403. The guards hold in the API.
[TestFixture]
public class UserAdminTests : ApiFixture
{
    private Guid _created;

    [Test]
    [Order(1)]
    public async Task Create_ShouldReturnAOneTimePasswordThatSignsInAndMustBeChanged()
    {
        var response = await Client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(" new@example.test ", "New Writer"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<OneTimePasswordDto>())!;
        _created = created.User.Id;
        created.User.ShouldBe(created.User with { Email = "new@example.test", DisplayName = "New Writer", IsAdmin = false, Status = "active", MustChangePassword = true, LastLoginAt = null, LastLoginLabel = null });
        created.User.CreatedLabel.ShouldBe("just now");
        created.OneTimePassword.Length.ShouldBe(16);
        using var newUser = await AuthClient.SignedInAsync(Host, "NEW@example.test", created.OneTimePassword);
        (await newUser.GetFromJsonAsync<MeDto>("/api/auth/me"))!.MustChangePassword.ShouldBeTrue();
        var library = await newUser.GetFromJsonAsync<LibraryDto>("/api/library");
        library.ShouldNotBeNull();
        library.Series.ShouldBeEmpty();
        library.Standalone.ShouldBeEmpty();
    }

    [Test]
    [Order(2)]
    public async Task Create_WhenTheEmailIsTakenInAnyCase_ShouldBe409()
    {
        var response = await Client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("Jd@Codex.Local", "Copy"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(3)]
    public async Task List_ShouldShowEveryUserWithStatusAndLastLogin()
    {
        var users = (await Client.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!;

        users.Select(u => u.Email).ShouldBe(["new@example.test", TestUsers.JdEmail, "ana@codex.local"], ignoreOrder: true);
        var created = users.Single(u => u.Id == _created);
        created.LastLoginLabel.ShouldBe("just now");
    }

    [TestCase("GET")]
    [TestCase("POST")]
    [Order(4)]
    public async Task Admin_WhenTheUserIsNotAnAdmin_ShouldBe403(string method)
    {
        using var client = Host.CreateClient().ActingAs(_created);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/admin/users")
        {
            Content = method == "POST" ? JsonContent.Create(new CreateUserRequest("x@example.test", "X")) : null,
        };

        var response = await client.SendAsync(request);

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
    }

    [TestCase(true, null, "You cannot remove your own admin rights.")]
    [TestCase(null, true, "You cannot disable your own account.")]
    [Order(5)]
    public async Task Update_WhenItWouldDemoteOrDisableYourself_ShouldBe409(bool? notAdmin, bool? disabled, string message)
    {
        var response = await Client.PatchAsJsonAsync(
            $"/api/admin/users/{SeedIds.Users.Jd}", new UpdateUserRequest(notAdmin is null ? null : false, disabled, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Error.ShouldBe(message);
    }

    [Test]
    [Order(6)]
    public async Task Update_WhenAnotherAdminIsDemoted_ShouldKeepAnActiveAdmin()
    {
        (await Client.PatchAsJsonAsync($"/api/admin/users/{_created}", new UpdateUserRequest(IsAdmin: true, null, null))).EnsureSuccessStatusCode();
        using var second = Host.CreateClient().ActingAs(_created);

        // The new admin may demote JD...
        var demoted = await second.PatchAsJsonAsync($"/api/admin/users/{SeedIds.Users.Jd}", new UpdateUserRequest(IsAdmin: false, null, null));
        (await demoted.Content.ReadFromJsonAsync<AdminUserDto>())!.IsAdmin.ShouldBeFalse();

        // ...and is then the last active admin, whom nothing can remove.
        var last = await second.PatchAsJsonAsync($"/api/admin/users/{_created}", new UpdateUserRequest(IsAdmin: false, null, null));
        (await last.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        (await Client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    [Order(7)]
    public async Task Update_WhenTheUserIsUnknown_ShouldBe404()
    {
        using var admin = Host.CreateClient().ActingAs(_created);

        var response = await admin.PatchAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}", new UpdateUserRequest(null, true, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}

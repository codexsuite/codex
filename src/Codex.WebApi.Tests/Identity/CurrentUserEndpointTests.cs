using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Identity;

[TestFixture]
public class CurrentUserEndpointTests : ApiFixture
{
    [Test]
    public async Task Get_ShouldReturnTheSeededUserWithInitials()
    {
        var me = await Client.GetFromJsonAsync<MeDto>("/api/auth/me");

        me.ShouldNotBeNull();
        me.Id.ShouldBe(SeedIds.Users.Jd);
        me.Email.ShouldBe("jd@codex.local");
        me.DisplayName.ShouldBe("Joel Dickson");
        me.Initials.ShouldBe("JD");
        me.IsAdmin.ShouldBeTrue();
    }
}

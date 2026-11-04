using Codex.Contracts;

namespace Codex.TestSupport.Hosting;

// Who a test host's requests act as when they carry no sign-in cookie (TestAuthentication). Jd is the default for
// every fixture, so existing tests run signed in as the seeded author; Anonymous sends requests with no user at all.
public sealed record TestUser(Guid? Id)
{
    public static TestUser Jd { get; } = new(SeedIds.Users.Jd);

    public static TestUser Anonymous { get; } = new((Guid?)null);

    public static TestUser Of(Guid id) => new(id);
}

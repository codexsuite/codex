using Codex.AcceptanceTests.Pages.Shell;
using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests.Harness;

[TestFixture]
public class ShellSmokeTests : AcceptanceFixture
{
    [Test]
    public async Task Root_WhenOpened_ShouldShowTheAppShell()
    {
        await Page.GotoAsync("/");

        await Expect(new AppShell(Page).Root).ToBeVisibleAsync();
    }
}

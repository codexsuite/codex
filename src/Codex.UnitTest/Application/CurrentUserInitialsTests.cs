using Codex.Application.Identity;

namespace Codex.UnitTest.Application;

[TestFixture]
public class CurrentUserInitialsTests
{
    [TestCase("Joel Dickson", "JD")]
    [TestCase("ada lovelace", "AL")]
    [TestCase("Mary Ann  Evans", "ME")]
    [TestCase("Cher", "C")]
    [TestCase("  ", "")]
    public void Initials_WhenNameHasWords_ShouldTakeFirstAndLastLetters(string displayName, string expected)
    {
        UserInitials.From(displayName).ShouldBe(expected);
    }
}

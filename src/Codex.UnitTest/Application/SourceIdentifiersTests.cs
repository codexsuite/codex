using Codex.Application.Features.References;

namespace Codex.UnitTest.Application;

[TestFixture]
public class SourceIdentifiersTests
{
    [TestCase("9780140449136", "9780140449136")]
    [TestCase("978-0-14-044913-6", "9780140449136")]
    [TestCase("ISBN 978 0 14 044913 6", "9780140449136")]
    [TestCase("0140449132", "9780140449136")]
    [TestCase("0-306-40615-2", "9780306406157")]
    [TestCase("080442957X", "9780804429573")]
    public void NormaliseIsbn_WhenValid_ShouldGiveIsbn13(string raw, string expected)
    {
        SourceIdentifiers.NormaliseIsbn(raw).ShouldBe(expected);
    }

    [TestCase("9780140449130")]
    [TestCase("0140449133")]
    [TestCase("12345")]
    [TestCase("")]
    [TestCase(null)]
    public void NormaliseIsbn_WhenInvalid_ShouldBeNull(string? raw)
    {
        SourceIdentifiers.NormaliseIsbn(raw).ShouldBeNull();
    }

    [TestCase("10.1000/xyz123", "10.1000/xyz123")]
    [TestCase("https://doi.org/10.1000/XYZ123", "10.1000/XYZ123")]
    [TestCase("doi:10.1000/xyz123", "10.1000/xyz123")]
    [TestCase("  http://dx.doi.org/10.1000/xyz123 ", "10.1000/xyz123")]
    public void NormaliseDoi_WhenValid_ShouldStripThePrefix(string raw, string expected)
    {
        SourceIdentifiers.NormaliseDoi(raw).ShouldBe(expected);
    }

    [TestCase("not a doi")]
    [TestCase("11.1000/abc")]
    [TestCase("10.1/abc")]
    public void NormaliseDoi_WhenInvalid_ShouldBeNull(string raw)
    {
        SourceIdentifiers.NormaliseDoi(raw).ShouldBeNull();
    }

    [TestCase("https://example.test/a", true)]
    [TestCase("ftp://example.test/a", false)]
    [TestCase("example.test/a", false)]
    [TestCase("javascript:alert(1)", false)]
    public void NormaliseUrl_ShouldAcceptOnlyAbsoluteWebAddresses(string raw, bool valid)
    {
        (SourceIdentifiers.NormaliseUrl(raw) is not null).ShouldBe(valid);
    }
}

using Codex.Application.Features.Print;
using Codex.Contracts.Errors;

namespace Codex.UnitTest.Application;

[TestFixture]
public class EffectiveDpiTests
{
    [Test]
    public void Compute_ShouldJudgeTheTargetOnTheUnroundedDpi()
    {
        // A4 text width is 6.7677 in: 2030 px is 299.96 dpi, shown as 300 but still below the target.
        var dpi = EffectiveDpi.Compute(2030, 1353, "a4", 1, "float-top");

        dpi.EffectiveDpi.ShouldBe(300);
        dpi.Status.ShouldBe("low");
    }

    [Test]
    public void Compute_ForATallImage_ShouldFitTheTextHeight()
    {
        var dpi = EffectiveDpi.Compute(1000, 3000, "5.5x8.5", 1, "page"); // 4 x 7 in box

        (dpi.PlacedHeightIn, dpi.PlacedWidthIn, dpi.EffectiveDpi, dpi.Status).ShouldBe((7, 2.333, 428.6, "ok"));
    }

    [Test]
    public void Compute_ForAWideImage_ShouldBeLimitedByTheBoxWidth()
    {
        var dpi = EffectiveDpi.Compute(3000, 300, "6x9", 2, "column"); // column (4.5 - 0.25) / 2 = 2.125 in

        (dpi.PlacedWidthIn, dpi.EffectiveDpi).ShouldBe((2.125, 1411.8));
    }

    [Test]
    public void Compute_WithAnUnknownTrim_ShouldThrowValidation()
    {
        var thrown = Should.Throw<ApiException>(() => EffectiveDpi.Compute(100, 100, "tabloid", 1, "column"));

        thrown.Code.ShouldBe(ApiErrorCodes.Validation);
    }
}

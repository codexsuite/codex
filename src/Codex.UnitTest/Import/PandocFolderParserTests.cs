using System.Text;
using System.Text.RegularExpressions;
using Codex.Application.Features.Import;
using Codex.Application.Prose;
using Codex.TestSupport.Fixtures;

namespace Codex.UnitTest.Import;

// The pandoc source against the made-up Players Guide folder (Fixtures/import/players-guide-campaign).
[TestFixture]
public partial class PandocFolderParserTests
{
    private static ImportUpload Upload(Func<string, bool>? sendImage = null, bool imagesAsNames = false)
    {
        var files = PlayersGuideCampaign.Files();
        var images = files.Where(f => PlayersGuideCampaign.IsImage(f.Path) && (sendImage?.Invoke(f.Path) ?? true)).ToList();
        return new ImportUpload(
            "pandoc",
            files.Where(f => !PlayersGuideCampaign.IsImage(f.Path)).Select(f => new ImportFile(f.Path, File.ReadAllBytes(f.File))).ToList(),
            imagesAsNames ? images.Select(i => new ImportSkippedItem(i.Path, ImportLimits.NotMarkdown)).ToList() : [],
            imagesAsNames ? [] : images.Select(i => new ImportImage(i.Path, new FileInfo(i.File).Length, () => File.OpenRead(i.File))).ToList());
    }

    private static ImportUpload Texts(params (string Path, string Text)[] files) =>
        new("pandoc", files.Select(f => new ImportFile(f.Path, Encoding.UTF8.GetBytes(f.Text))).ToList());

    private static IEnumerable<ImportScenePlan> Scenes(ImportPlan plan) => plan.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes);

    // Ids numbered in order of appearance, as the .expected files hold them.
    private static string Numbered(string prose)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        return Uuid().Replace(prose, m =>
        {
            if (!ids.TryGetValue(m.Value, out var n))
            {
                ids[m.Value] = n = ids.Count + 1;
            }
            return $"00000000-0000-4000-8000-{n:000000000000}";
        });
    }

    [Test]
    public void Parse_WhenPlayersGuideExcerpts_ShouldMakeAnActPerPartWithItsOpenerArt()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        plan.Acts.Select(a => a.Title).ShouldBe(["Introduction to Velmara", "Classes and Archetypes", "Magic in Velmara"]);
        plan.Acts.Select(a => a.Opener?.ImagePath).ShouldBe([PlayersGuideCampaign.PlacedImages[0], PlayersGuideCampaign.PlacedImages[5], PlayersGuideCampaign.PlacedImages[7]]);
        plan.Book.Title.ShouldBe("Players Guide to The Realm of Velmara");
        plan.Book.Author.ShouldBe("Joel Dickson");
        plan.ImagePaths().ShouldBe(PlayersGuideCampaign.PlacedImages);
    }

    [Test]
    public void Parse_WhenHeadingsAreNumberedByHand_ShouldMakeChaptersWithoutTheNumbers()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        plan.Acts.SelectMany(a => a.Chapters).Select(c => c.Title).ShouldBe(
            ["Welcome", "The World of Velmara", "The History of Velmara", "The Themes of Velmara", "Fighter", "Ranger", "Introduction", "Elemental Shrines"]);
        plan.Acts.SelectMany(a => a.Chapters).ShouldAllBe(c => c.Scenes.Count == 1 && c.IsNumbered);
        var fighter = Scenes(plan).ElementAt(4).Prose!;
        fighter.ShouldStartWith("# Overview\n\nFighters in Velmara");
        fighter.ShouldContain("\n# Kits\n");
        fighter.ShouldContain("\n## Mage Hunter\n");
    }

    [Test]
    public void Parse_WhenPlayersGuideExcerpts_ShouldStoreEachChapterAsExpected()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        var expected = Directory.GetFiles(PlayersGuideCampaign.Expected, "*.md").Order(StringComparer.Ordinal).Select(File.ReadAllText).ToList();
        Scenes(plan).Select(s => Numbered(s.Prose!)).ShouldBe(expected);
    }

    [Test]
    public void Parse_WhenPlayersGuideExcerpts_ShouldLeaveNoLatexOutsideAFence()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        foreach (var text in Scenes(plan).Select(s => s.Prose!).Concat(plan.Entries.Select(e => e.Body!)))
        {
            var document = ProseDocument.Parse(text);
            var loose = document.Blocks
                .Where(b => b.Kind is ProseBlockKind.Paragraph or ProseBlockKind.Heading or ProseBlockKind.List or ProseBlockKind.Quote)
                .Select(b => document.Text[b.Start..b.End])
                .Where(b => LatexCommand().IsMatch(b))
                .ToList();
            loose.ShouldBeEmpty();
        }
        string.Concat(Scenes(plan).Select(s => s.Prose)).ShouldNotContain("DndDropCapLine");
        Scenes(plan).First().Prose!.ShouldContain("\n\nWelcome, adventurer, to the realm of Velmara");
    }

    [Test]
    public void Parse_WhenDropCapsAreUsed_ShouldAskForFantasyRpgWithDropCapsOn()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        plan.Layout.ShouldNotBeNull();
        plan.Layout.Template.ShouldBe("fantasy-rpg");
        plan.Layout.Options!["drop-caps"].GetBoolean().ShouldBeTrue();
    }

    [Test]
    public void Parse_WhenImagesArePlaced_ShouldMakeFiguresWithPlacementAndCaption()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        var figures = Scenes(plan).SelectMany(s => s.Figures ?? []).ToList();
        figures.Select(f => (f.ImagePath, f.Placement, f.Caption)).ShouldBe(
        [
            (PlayersGuideCampaign.PlacedImages[1], "column", "A lantern bearer with travellers in the age of Harvest"),
            (PlayersGuideCampaign.PlacedImages[2], "float-top", "The drifting Tower of the Cartographer's Guild"),
            (PlayersGuideCampaign.PlacedImages[3], "column", "Earth: Lin Daohe (\"Lin Steady as Stone\") Human Female"),
            (PlayersGuideCampaign.PlacedImages[4], "float-top", null),
            (PlayersGuideCampaign.PlacedImages[6], "column",
                "Marshal Oren Tallis, a patient strategist and commander of the Velmaran legions, whose steady judgement and quiet leadership helped to build a league that spans half the coast."),
        ]);
        foreach (var scene in Scenes(plan))
        {
            ProseDocument.Parse(scene.Prose!).ListEmbeds().Where(e => e.Kind == ProseEmbedKind.Figure).Select(e => e.Id)
                .ShouldBe((scene.Figures ?? []).Select(f => f.Id));
        }
    }

    [Test]
    public void Parse_WhenSpellHeadersAreInALatexFence_ShouldMakeSpellsEntriesWithTheirFields()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        plan.Entries.Select(e => (e.Category, e.Name)).ShouldBe([("Spells", "Earthen Armor (Earth)"), ("Spells", "Mineral Infusion (Earth)")]);
        var infusion = plan.Entries[1];
        infusion.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["level-school"] = "4th-level alteration",
            ["casting-time"] = "1 round",
            ["range"] = "Touch",
            ["components"] = "V, S, M (a small gem worth at least 100 gp)",
            ["duration"] = "1 turn/level",
        });
        infusion.Body!.ShouldContain("\n\n1. Flaming (weapons only)");
        infusion.Body.ShouldEndWith("The material component is consumed upon casting.\n");
        var introduction = Scenes(plan).ElementAt(6);
        introduction.Prose!.ShouldEndWith($"::spell{{kb=\"{plan.Entries[0].Id}\"}}\n\n::spell{{kb=\"{infusion.Id}\"}}\n");
        introduction.Entries.ShouldBe(plan.Entries.Select(e => e.Key));
        plan.Categories!.ShouldHaveSingleItem().Fields.Select(f => f.Key).ShouldBe(PandocChapterReader.SpellFields);
    }

    [Test]
    public void Parse_WhenBreaksWrapAPartOpener_ShouldDropThemButKeepAManualBreak()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        var proses = Scenes(plan).Select(s => s.Prose!).ToList();
        proses.Count(p => p.Contains("::pagebreak", StringComparison.Ordinal)).ShouldBe(1);
        proses[4].ShouldEndWith("\n\n::pagebreak\n");
    }

    [Test]
    public void Parse_WhenTablesAndCommentsAreInTheText_ShouldKeepThemWhole()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        var fighter = Scenes(plan).ElementAt(4).Prose!;
        fighter.ShouldContain("\n\n| Kit | Recommendations for Velmara |\n| --- | --- |\n| Myrmidon | Sworn to a city watch would bea  good city hook |\n");
        fighter.ShouldContain("\n\n:::comment\n<!--\nKits\n");
        Scenes(plan).First().Prose!.ShouldStartWith(":::comment\n<!--\nRead aloud\n");
        Scenes(plan).ElementAt(2).Prose!.ShouldContain("\n\n:::comment\n<!--山风月, -->\n:::\n\n::figure{");
    }

    [Test]
    public void Parse_WhenOtherFilesAreInTheFolder_ShouldListThemAsSkipped()
    {
        var plan = new PandocFolderParser().Parse(Upload());

        plan.Skipped.Select(s => (s.Path, s.Reason)).ShouldBe(
        [
            ("players-guide-campaign/Build.ps1", ImportLimits.NotMarkdown),
            ("players-guide-campaign/README.md", "Not one of the chapters PlayersGuide.yaml lists"),
            (PlayersGuideCampaign.UnusedImage, PandocFolderParser.NotUsedImage),
            ("players-guide-campaign/index.md", "Not one of the chapters PlayersGuide.yaml lists"),
        ]);
    }

    [Test]
    public void Parse_WhenAReferencedImageIsMissing_ShouldLeaveItsFigureOutAndListIt()
    {
        var missing = PlayersGuideCampaign.PlacedImages[2];

        var plan = new PandocFolderParser().Parse(Upload(path => path != missing && path != PlayersGuideCampaign.PlacedImages[5]));

        Scenes(plan).SelectMany(s => s.Figures ?? []).Select(f => f.ImagePath).ShouldNotContain(missing);
        Scenes(plan).ElementAt(2).Prose!.Split("::figure").Length.ShouldBe(3);
        plan.Acts[1].Opener.ShouldBeNull();
        plan.Skipped.ShouldContain(new ImportSkippedItem(missing, PandocFolderParser.MissingImage + "; the figure was left out"));
        plan.Skipped.ShouldContain(new ImportSkippedItem(PlayersGuideCampaign.PlacedImages[5], PandocFolderParser.MissingImage + "; the act has no opener art"));
    }

    [Test]
    public void Parse_WhenImagesAreOnlyNamed_ShouldStillPlaceThemForThePreview()
    {
        var plan = new PandocFolderParser().Parse(Upload(imagesAsNames: true));

        plan.ImagePaths().ShouldBe(PlayersGuideCampaign.PlacedImages);
        ImportPreview.From("pandoc", plan).Images.ShouldBe(PlayersGuideCampaign.PlacedImages);
        plan.Skipped.Select(s => s.Path).ShouldBe(
            ["players-guide-campaign/Build.ps1", "players-guide-campaign/README.md", PlayersGuideCampaign.UnusedImage, "players-guide-campaign/index.md"]);
    }

    [Test]
    public void Parse_WhenNoDefaultsFile_ShouldReadChapterFilesInNumberOrder()
    {
        var plan = new PandocFolderParser().Parse(Texts(
            ("book/Chapter 10.md", "# 10.1 Last\n\nTen."),
            ("book/Chapter 2.md", "# 2.1 Second\n\nTwo."),
            ("book/Chapter 9.md", "# Ninth\n\nNine."),
            ("book/draft.md", "# Draft\n")));

        plan.Acts.Select(a => a.Title).ShouldBe(["Chapter 2", "Chapter 9", "Chapter 10"]);
        plan.Acts.SelectMany(a => a.Chapters).Select(c => c.Title).ShouldBe(["Second", "Ninth", "Last"]);
        plan.Skipped.ShouldBe([new ImportSkippedItem("book/draft.md", "Not a chapter file (Chapter N.md)")]);
        plan.Layout.ShouldBeNull();
    }

    [Test]
    public void Parse_WhenDefaultsListAMissingFile_ShouldSkipItAndKeepTheOrder()
    {
        var plan = new PandocFolderParser().Parse(Texts(
            ("b/book.yaml", "input-files:\n  - two.md\n  - gone.md\n  - one.md\n"),
            ("b/one.md", "# One\n\nText."),
            ("b/two.md", "# Two\n\nText.")));

        plan.Acts.SelectMany(a => a.Chapters).Select(c => c.Title).ShouldBe(["Two", "One"]);
        plan.Skipped.ShouldBe([new ImportSkippedItem("b/gone.md", "Listed in book.yaml but not in the folder")]);
        plan.Book.Title.ShouldBe("b");
    }

    [Test]
    public void Parse_WhenTheFolderHasNeitherDefaultsNorChapterFiles_ShouldRefuseIt()
    {
        Should.Throw<Codex.Contracts.Errors.ApiException>(() => new PandocFolderParser().Parse(Texts(("b/notes.md", "Hi."))))
            .Message.ShouldContain("no pandoc defaults file");
    }

    [Test]
    public void Without_WhenAnImageCouldNotBeStored_ShouldDropItsFigureLineAndOpener()
    {
        var plan = new PandocFolderParser().Parse(Upload());
        var refused = new ImportSkippedItem(PlayersGuideCampaign.PlacedImages[6], "Images can be at most 64 megapixels.");
        var opener = new ImportSkippedItem(PlayersGuideCampaign.PlacedImages[0], "Not uploaded with the import.");

        var without = ImportImages.Without(plan, [refused, opener]);

        without.ImagePaths().ShouldNotContain(refused.Path);
        without.Acts[0].Opener.ShouldBeNull();
        var fighter = Scenes(without).ElementAt(4);
        fighter.Figures.ShouldBeEmpty();
        fighter.Prose!.ShouldNotContain("::figure");
        fighter.Prose!.ShouldContain("Optional Kits can be applied to Fighter Class in Velmara\n\n## Mage Hunter");
        without.Skipped.ShouldContain(new ImportSkippedItem(refused.Path, "Images can be at most 64 megapixels. The image was left out."));
    }

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"\\[A-Za-z]+")]
    private static partial Regex LatexCommand();
}

using System.Reflection;
using Codex.Application;
using Codex.Contracts;
using Codex.Core;
using Codex.TestSupport;
using Codex.WebApi;

namespace Codex.UnitTest.Architecture;

[TestFixture]
public class ArchitectureTests
{
    private static readonly Assembly[] Production =
    [
        typeof(DisplayDate).Assembly,
        typeof(ICurrentUser).Assembly,
        ApplicationMarker.Assembly,
        typeof(ApiMarker).Assembly,
    ];

    private static readonly string[] ProductionProjects =
    [
        "Codex.Core/Codex.Core.csproj",
        "Codex.Contracts/Codex.Contracts.csproj",
        "Codex.Application/Codex.Application.csproj",
        "Codex.WebApi/Codex.WebApi.csproj",
    ];

    [Test]
    public void Features_ShouldNotReferenceOtherFeatures()
    {
        FeatureScan.CrossFeatureReferences(Production).ShouldBeEmpty();
    }

    [Test]
    public void Features_ShouldNotReferenceMcp()
    {
        McpScan.FeatureReferencesToMcp(Production).ShouldBeEmpty();
    }

    [Test]
    public void McpScan_WhenFeatureUsesMcp_ShouldReportIt()
    {
        McpScan.FeatureReferencesToMcp([typeof(ArchitectureTests).Assembly])
            .ShouldBe(["Codex.Application.Features.Yak.YakUsesMcp -> Codex.Application.Mcp.McpItem"]);
    }

    [Test]
    public void Production_ShouldNotReferenceTestSupport()
    {
        Production.SelectMany(a => AssemblyScan.ReferencesTo(a, "Codex.TestSupport")).ShouldBeEmpty();
        ProductionProjectFiles().SelectMany(f => AssemblyScan.ProjectFileReferences(f, "Codex.TestSupport")).ShouldBeEmpty();
    }

    [Test]
    public void Production_ShouldNotReferenceEfCore()
    {
        Production.SelectMany(a => AssemblyScan.ReferencesTo(a, "Microsoft.EntityFrameworkCore")).ShouldBeEmpty();
        ProductionProjectFiles().SelectMany(f => AssemblyScan.ProjectFileReferences(f, "EntityFrameworkCore")).ShouldBeEmpty();
    }

    // The checks above are only worth having if they can fail. These run the same scans over the test assembly,
    // which holds deliberate violations and does reference TestSupport.
    [Test]
    public void FeatureScan_WhenFeatureUsesAnotherFeature_ShouldReportEachKindOfReference()
    {
        var violations = FeatureScan.CrossFeatureReferences([typeof(ArchitectureTests).Assembly]);

        violations.ShouldBe(
        [
            "Codex.Application.Features.Yak.YakByCall -> Codex.Application.Features.Zebra.ZebraHelper",
            "Codex.Application.Features.Yak.YakByField -> Codex.Application.Features.Zebra.ZebraHelper",
            "Codex.Application.Features.Yak.YakByGeneric -> Codex.Application.Features.Zebra.ZebraHelper",
        ]);
    }

    [Test]
    public void AssemblyScan_WhenAssemblyUsesTestSupport_ShouldReportIt()
    {
        AssemblyScan.ReferencesTo(typeof(ArchitectureTests).Assembly, "Codex.TestSupport").ShouldBe(["Codex.TestSupport"]);
        AssemblyScan.ProjectFileReferences(
            Path.Combine(RepoPaths.Root, "src", "Codex.UnitTest", "Codex.UnitTest.csproj"), "Codex.TestSupport").ShouldNotBeEmpty();
    }

    [Test]
    public void AssemblyScan_WhenReferenceIsEfCore_ShouldReportIt()
    {
        AssemblyScan.WithPrefix(["System.Linq", "Microsoft.EntityFrameworkCore.Relational"], "Microsoft.EntityFrameworkCore")
            .ShouldBe(["Microsoft.EntityFrameworkCore.Relational"]);
        AssemblyScan.Mentioning(
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore\" Version=\"10.0.0\" />", "EntityFrameworkCore")
            .ShouldBe(["EntityFrameworkCore"]);
    }

    private static IEnumerable<string> ProductionProjectFiles()
        => ProductionProjects.Select(p => Path.Combine(RepoPaths.Root, "src", p));
}

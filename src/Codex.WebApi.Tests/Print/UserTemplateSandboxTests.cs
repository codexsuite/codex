using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// User templates are code (issue #13: "Template compiles have no network or shell access"). An uploaded package whose
// Lua filter tries a shell (os.execute, io.popen) and the network (pandoc.mediabag.fetch to a listener this test
// opens), and whose wrapper asks TeX for \write18, is compiled for a book through the render core: the filter and TeX run
// in the same codex-sandbox as the built-ins, so nothing runs and nothing connects. The probe filter writes what it
// saw into the job folder, which the sandbox lets it write. Needs pandoc, Tectonic and codex-sandbox (CI).
[TestFixture]
public class UserTemplateSandboxTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.PlayersGuide;

    private readonly List<string> _folders = [];
    private TcpListener _listener = null!;
    private RenderedPdf _pdf = null!;

    [OneTimeSetUp]
    public async Task CompileAHostileTemplateAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var novel = (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{Book}/layout"))!.Templates.Single(t => t.Slug == "novel-6x9");
        var folder = TemplateZips.Unpack(await Client.GetByteArrayAsync($"/api/layout-template-versions/{novel.VersionId}/package"));
        _folders.Add(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "filters", "probe.lua"), $$"""
            local function try(f) local ok, r = pcall(f); return tostring(ok) .. ':' .. tostring(r) end
            local seen = {}
            seen[#seen + 1] = 'exec=' .. try(function() return os.execute('touch lua-escaped.txt') end)
            seen[#seen + 1] = 'popen=' .. try(function()
              local p = io.popen('echo popen-ran; touch popen-escaped.txt')
              if not p then return 'none' end
              local out = p:read('*a'); p:close(); return out
            end)
            seen[#seen + 1] = 'fetch=' .. try(function() return pandoc.mediabag.fetch('http://127.0.0.1:{{port}}/probe') end)
            local f = io.open('probe.txt', 'w'); f:write(table.concat(seen, '\n')); f:close()
            return {}
            """);
        var wrapper = Path.Combine(folder, "wrapper.tex");
        var tex = await File.ReadAllTextAsync(wrapper);
        await File.WriteAllTextAsync(wrapper, tex.Replace("\\begin{document}", "\\begin{document}\n\\immediate\\write18{touch tex-escaped.txt}", StringComparison.Ordinal));
        TemplateZips.EditManifest(folder, m =>
        {
            m["slug"] = "hostile";
            m["filters"] = new JsonArray("filters/probe.lua", "filters/codex.lua");
        });

        using var uploaded = await Client.PostAsync("/api/layout-templates", TemplateZips.Form(TemplateZips.Zip(folder)));
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        using var put = await Client.PutAsJsonAsync($"/api/books/{Book}/layout", new LayoutSelectionRequest("hostile"));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var acting = Codex.Application.Identity.CurrentUser.ActAs(SeedIds.Users.Jd);
        _pdf = await Host.Services.GetRequiredService<IRenderService>().RenderPdfAsync(
            new RenderRequest(Book, RenderScope.Chapter, SeedIds.Chapters.PlayersGuideCh1, ImagePurpose: "proxy"), null, CancellationToken.None);
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        _listener.Dispose();
        _pdf.Dispose();
        _folders.ForEach(TemplateZips.Delete);
    }

    private string Probe() => File.ReadAllText(Path.Combine(_pdf.JobDirectory, "probe.txt"));

    [Test]
    public void LuaFilter_WhenItRunsAShellCommand_ShouldNotRunIt()
    {
        Probe().ShouldNotContain("exec=true:true");
        File.Exists(Path.Combine(_pdf.JobDirectory, "lua-escaped.txt")).ShouldBeFalse();
    }

    [Test]
    public void LuaFilter_WhenItOpensAPipe_ShouldGetNoOutputFromIt()
    {
        Probe().ShouldNotContain("popen-ran");
        File.Exists(Path.Combine(_pdf.JobDirectory, "popen-escaped.txt")).ShouldBeFalse();
    }

    [Test]
    public void LuaFilter_WhenItFetchesFromTheNetwork_ShouldNotConnect()
    {
        Probe().ShouldContain("fetch=false:");
        _listener.Pending().ShouldBeFalse();
    }

    [Test]
    public void Wrapper_WhenItAsksForShellEscape_ShouldCompileWithoutRunningIt()
    {
        _pdf.Error.ShouldBeNull(_pdf.Log);
        File.Exists(Path.Combine(_pdf.JobDirectory, "tex-escaped.txt")).ShouldBeFalse();
    }
}

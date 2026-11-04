using System.Net.Http.Json;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.TestSupport.Hosting;
using Dapper;
using Npgsql;
using static Codex.WebApi.Tests.Auth.SweepNeeds;

namespace Codex.WebApi.Tests.Auth;

// The access rule over the whole HTTP surface (#80, extending #29's ownership sweep): every /api route that names
// something inside a book, with the least role it needs, and a URL naming JD's item. JD's items sit in Product
// Engineering (a standalone book) and, for the asset and figure, Players Guide (in the Forsaken Gods series), so a
// sweep user gets one grant on each of Product Engineering and the series. AccessSweepTests calls every route as a
// stranger; AccessSweepRoleTests as a viewer, a commenter and an editor. Bodies are {} (or a minimal form), so the access
// check has to come before any validation of the body.
public static class AccessSweep
{
    // Routes that take no book or in-book id: lists filtered by access, global catalogues, the user's own account.
    public static readonly string[] NoBookIds =
    [
        "GET /api/health",
        "GET /api/library",
        "GET /api/prompts",
        "POST /api/books",
        "POST /api/series",
        "POST /api/imports",
        "POST /api/imports/preview",
        "POST /api/blocks/move",
        // The upload link is the credential (#65): it names its own book and user, so there is no id to sweep.
        "PUT /api/asset-uploads/{token}",
        "POST /api/citations",
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "GET /api/auth/me",
        "PATCH /api/auth/me",
        "POST /api/auth/password",
        "GET /api/admin/users",
        "POST /api/admin/users",
        "PATCH /api/admin/users/{userId:guid}",
        "POST /api/admin/users/{userId:guid}/reset-password",
        "POST /api/admin/users/{userId:guid}/revoke-tokens",
        "GET /api/auth/tokens",
        "POST /api/auth/tokens",
        "GET /api/auth/device-sessions",
        "POST /api/auth/device-sessions",
        "DELETE /api/auth/device-sessions/current",
        // A new layout template of the signed-in user's own (#13): no id in the route.
        "POST /api/layout-templates",
        // Settings -> AI, Prompt templates (L18.1): rows are the signed-in user's own (user_id), never a book's; another
        // user's id is 404 (PromptTemplateTests).
        "GET /api/ai/templates",
        "POST /api/ai/templates",
        "PUT /api/ai/templates/{templateId}",
        "DELETE /api/ai/templates/{templateId}",
        "POST /api/ai/templates/{templateId}/duplicate",
        // AI integrations (#36) are the signed-in user's own: lists and creates filter by owner.
        "GET /api/ai/integrations",
        "POST /api/ai/integrations",
        "POST /api/ai/integrations/test",
        "GET /api/ai/models",
    ];

    private static readonly HashSet<string> FormRoutes =
    [
        "POST /api/books/{bookId:guid}/assets",
        "POST /api/permission-requests/{id:guid}/granted",
        "POST /api/layout-templates/{templateId:guid}/versions",
        "POST /api/permission-requests/{id:guid}/letter",
    ];

    // Every id-bearing /api route; the URLs name JD's items (ids: the fixture's own rows).
    public static IEnumerable<SweepRoute> Routes()
    {
        var book = SeedIds.Books.ProductEngineering;
        var series = SeedIds.Series.ForsakenGods;
        var act = SeedIds.Acts.Introduction;
        var chapter = SeedIds.Chapters.Ch3;
        var scene = SeedIds.Scenes.Ch3Sc1;
        var beat = SeedIds.Beats.Ch3Sc1EarlyAgileYears;
        var entry = SeedIds.KbEntries.ConwaysLaw;
        var label = SeedIds.Labels.ReadyToWrite;
        var source = SeedIds.References.BeckXp;
        var use = SeedIds.ReferenceUses.BeckXpCh3Sc1;
        var flag = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote;
        var permission = SeedIds.PermissionRequests.TeamTopologies;
        var asset = SeedIds.Assets.PlayersGuideFigure;
        var figure = SeedIds.Figures.PlayersGuideFigure;
        var generation = SeedIds.AiGenerations.Ch5Sc2Pending;
        var addition = SeedIds.KbProgressions.Ch4Sc1GoodhartsLaw;

        // Books and series
        yield return new("GET", "/api/books/{id:guid}", View, ids => $"/api/books/{book}");
        yield return new("GET", "/api/books/{bookId:guid}/outline", View, ids => $"/api/books/{book}/outline");
        yield return new("GET", "/api/books/{bookId:guid}/manuscript", View, ids => $"/api/books/{book}/manuscript");
        yield return new("GET", "/api/books/{bookId:guid}/beats", View, ids => $"/api/books/{book}/beats");
        yield return new("POST", "/api/books/{bookId:guid}/find", View, ids => $"/api/books/{book}/find");
        yield return new("POST", "/api/books/{bookId:guid}/replace", Write, ids => $"/api/books/{book}/replace");
        yield return new("POST", "/api/books/{bookId:guid}/replace/undo", Write, ids => $"/api/books/{book}/replace/undo");
        yield return new("GET", "/api/books/{bookId:guid}/drafts", View, ids => $"/api/books/{book}/drafts");
        yield return new("GET", "/api/books/{bookId:guid}/import", View, ids => $"/api/books/{book}/import");
        yield return new("GET", "/api/books/{bookId:guid}/kb-additions", View, ids => $"/api/books/{book}/kb-additions");
        yield return new("GET", "/api/books/{bookId:guid}/labels", View, ids => $"/api/books/{book}/labels");
        yield return new("POST", "/api/books/{bookId:guid}/labels", Write, ids => $"/api/books/{book}/labels");
        yield return new("GET", "/api/books/{bookId:guid}/layout", View, ids => $"/api/books/{book}/layout");
        yield return new("PUT", "/api/books/{bookId:guid}/layout", Write, ids => $"/api/books/{book}/layout");
        yield return new("POST", "/api/books/{bookId:guid}/layout/preview", View, ids => $"/api/books/{book}/layout/preview");
        yield return new("POST", "/api/books/{bookId:guid}/layout/upgrade", Write, ids => $"/api/books/{book}/layout/upgrade");
        yield return new("POST", "/api/books/{bookId:guid}/export-source", Write, ids => $"/api/books/{book}/export-source");
        yield return new("GET", "/api/books/{bookId:guid}/assets", View, ids => $"/api/books/{book}/assets");
        yield return new("POST", "/api/books/{bookId:guid}/assets", Write, ids => $"/api/books/{book}/assets");
        yield return new("GET", "/api/books/{bookId:guid}/settings", View, ids => $"/api/books/{book}/settings");
        yield return new("PATCH", "/api/books/{bookId:guid}/settings", Write, ids => $"/api/books/{book}/settings");
        yield return new("GET", "/api/books/{bookId:guid}/settings/layout", View, ids => $"/api/books/{book}/settings/layout");
        yield return new("GET", "/api/books/{bookId:guid}/settings/rules-impact", View, ids => $"/api/books/{book}/settings/rules-impact");
        yield return new("GET", "/api/books/{bookId:guid}/publication-settings", View, ids => $"/api/books/{book}/publication-settings");
        yield return new("PATCH", "/api/books/{bookId:guid}/publication-settings", Write, ids => $"/api/books/{book}/publication-settings");
        yield return new("DELETE", "/api/books/{bookId:guid}/publication-settings/{field}", Write, ids => $"/api/books/{book}/publication-settings/publisher");
        yield return new("PUT", "/api/books/{bookId:guid}/cover", Write, ids => $"/api/books/{book}/cover");
        yield return new("DELETE", "/api/books/{bookId:guid}/cover", Write, ids => $"/api/books/{book}/cover");
        yield return new("POST", "/api/books/{bookId:guid}/archive", Owner, ids => $"/api/books/{book}/archive");
        yield return new("POST", "/api/books/{bookId:guid}/restore", Owner, ids => $"/api/books/{book}/restore");
        yield return new("POST", "/api/books/{bookId:guid}/duplicate", Owner, ids => $"/api/books/{book}/duplicate");
        yield return new("POST", "/api/books/{bookId:guid}/new-edition", Owner, ids => $"/api/books/{book}/new-edition");
        yield return new("GET", "/api/books/{bookId:guid}/publish", View, ids => $"/api/books/{book}/publish");
        yield return new("POST", "/api/books/{bookId:guid}/publish", Owner, ids => $"/api/books/{book}/publish");
        yield return new("PUT", "/api/books/{bookId:guid}/series-number", Owner, ids => $"/api/books/{book}/series-number");
        yield return new("POST", "/api/books/{bookId:guid}/series-move", Owner, ids => $"/api/books/{book}/series-move");
        yield return new("POST", "/api/books/{bookId:guid}/series-move/preview", Owner, ids => $"/api/books/{book}/series-move/preview");
        yield return new("GET", "/api/books/{bookId:guid}/quote-flags", View, ids => $"/api/books/{book}/quote-flags");
        yield return new("POST", "/api/books/{bookId:guid}/references", Write, ids => $"/api/books/{book}/references");
        yield return new("POST", "/api/books/{bookId:guid}/references/import", Write, ids => $"/api/books/{book}/references/import");
        yield return new("POST", "/api/books/{bookId:guid}/references/import/preview", Write, ids => $"/api/books/{book}/references/import/preview");
        yield return new("POST", "/api/books/{bookId:guid}/references/lookup", Write, ids => $"/api/books/{book}/references/lookup");
        yield return new("GET", "/api/books/{id:guid}/kb", View, ids => $"/api/books/{book}/kb");
        yield return new("POST", "/api/books/{id:guid}/kb/categories", Write, ids => $"/api/books/{book}/kb/categories");
        yield return new("POST", "/api/books/{id:guid}/kb/entries", Write, ids => $"/api/books/{book}/kb/entries");
        yield return new("GET", "/api/books/{id:guid}/markers", View, ids => $"/api/books/{book}/markers");
        yield return new("GET", "/api/books/{id:guid}/references/summary", View, ids => $"/api/books/{book}/references/summary");
        yield return new("GET", "/api/books/{id:guid}/sources", View, ids => $"/api/books/{book}/sources");
        yield return new("GET", "/api/books/{id:guid}/sources/csl", View, ids => $"/api/books/{book}/sources/csl");
        yield return new("GET", "/api/books/{id:guid}/sources/export", View, ids => $"/api/books/{book}/sources/export?format=bibtex");
        yield return new("PATCH", "/api/series/{seriesId:guid}", Owner, ids => $"/api/series/{series}");
        yield return new("PUT", "/api/series/{seriesId:guid}/order", Owner, ids => $"/api/series/{series}/order");
        yield return new("GET", "/api/series/{seriesId:guid}/next-number", Owner, ids => $"/api/series/{series}/next-number");
        yield return new("GET", "/api/series/{seriesId:guid}/delete-impact", Owner, ids => $"/api/series/{series}/delete-impact");
        yield return new("DELETE", "/api/series/{seriesId:guid}", Owner, ids => $"/api/series/{series}");
        yield return new("POST", "/api/series/{seriesId:guid}/archive", Owner, ids => $"/api/series/{series}/archive");
        yield return new("POST", "/api/series/{seriesId:guid}/restore", Owner, ids => $"/api/series/{series}/restore");
        yield return new("GET", "/api/books/{bookId:guid}/delete-impact", Owner, ids => $"/api/books/{book}/delete-impact");
        yield return new("DELETE", "/api/books/{bookId:guid}", Owner, ids => $"/api/books/{book}");

        // Sharing (#80)
        yield return new("GET", "/api/books/{bookId:guid}/sharing", View, ids => $"/api/books/{book}/sharing");
        yield return new("POST", "/api/books/{bookId:guid}/sharing/lookup", Owner, ids => $"/api/books/{book}/sharing/lookup");
        yield return new("PUT", "/api/books/{bookId:guid}/sharing/{userId:guid}", Owner, ids => $"/api/books/{book}/sharing/{SeedIds.Users.Ana}");
        yield return new("DELETE", "/api/books/{bookId:guid}/sharing/{userId:guid}", Owner, ids => $"/api/books/{book}/sharing/{SeedIds.Users.Ana}");

        // Structure
        yield return new("POST", "/api/books/{bookId:guid}/acts", Write, ids => $"/api/books/{book}/acts");
        yield return new("PATCH", "/api/acts/{actId:guid}", Write, ids => $"/api/acts/{act}");
        yield return new("POST", "/api/acts/{actId:guid}/archive", Write, ids => $"/api/acts/{act}/archive");
        yield return new("POST", "/api/acts/{actId:guid}/restore", Write, ids => $"/api/acts/{act}/restore");
        yield return new("POST", "/api/acts/{actId:guid}/chapters", Write, ids => $"/api/acts/{act}/chapters");
        yield return new("PUT", "/api/acts/{actId:guid}/position", Write, ids => $"/api/acts/{act}/position");
        yield return new("PUT", "/api/acts/{actId:guid}/opener", Write, ids => $"/api/acts/{act}/opener");
        yield return new("PUT", "/api/chapters/{chapterId:guid}/opener", Write, ids => $"/api/chapters/{chapter}/opener");
        yield return new("POST", "/api/chapters/{chapterId:guid}/scenes", Write, ids => $"/api/chapters/{chapter}/scenes");
        yield return new("PUT", "/api/chapters/{chapterId:guid}/position", Write, ids => $"/api/chapters/{chapter}/position");
        yield return new("GET", "/api/chapters/{chapterId:guid}/archived-scenes", View, ids => $"/api/chapters/{chapter}/archived-scenes");
        yield return new("PATCH", "/api/chapters/{chapterId:guid}", Write, ids => $"/api/chapters/{chapter}");
        yield return new("POST", "/api/chapters/{chapterId:guid}/archive", Write, ids => $"/api/chapters/{chapter}/archive");
        yield return new("POST", "/api/chapters/{chapterId:guid}/restore", Write, ids => $"/api/chapters/{chapter}/restore");
        yield return new("POST", "/api/scenes/{sceneId:guid}/merge-previous", Write, ids => $"/api/scenes/{scene}/merge-previous");
        yield return new("PATCH", "/api/scenes/{sceneId:guid}", Write, ids => $"/api/scenes/{scene}");
        yield return new("PUT", "/api/scenes/{sceneId:guid}/position", Write, ids => $"/api/scenes/{scene}/position");
        yield return new("POST", "/api/scenes/{sceneId:guid}/archive", Write, ids => $"/api/scenes/{scene}/archive");
        yield return new("POST", "/api/scenes/{sceneId:guid}/restore", Write, ids => $"/api/scenes/{scene}/restore");
        yield return new("POST", "/api/scenes/{sceneId:guid}/split", Write, ids => $"/api/scenes/{scene}/split");
        yield return new("PUT", "/api/scenes/{sceneId:guid}/labels", Write, ids => $"/api/scenes/{scene}/labels");
        yield return new("PATCH", "/api/labels/{labelId:guid}", Write, ids => $"/api/labels/{label}");
        yield return new("DELETE", "/api/labels/{labelId:guid}", Write, ids => $"/api/labels/{label}");

        // Manuscript and Write
        yield return new("GET", "/api/scenes/{sceneId:guid}/prose", View, ids => $"/api/scenes/{scene}/prose");
        yield return new("PUT", "/api/scenes/{sceneId:guid}/prose", Write, ids => $"/api/scenes/{scene}/prose");
        yield return new("GET", "/api/scenes/{sceneId:guid}/prose/revisions", View, ids => $"/api/scenes/{scene}/prose/revisions");
        yield return new("POST", "/api/scenes/{sceneId:guid}/prose/revisions/{revisionId:guid}/restore", Write, ids => $"/api/scenes/{scene}/prose/revisions/{Guid.NewGuid()}/restore");
        yield return new("POST", "/api/scenes/{sceneId:guid}/beats", Write, ids => $"/api/scenes/{scene}/beats");
        yield return new("POST", "/api/scenes/{sceneId:guid}/continue", Write, ids => $"/api/scenes/{scene}/continue");
        yield return new("POST", "/api/scenes/{sceneId:guid}/kb-additions", Write, ids => $"/api/scenes/{scene}/kb-additions");
        yield return new("POST", "/api/scenes/{id:guid}/kb", Write, ids => $"/api/scenes/{scene}/kb");
        yield return new("DELETE", "/api/scenes/{id:guid}/kb/{entryId:guid}", Write, ids => $"/api/scenes/{scene}/kb/{entry}");
        yield return new("PATCH", "/api/beats/{beatId:guid}", Write, ids => $"/api/beats/{beat}");
        yield return new("PUT", "/api/beats/{id:guid}/context", Write, ids => $"/api/beats/{beat}/context");
        yield return new("POST", "/api/beats/{beatId:guid}/generate", Write, ids => $"/api/beats/{beat}/generate");
        yield return new("PATCH", "/api/kb-additions/{additionId:guid}", Write, ids => $"/api/kb-additions/{addition}");
        yield return new("GET", "/api/generations/{generationId:guid}/sent", View, ids => $"/api/generations/{generation}/sent");
        yield return new("POST", "/api/generations/{generationId:guid}/apply", Write, ids => $"/api/generations/{generation}/apply");
        yield return new("POST", "/api/generations/{generationId:guid}/discard", Write, ids => $"/api/generations/{generation}/discard");
        yield return new("POST", "/api/generations/{generationId:guid}/undo-apply", Write, ids => $"/api/generations/{generation}/undo-apply");
        yield return new("POST", "/api/generations/{generationId:guid}/undo-discard", Write, ids => $"/api/generations/{generation}/undo-discard");
        yield return new("GET", "/api/figures/{id:guid}", View, ids => $"/api/figures/{figure}");
        yield return new("PATCH", "/api/figures/{id:guid}", Write, ids => $"/api/figures/{figure}");
        yield return new("POST", "/api/figures/{id:guid}/remove", Write, ids => $"/api/figures/{figure}/remove");
        yield return new("POST", "/api/scenes/{sceneId:guid}/figures", Write, ids => $"/api/scenes/{scene}/figures");

        // Knowledge base
        yield return new("GET", "/api/kb/entries/{id:guid}", View, ids => $"/api/kb/entries/{entry}");
        yield return new("PATCH", "/api/kb/entries/{id:guid}", Write, ids => $"/api/kb/entries/{entry}");
        yield return new("DELETE", "/api/kb/entries/{id:guid}", Write, ids => $"/api/kb/entries/{entry}");
        yield return new("GET", "/api/kb/entries/{id:guid}/delete-impact", View, ids => $"/api/kb/entries/{entry}/delete-impact");
        yield return new("POST", "/api/kb/entries/{id:guid}/restore", Write, ids => $"/api/kb/entries/{entry}/restore");
        yield return new("PUT", "/api/kb/entries/{id:guid}/position", Write, ids => $"/api/kb/entries/{entry}/position");
        yield return new("POST", "/api/kb/entries/{id:guid}/promote", Write, ids => $"/api/kb/entries/{entry}/promote");
        // View on the entry it names; the write is on the scene the body names, checked after it is read.
        yield return new("POST", "/api/kb/entries/{id:guid}/make-mention", View, ids => $"/api/kb/entries/{entry}/make-mention");
        yield return new("POST", "/api/kb/entries/{id:guid}/related", Write, ids => $"/api/kb/entries/{entry}/related");
        yield return new("DELETE", "/api/kb/entries/{id:guid}/related/{otherId:guid}", Write, ids => $"/api/kb/entries/{entry}/related/{SeedIds.KbEntries.BrooksLaw}");
        yield return new("GET", "/api/kb/entries/{id:guid}/alias-check", View, ids => $"/api/kb/entries/{entry}/alias-check?alias=Conway");
        yield return new("GET", "/api/kb/entries/{id:guid}/merge-impact", View, ids => $"/api/kb/entries/{entry}/merge-impact?intoId={SeedIds.KbEntries.BrooksLaw}");
        yield return new("POST", "/api/kb/entries/{id:guid}/merge", Write, ids => $"/api/kb/entries/{entry}/merge");
        yield return new("PATCH", "/api/kb/categories/{id:guid}", Write, ids => $"/api/kb/categories/{SeedIds.KbCategories.Laws}");
        yield return new("GET", "/api/kb/categories/{id:guid}/delete-impact", View, ids => $"/api/kb/categories/{SeedIds.KbCategories.Laws}/delete-impact");
        yield return new("DELETE", "/api/kb/categories/{id:guid}", Write, ids => $"/api/kb/categories/{SeedIds.KbCategories.Laws}");

        // References
        yield return new("GET", "/api/references/{id:guid}", View, ids => $"/api/references/{source}");
        yield return new("PATCH", "/api/references/{id:guid}", Write, ids => $"/api/references/{source}");
        yield return new("POST", "/api/references/{id:guid}/archive", Write, ids => $"/api/references/{source}/archive");
        yield return new("DELETE", "/api/references/{id:guid}", Write, ids => $"/api/references/{source}");
        yield return new("POST", "/api/references/{id:guid}/merge", Write, ids => $"/api/references/{source}/merge");
        yield return new("POST", "/api/references/{id:guid}/reopen", Write, ids => $"/api/references/{source}/reopen");
        yield return new("POST", "/api/references/{id:guid}/fair-use", Write, ids => $"/api/references/{source}/fair-use");
        yield return new("POST", "/api/references/{id:guid}/permission", Write, ids => $"/api/references/{source}/permission");
        yield return new("POST", "/api/references/{id:guid}/recheck", Write, ids => $"/api/references/{source}/recheck");
        yield return new("POST", "/api/references/{id:guid}/recheck/accept", Write, ids => $"/api/references/{source}/recheck/accept");
        yield return new("POST", "/api/references/{id:guid}/sign-off", Write, ids => $"/api/references/{source}/sign-off");
        yield return new("PATCH", "/api/reference-uses/{id:guid}", Write, ids => $"/api/reference-uses/{use}");
        yield return new("POST", "/api/citations/{useId:guid}/remove", Write, ids => $"/api/citations/{use}/remove");
        yield return new("POST", "/api/quote-flags/{flagId:guid}/cite", Write, ids => $"/api/quote-flags/{flag}/cite");
        yield return new("POST", "/api/quote-flags/{id:guid}/not-a-quote", Write, ids => $"/api/quote-flags/{flag}/not-a-quote");
        yield return new("POST", "/api/quote-flags/{id:guid}/other-source", Write, ids => $"/api/quote-flags/{flag}/other-source");
        yield return new("PATCH", "/api/permission-requests/{id:guid}", Write, ids => $"/api/permission-requests/{permission}");
        yield return new("GET", "/api/permission-requests/{id:guid}/letter", View, ids => $"/api/permission-requests/{permission}/letter");
        yield return new("POST", "/api/permission-requests/{id:guid}/sent", Write, ids => $"/api/permission-requests/{permission}/sent");
        yield return new("POST", "/api/permission-requests/{id:guid}/granted", Write, ids => $"/api/permission-requests/{permission}/granted");
        yield return new("POST", "/api/permission-requests/{id:guid}/refused", Write, ids => $"/api/permission-requests/{permission}/refused");
        yield return new("POST", "/api/permission-requests/{id:guid}/withdraw", Write, ids => $"/api/permission-requests/{permission}/withdraw");
        yield return new("POST", "/api/permission-requests/{id:guid}/followed-up", Write, ids => $"/api/permission-requests/{permission}/followed-up");
        yield return new("POST", "/api/permission-requests/{id:guid}/letter", Write, ids => $"/api/permission-requests/{permission}/letter");
        yield return new("DELETE", "/api/permission-requests/{id:guid}/letter", Write, ids => $"/api/permission-requests/{permission}/letter");

        // Assets, imports, jobs
        yield return new("GET", "/api/assets/{id:guid}", View, ids => $"/api/assets/{asset}");
        yield return new("GET", "/api/assets/{id:guid}/info", View, ids => $"/api/assets/{asset}/info");
        yield return new("GET", "/api/assets/{id:guid}/dpi", View, ids => $"/api/assets/{asset}/dpi?trim=6x9");
        yield return new("GET", "/api/assets/{id:guid}/renditions/{purpose}", View, ids => $"/api/assets/{asset}/renditions/web");
        yield return new("POST", "/api/assets/{id:guid}/renditions", Write, ids => $"/api/assets/{asset}/renditions");
        // The import banner sits on the imported book: dismissing it is an edit, undoing the import deletes the book.
        yield return new("POST", "/api/imports/{id:guid}/dismiss", Write, ids => $"/api/imports/{ids.ImportId}/dismiss");
        yield return new("POST", "/api/imports/{id:guid}/undo", Owner, ids => $"/api/imports/{ids.ImportId}/undo");
        // Jobs are exports and proofs, which need Write.
        yield return new("GET", "/api/jobs/{id:guid}", Write, ids => $"/api/jobs/{ids.JobId}");

        // Exports and proofs
        yield return new("POST", "/api/books/{bookId:guid}/export/preflight", Write, ids => $"/api/books/{book}/export/preflight");
        yield return new("POST", "/api/books/{bookId:guid}/exports", Write, ids => $"/api/books/{book}/exports");
        yield return new("GET", "/api/books/{bookId:guid}/exports", Write, ids => $"/api/books/{book}/exports");
        yield return new("GET", "/api/exports/{id:guid}", Write, ids => $"/api/exports/{ids.JobId}");
        yield return new("POST", "/api/exports/{id:guid}/cancel", Write, ids => $"/api/exports/{ids.JobId}/cancel");
        yield return new("GET", "/api/exports/{id:guid}/file", Write, ids => $"/api/exports/{ids.JobId}/file");
        yield return new("GET", "/api/books/{bookId:guid}/manuscript-export", Write, ids => $"/api/books/{book}/manuscript-export");
        yield return new("POST", "/api/books/{bookId:guid}/manuscript-exports", Write, ids => $"/api/books/{book}/manuscript-exports");
        yield return new("GET", "/api/books/{bookId:guid}/manuscript-exports", Write, ids => $"/api/books/{book}/manuscript-exports");
        yield return new("POST", "/api/books/{bookId:guid}/proofs", Write, ids => $"/api/books/{book}/proofs");
        yield return new("GET", "/api/books/{bookId:guid}/proofs", Write, ids => $"/api/books/{book}/proofs?chapterId={chapter}");
        yield return new("GET", "/api/proofs/{id:guid}", Write, ids => $"/api/proofs/{ids.ProofId}");
        yield return new("GET", "/api/proofs/{id:guid}/pdf", Write, ids => $"/api/proofs/{ids.ProofId}/pdf");

        // Layout templates (#13): JD's own template; built-ins are everyone's to fork and download.
        yield return new("POST", "/api/layout-templates/{templateId:guid}/versions", Own, ids => $"/api/layout-templates/{ids.TemplateId}/versions");
        yield return new("POST", "/api/layout-templates/{templateId:guid}/archive", Own, ids => $"/api/layout-templates/{ids.TemplateId}/archive");
        yield return new("POST", "/api/layout-template-versions/{versionId:guid}/fork", Own, ids => $"/api/layout-template-versions/{ids.TemplateVersionId}/fork");
        yield return new("GET", "/api/layout-template-versions/{versionId:guid}/package", Own, ids => $"/api/layout-template-versions/{ids.TemplateVersionId}/package");
        yield return new("GET", "/api/layout-template-versions/{versionId:guid}/sample", Own, ids => $"/api/layout-template-versions/{ids.TemplateVersionId}/sample");

        // Personal access tokens (#32): not in a book, but B must not revoke JD's.
        yield return new("DELETE", "/api/auth/tokens/{tokenId:guid}", Own, ids => $"/api/auth/tokens/{ids.TokenId}");

        // Device sessions (#35): B must not revoke JD's.
        yield return new("DELETE", "/api/auth/device-sessions/{sessionId:guid}", Own, ids => $"/api/auth/device-sessions/{ids.DeviceSessionId}");

        // AI integrations (#36): B must not change, delete, default or test JD's.
        yield return new("PATCH", "/api/ai/integrations/{integrationId:guid}", Own, ids => $"/api/ai/integrations/{ids.IntegrationId}");
        yield return new("DELETE", "/api/ai/integrations/{integrationId:guid}", Own, ids => $"/api/ai/integrations/{ids.IntegrationId}");
        yield return new("POST", "/api/ai/integrations/{integrationId:guid}/default", Own, ids => $"/api/ai/integrations/{ids.IntegrationId}/default");
        yield return new("POST", "/api/ai/integrations/{integrationId:guid}/test", Own, ids => $"/api/ai/integrations/{ids.IntegrationId}/test");
    }

    // Every id the routes need beyond the seed, inserted into the fixture's database as JD's.
    public static async Task<SweepIds> InsertJdsRowsAsync(string connectionString)
    {
        await using var db = new NpgsqlConnection(connectionString);
        var importId = await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO book_import (book_id, source, file_name, status, created_by)
            VALUES (@book, 'markdown', 'book.zip', 'done', @owner) RETURNING id
            """, new { book = SeedIds.Books.ProductEngineering, owner = SeedIds.Users.Jd });
        var jobId = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO export_job (book_id, kind, created_by) VALUES (@book, 'source', @owner) RETURNING id",
            new { book = SeedIds.Books.ProductEngineering, owner = SeedIds.Users.Jd });
        var proofId = await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO export_job (book_id, kind, chapter_id, template_version_id, trim_size, created_by)
            SELECT @book, 'proof', @chapter, v.id, '6x9', @owner FROM layout_template_version v LIMIT 1 RETURNING id
            """, new { book = SeedIds.Books.ProductEngineering, chapter = SeedIds.Chapters.Ch3, owner = SeedIds.Users.Jd });
        var (templateId, templateVersionId) = await TestRows.UserTemplateAsync(db, SeedIds.Users.Jd, "jds-own");
        var integrationId = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO ai_integration (owner_id, kind, label, credential, key_hint, is_default) VALUES (@owner, 'openrouter', 'JD', 'x', 'sk-…1234', true) RETURNING id",
            new { owner = SeedIds.Users.Jd });
        var tokenId = (await TestTokens.CreateAsync(connectionString, SeedIds.Users.Jd, "JD's agent", [ApiTokenScopes.Read])).Id;
        var deviceSessionId = await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO device_session (user_id, name, token_prefix, token_hash, security_stamp, expires_at)
            SELECT id, 'JD''s phone', 'cdx_dev_sweep0000', sha256(convert_to('sweep', 'UTF8')), security_stamp, now() + interval '90 days'
            FROM app_user WHERE id = @owner RETURNING id
            """, new { owner = SeedIds.Users.Jd });
        return new SweepIds(importId, jobId, proofId, tokenId, deviceSessionId, templateId, templateVersionId, integrationId);
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, SweepRoute route, SweepIds ids)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), route.Url(ids));
        if (route.Method != "GET" && route.Method != "DELETE")
        {
            request.Content = FormRoutes.Contains(route.Key) ? TinyForm() : JsonContent.Create(new { });
        }
        return await client.SendAsync(request);
    }

    private static MultipartFormDataContent TinyForm()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "x.png");
        form.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "letter", "letter.pdf");
        return form;
    }
}

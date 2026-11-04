using Codex.AcceptanceTests.Pages.Write;

namespace Codex.AcceptanceTests.Pages.Cite;

// Citing from Write: selecting words in a scene, `/` → Source reference and its source picker, and the marker card's
// Remove.
public sealed class CitePage(IPage page)
{
    // Selects `text` in the scene's prose (it may cross the spans the editor draws), then has ProseMirror read the
    // selection at once. The click before it focuses the editor; see WritePageObject.PlaceCaretAtStartAsync for why
    // the selection is not made with the keyboard.
    private const string SelectScript =
        """
        (element, wanted) => {
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            const nodes = [];
            let text = '';
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                nodes.push({ node, start: text.length });
                text += node.textContent;
            }
            const at = text.indexOf(wanted);
            if (at < 0) throw new Error(`"${wanted}" is not in the scene`);
            const end = at + wanted.length;
            const from = nodes.findLast(n => n.start <= at);
            const to = nodes.find(n => end <= n.start + n.node.textContent.length);
            window.getSelection().setBaseAndExtent(from.node, at - from.start, to.node, end - to.start);
            document.dispatchEvent(new Event('selectionchange'));
        }
        """;

    private readonly WritePageObject _write = new(page);

    public ILocator SourceReference => page.GetByTestId(CiteTestIds.SlashSourceReference);

    public ILocator Picker => page.GetByTestId(CiteTestIds.Picker);

    public ILocator Search => page.GetByTestId(CiteTestIds.Search);

    public ILocator NewSource => page.GetByTestId(CiteTestIds.NewSource);

    public ILocator Option(Guid sourceId) => page.GetByTestId(CiteTestIds.Option(sourceId));

    // The marker card's Remove.
    public ILocator Remove => page.GetByTestId(CiteTestIds.Remove);

    public async Task SelectAsync(Guid sceneId, string text)
    {
        await _write.ProseEditor(sceneId).ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = 8 } });
        await _write.ProseEditor(sceneId).EvaluateAsync(SelectScript, text);
    }

    // Selects `passage`, types `/` over it, and picks `sourceId` from Source reference's picker.
    public async Task CiteAsync(Guid sceneId, string passage, Guid sourceId)
    {
        await SelectAsync(sceneId, passage);
        await page.Keyboard.TypeAsync("/source");
        await SourceReference.ClickAsync();
        await Option(sourceId).ClickAsync();
    }
}

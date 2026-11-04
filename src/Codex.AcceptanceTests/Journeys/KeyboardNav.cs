namespace Codex.AcceptanceTests.Journeys;

// Keyboard-only driving for the "_Keyboard" journeys: no click, hover or pointer, only Page.Keyboard. A control that
// Tab cannot reach fails the step, which is the point of the pass.
public static class KeyboardNav
{
    private const int MaxTabs = 250;

    private const string IsActive = "element => element === document.activeElement";

    public static async Task<bool> HasFocusAsync(this ILocator target) => await target.EvaluateAsync<bool>(IsActive);

    // Presses Tab until `target` has focus. Shift+Tab is used when `backwards` is true.
    public static async Task TabToAsync(this IPage page, ILocator target, bool backwards = false)
    {
        await target.WaitForAsync();
        for (var presses = 0; presses <= MaxTabs; presses++)
        {
            if (await target.HasFocusAsync())
            {
                return;
            }

            await page.Keyboard.PressAsync(backwards ? "Shift+Tab" : "Tab");
        }

        throw new PlaywrightException($"{target} is not reachable by Tab (pressed {MaxTabs} times).");
    }

    // Tabs to the control, then presses `key` (Enter or Space) on it.
    public static async Task ActivateAsync(this IPage page, ILocator target, string key = "Enter")
    {
        await page.TabToAsync(target);
        await page.Keyboard.PressAsync(key);
    }

    // Presses ArrowDown until `target` has focus (roving menus and lists).
    public static async Task ArrowToAsync(this IPage page, ILocator target)
    {
        await target.WaitForAsync();
        for (var presses = 0; presses <= 30; presses++)
        {
            if (await target.HasFocusAsync())
            {
                return;
            }

            await page.Keyboard.PressAsync("ArrowDown");
        }

        throw new PlaywrightException($"{target} is not reachable by ArrowDown.");
    }
}

namespace Codex.Core;

// Sort keys for reordering without renumbering: text compared byte by byte (COLLATE "C"), so the alphabet is in ASCII order.
public static class FractionalKey
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // A key strictly between `before` and `after`. Null `before` means "at the very start", null `after` "at the very end".
    public static string Between(string? before, string? after)
    {
        var low = before ?? "";
        if (after != null && string.CompareOrdinal(low, after) >= 0)
        {
            throw new ArgumentException($"'{before}' must sort before '{after}'.");
        }
        var key = Midpoint(low, after);
        if (string.CompareOrdinal(key, low) <= 0 || (after != null && string.CompareOrdinal(key, after) >= 0))
        {
            throw new ArgumentException($"No key fits between '{before}' and '{after}'.");
        }
        return key;
    }

    // `low` may be empty (negative infinity); `high` null is positive infinity.
    private static string Midpoint(string low, string? high)
    {
        if (high != null)
        {
            var common = 0;
            while (common < high.Length && (common < low.Length ? low[common] : Digits[0]) == high[common])
            {
                common++;
            }
            if (common > 0)
            {
                return high[..common] + Midpoint(low.Length > common ? low[common..] : "", high[common..]);
            }
        }

        var lowDigit = low.Length > 0 ? Digits.IndexOf(low[0], StringComparison.Ordinal) : 0;
        var highDigit = high is { Length: > 0 } ? Digits.IndexOf(high[0], StringComparison.Ordinal) : Digits.Length;
        if (highDigit - lowDigit > 1)
        {
            return Digits[(lowDigit + highDigit + 1) / 2].ToString();
        }
        if (high is { Length: > 1 })
        {
            return high[..1];
        }
        return Digits[lowDigit] + Midpoint(low.Length > 1 ? low[1..] : "", null);
    }
}

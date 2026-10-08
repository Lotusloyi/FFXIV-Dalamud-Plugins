using System.Text.RegularExpressions;

namespace FriendCompass;

internal static class InstanceMenu
{
    public static bool IsTravelEntry(string text) =>
        text.Contains("切换副本区", StringComparison.Ordinal) ||
        text.Contains("切換副本區", StringComparison.Ordinal) ||
        text.Contains("切换分流", StringComparison.Ordinal) ||
        text.Contains("Travel to Instanced Area", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("インスタンスエリアへ移動", StringComparison.Ordinal) ||
        text.Contains("Instanz wechseln", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Changer d'instance", StringComparison.OrdinalIgnoreCase);

    public static int Number(string text)
    {
        foreach (var character in text)
            if (character is >= '\uE0B1' and <= '\uE0B9')
                return character - '\uE0B0';

        var match = Regex.Match(text.Trim(), @"^(?:分流|副本区|副本區|Instance)\s*([1-9])(?:\D|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value[0] - '0' : 0;
    }

    public static int Next(int current, IReadOnlyCollection<int> available, IReadOnlySet<int> visited)
    {
        // Before the menu has been observed, one attempt discovers the actual choices.
        var choices = available.Count == 0 ? Enumerable.Range(1, 9) : available.Order();
        return choices.Where(number => number != current && !visited.Contains(number))
            .OrderBy(number => number > current ? number - current : number + 9 - current)
            .FirstOrDefault();
    }
}

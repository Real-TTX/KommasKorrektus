using System.Security.Cryptography;
using System.Text;

namespace KommasKorrektus.Core.Documents.Layout;

/// <summary>
/// The stylesheet of one rendering. The markup carries no style attributes (the site's content security policy forbids them): every distinct format becomes a
/// class whose name is a hash of its declarations, so the same format is the same class in every rendering and the stylesheet only holds what the page uses.
/// </summary>
internal sealed class CssRegistry
{
    private readonly SortedDictionary<string, string> _rules = new(StringComparer.Ordinal);

    /// <summary>The class for these declarations (empty when there are none). <paramref name="extra"/> holds further rules; <c>{c}</c> in it stands for the class.</summary>
    public string Add(string prefix, string declarations, string extra = "")
    {
        if (declarations.Length == 0 && extra.Length == 0)
        {
            return string.Empty;
        }

        var name = prefix + "-" + Hash(declarations + "|" + extra);
        if (!_rules.ContainsKey(name))
        {
            var rule = declarations.Length == 0 ? string.Empty : $".{name}{{{declarations}}}";
            _rules[name] = rule + extra.Replace("{c}", "." + name, StringComparison.Ordinal);
        }

        return name;
    }

    public string ToCss()
    {
        var sb = new StringBuilder();
        foreach (var rule in _rules.Values)
        {
            sb.Append(rule).Append('\n');
        }

        return sb.ToString();
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 5).ToLowerInvariant();
}

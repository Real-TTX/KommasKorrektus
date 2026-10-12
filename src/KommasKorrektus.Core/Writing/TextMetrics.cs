using System.Text.RegularExpressions;

namespace KommasKorrektus.Core.Writing;

/// <summary>
/// How a manuscript is measured. A word is a run of characters between blanks that has a letter or a digit in it (a lone dash is none): that is
/// how Word counts, and the number editors quote. A standard page (Normseite) is a number of characters including spaces, 1800 by default.
/// </summary>
public static partial class TextMetrics
{
    /// <summary>A line break inside a paragraph (Word's soft return). The paragraphs of a document are separated by a line feed.</summary>
    public const char LineBreak = (char)0x2028;

    public const int DefaultNormPageCharacters = 1800;

    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex LetterOrDigit();

    public static string[] Paragraphs(string? text)
        => string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public static string JoinParagraphs(IEnumerable<string> paragraphs) => string.Join('\n', paragraphs);

    public static int CountWords(string? paragraph)
    {
        if (string.IsNullOrEmpty(paragraph))
        {
            return 0;
        }

        var count = 0;
        var inWord = false;
        var hasLetter = false;
        foreach (var c in paragraph)
        {
            if (char.IsWhiteSpace(c) || c == LineBreak)
            {
                if (inWord && hasLetter)
                {
                    count++;
                }

                inWord = false;
                hasLetter = false;
            }
            else
            {
                inWord = true;
                if (char.IsLetterOrDigit(c))
                {
                    hasLetter = true;
                }
            }
        }

        if (inWord && hasLetter)
        {
            count++;
        }

        return count;
    }

    public static int CountWords(IEnumerable<string> paragraphs) => paragraphs.Sum(CountWords);

    /// <summary>Characters including spaces, without the paragraph marks.</summary>
    public static int CountCharacters(IEnumerable<string> paragraphs) => paragraphs.Sum(p => p.Length);

    public static decimal NormPages(int characters, int charactersPerPage = DefaultNormPageCharacters)
        => charactersPerPage <= 0 ? 0 : Math.Round((decimal)characters / charactersPerPage, 2);

    public static bool HasWordCharacter(string token) => LetterOrDigit().IsMatch(token);
}

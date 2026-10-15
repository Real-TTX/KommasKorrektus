using System.Globalization;
using System.Text;

namespace KommasKorrektus.Core.Diff;

/// <summary>
/// The comparison of two texts the way Visual Studio Code compares two files (its diff, MIT licence: src/vs/editor/common/diff/defaultLinesDiffComputer), with paragraphs where
/// VS Code has lines. It is the same, step by step, as <c>wwwroot/js/textdiff.js</c>, which makes it in the browser while somebody writes; the two have to give the same
/// answer for any two texts (DiffParityTests), so a change on one side is a change on the other. What it does is described there; in short:
/// 1. the paragraphs are compared as whole strings (a scoring subsequence up to 1700 of them, anchors of paragraphs that occur once above that);
/// 2. every stretch that differs is compared character by character, the paragraph breaks being characters too, and cleaned up the way a person reads it;
/// 3. the result is cut at the paragraph breaks into rows (a paragraph as Word keeps it with tracked changes), and the paragraphs that stand across from each other are found.
/// </summary>
internal static class TextDiff
{
    /// <summary>The paragraph break, as a character of a stretch of paragraphs.</summary>
    internal const char Sep = (char)0x2029;

    private const char Soft = (char)0x2028;
    private const int LineDpLimit = 1700;
    private const int CharDpLimit = 500;
    private const int WorkLimit = 4_000_000;
    private const int MaxShift = 100;

    // ================================================================================================ the answer
    internal sealed class Row
    {
        public int? O { get; set; }

        public int? N { get; set; }

        public string Kind { get; set; } = string.Empty;

        /// <summary>What became of the break at the end of the row: 'E' it stayed, 'D' taken away (joined with the next), 'I' put in (the paragraph was split there).</summary>
        public char Mark { get; set; }

        public int OldFrom { get; set; }

        public int OldTo { get; set; }

        public int NewFrom { get; set; }

        public int NewTo { get; set; }

        public List<(int From, int To)> Del { get; } = [];

        public List<(int From, int To)> Ins { get; } = [];

        public List<DiffSegment> Segments { get; } = [];
    }

    internal readonly record struct Position(int P, int C);

    internal sealed record Change(Position OldFrom, Position OldTo, Position NewFrom, Position NewTo, string OldText, string NewText);

    internal sealed record Result(IReadOnlyList<Row> Rows, IReadOnlyList<Change> Changes, IReadOnlyList<(int Old, int New)> Pairs);

    // ================================================================================================ characters
    /// <summary>Whitespace, as a fixed list (the string functions of the browser and of .NET do not agree on all of it).</summary>
    internal static bool IsWhite(int c)
        => (c >= 0x09 && c <= 0x0d) || c == 0x20 || c == 0xa0 || c == 0x1680 || (c >= 0x2000 && c <= 0x200a) || c == 0x2028 || c == 0x2029
           || c == 0x202f || c == 0x205f || c == 0x3000 || c == 0xfeff;

    private static string Trimmed(string text)
    {
        int a = 0, b = text.Length;
        while (a < b && IsWhite(text[a])) { a++; }
        while (b > a && IsWhite(text[b - 1])) { b--; }
        return a == 0 && b == text.Length ? text : text[a..b];
    }

    private const int Lower = 0;
    private const int Upper = 1;
    private const int Number = 2;
    private const int End = 3;
    private const int Other = 4;
    private const int Separator = 5;
    private const int Space = 6;
    private const int Break = 8;
    private static readonly int[] KindScore = [0, 0, 0, 10, 2, 30, 3, 10, 10];

    private static int KindOf(int c)
    {
        if (c < 0) { return End; }
        if (c == Sep) { return Break; }
        if (c is 0x20 or 0x09 or 0xa0 or 0x202f or Soft) { return Space; }
        if (c is 0x2c or 0x3b) { return Separator; }
        return CharUnicodeInfo.GetUnicodeCategory((char)c) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.TitlecaseLetter => Upper,
            UnicodeCategory.LowercaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => Lower,
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => Number,
            _ => Other,
        };
    }

    private static bool IsWordChar(int c) => KindOf(c) is Lower or Upper or Number;

    private static int FloorLog2(int x) => System.Numerics.BitOperations.Log2((uint)x);

    // ================================================================================================ changes: a range of the old sequence for a range of the new one
    private readonly record struct D(int S1, int E1, int S2, int E2)
    {
        public D Moved(int by) => by == 0 ? this : new D(S1 + by, E1 + by, S2 + by, E2 + by);

        public D Swapped() => new(S2, E2, S1, E1);

        public int Size => (E1 - S1) + (E2 - S2);

        public D Join(D b) => new(Math.Min(S1, b.S1), Math.Max(E1, b.E1), Math.Min(S2, b.S2), Math.Max(E2, b.E2));

        public D? Intersect(D b)
        {
            int s1 = Math.Max(S1, b.S1), e1 = Math.Min(E1, b.E1), s2 = Math.Max(S2, b.S2), e2 = Math.Min(E2, b.E2);
            return s1 <= e1 && s2 <= e2 ? new D(s1, e1, s2, e2) : null;
        }
    }

    private static bool Overlap(int s, int e, int t, int f) => Math.Max(s, t) < Math.Min(e, f);

    /// <summary>The unchanged stretches between changes (VS Code: SequenceDiff.invert).</summary>
    private static List<D> Invert(List<D> diffs, int length1)
    {
        var output = new List<D>(diffs.Count + 1);
        for (var i = 0; i <= diffs.Count; i++)
        {
            D? a = i == 0 ? null : diffs[i - 1];
            D? b = i == diffs.Count ? null : diffs[i];
            var s1 = a?.E1 ?? 0;
            var s2 = a?.E2 ?? 0;
            output.Add(b is { } next ? new D(s1, next.S1, s2, next.S2) : new D(s1, length1, s2, (a is { } last ? last.E2 - last.E1 : 0) + length1));
        }

        return output;
    }

    // ================================================================================================ sequences
    private abstract class Sequence
    {
        public abstract int Length { get; }

        public abstract int Get(int i);

        public abstract bool Strongly(int i, int j);

        public abstract int Boundary(int i);

        public int[] Elements()
        {
            var output = new int[Length];
            for (var i = 0; i < output.Length; i++) { output[i] = Get(i); }
            return output;
        }
    }

    /// <summary>The paragraphs as numbers (the same text without the blanks at its ends, the same number).</summary>
    private sealed class ParagraphSequence(IReadOnlyList<string> lines, int[] hashes) : Sequence
    {
        public override int Length => lines.Count;

        public override int Get(int i) => hashes[i];

        public override bool Strongly(int i, int j) => string.Equals(lines[i], lines[j], StringComparison.Ordinal);

        public override int Boundary(int i) => 1000 - (Indent(i == 0 ? string.Empty : lines[i - 1]) + Indent(i == lines.Count ? string.Empty : lines[i]));

        /// <summary>How many characters of the paragraphs from..to are not whitespace, counted up to one more than limit.</summary>
        public int Visible(int from, int to, int limit)
        {
            var count = 0;
            for (var p = from; p < to && count <= limit; p++)
            {
                var t = lines[p];
                for (var i = 0; i < t.Length && count <= limit; i++)
                {
                    if (!IsWhite(t[i])) { count++; }
                }
            }

            return count;
        }

        private static int Indent(string text)
        {
            var i = 0;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) { i++; }
            return i;
        }
    }

    /// <summary>The characters of a stretch of paragraphs, every paragraph followed by its break.</summary>
    private sealed class CharSequence : Sequence
    {
        private readonly List<int> _starts = [0];

        public CharSequence(char[] codes)
        {
            Codes = codes;
            for (var i = 0; i < codes.Length; i++)
            {
                if (codes[i] == Sep) { _starts.Add(i + 1); }
            }
        }

        public char[] Codes { get; }

        public override int Length => Codes.Length;

        public override int Get(int i) => Codes[i];

        public override bool Strongly(int i, int j) => Codes[i] == Codes[j];

        public override int Boundary(int i)
        {
            var prev = KindOf(i > 0 ? Codes[i - 1] : -1);
            var next = KindOf(i < Codes.Length ? Codes[i] : -1);
            if (prev == Break) { return 150; }
            var score = 0;
            if (prev != next)
            {
                score += 10;
                if (prev == Lower && next == Upper) { score += 1; }
            }

            return score + KindScore[prev] + KindScore[next];
        }

        public (int Start, int End)? Word(int i)
        {
            if (i < 0 || i >= Codes.Length || !IsWordChar(Codes[i])) { return null; }
            var s = i;
            while (s > 0 && IsWordChar(Codes[s - 1])) { s--; }
            var e = i;
            while (e < Codes.Length && IsWordChar(Codes[e])) { e++; }
            return (s, e);
        }

        public int LineOf(int offset)
        {
            int lo = 0, hi = _starts.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) >> 1;
                if (_starts[mid] <= offset) { lo = mid; } else { hi = mid - 1; }
            }

            return lo;
        }

        public int LinesIn(int from, int to) => LineOf(to) - LineOf(from);

        public (int Start, int End) FullLines(int from, int to)
        {
            var s = _starts[LineOf(from)];
            var e = Codes.Length;
            for (var k = LineOf(to); k < _starts.Count; k++)
            {
                if (_starts[k] >= to)
                {
                    e = _starts[k];
                    break;
                }
            }

            return (s, e);
        }

        public string Text(int from, int to) => new(Codes, from, to - from);
    }

    private static int[] Slice(int[] items, int from, int to) => items[from..to];

    // ================================================================================================ the algorithms
    /// <summary>The longest common subsequence with a score for every pair of equal elements (VS Code: DynamicProgrammingDiffing).</summary>
    private static List<D> Lcs(int[] a, int[] b, Func<int, int, int>? score, int bonus)
    {
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0) { return n == m ? [] : [new D(0, n, 0, m)]; }
        var best = new double[n * m];
        var dir = new byte[n * m];
        var run = new int[n * m];
        for (var s1 = 0; s1 < n; s1++)
        {
            var e1 = a[s1];
            for (var s2 = 0; s2 < m; s2++)
            {
                var at = s1 + (s2 * n);
                var horizontal = s1 == 0 ? 0 : best[at - 1];
                var vertical = s2 == 0 ? 0 : best[at - n];
                double extended;
                if (e1 == b[s2])
                {
                    extended = s1 == 0 || s2 == 0 ? 0 : best[at - 1 - n];
                    if (s1 > 0 && s2 > 0 && dir[at - 1 - n] == 3) { extended += (double)run[at - 1 - n] * bonus; }
                    extended += score?.Invoke(s1, s2) ?? 1;
                }
                else
                {
                    extended = -1;
                }

                var value = Math.Max(horizontal, Math.Max(vertical, extended));
                if (value == extended)
                {
                    run[at] = (s1 > 0 && s2 > 0 ? run[at - 1 - n] : 0) + 1;
                    dir[at] = 3;
                }
                else if (value == horizontal)
                {
                    run[at] = 0;
                    dir[at] = 1;
                }
                else if (value == vertical)
                {
                    run[at] = 0;
                    dir[at] = 2;
                }

                best[at] = value;
            }
        }

        var output = new List<D>();
        int last1 = n, last2 = m;
        void Report(int x, int y)
        {
            if (x + 1 != last1 || y + 1 != last2) { output.Add(new D(x + 1, last1, y + 1, last2)); }
            last1 = x;
            last2 = y;
        }

        int i = n - 1, j = m - 1;
        while (i >= 0 && j >= 0)
        {
            var d = dir[i + (j * n)];
            if (d == 3)
            {
                Report(i, j);
                i--;
                j--;
            }
            else if (d == 1)
            {
                i--;
            }
            else
            {
                j--;
            }
        }

        Report(-1, -1);
        output.Reverse();
        return output;
    }

    /// <summary>Myers' O(ND) algorithm, the way VS Code walks it (MyersDiffAlgorithm), or null when it would take more than the limit of work.</summary>
    private static List<D>? Myers(int[] a, int[] b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0) { return n == m ? [] : [new D(0, n, 0, m)]; }
        long work = 0;
        int Snake(int x, int y)
        {
            while (x < n && y < m && a[x] == b[y])
            {
                x++;
                y++;
            }

            work += 1;
            return x;
        }

        var off = m + 2;
        var v = new int[n + m + 5];
        var paths = new int[n + m + 5];
        Array.Fill(paths, -1);
        var prev = new List<int>();
        var sx = new List<int>();
        var sy = new List<int>();
        var sl = new List<int>();
        int Add(int before, int x, int y, int length)
        {
            prev.Add(before);
            sx.Add(x);
            sy.Add(y);
            sl.Add(length);
            return prev.Count - 1;
        }

        v[off] = Snake(0, 0);
        work += v[off];
        paths[off] = v[off] == 0 ? -1 : Add(-1, 0, 0, v[off]);
        var k = 0;
        var d = 0;
        var done = false;
        while (!done)
        {
            d++;
            if (work > WorkLimit) { return null; }
            var lower = -Math.Min(d, m + (d % 2));
            var upper = Math.Min(d, n + (d % 2));
            for (k = lower; k <= upper; k += 2)
            {
                var top = k == upper ? -1 : v[off + k + 1];
                var left = k == lower ? -1 : v[off + k - 1] + 1;
                var x = Math.Min(Math.Max(top, left), n);
                var y = x - k;
                if (x > n || y > m) { continue; }
                var reached = Snake(x, y);
                work += reached - x;
                v[off + k] = reached;
                var before = x == top ? paths[off + k + 1] : paths[off + k - 1];
                paths[off + k] = reached != x ? Add(before, x, y, reached - x) : before;
                if (v[off + k] == n && v[off + k] - k == m)
                {
                    done = true;
                    break;
                }
            }
        }

        var path = paths[off + k];
        var output = new List<D>();
        int last1 = n, last2 = m;
        while (true)
        {
            var endX = path >= 0 ? sx[path] + sl[path] : 0;
            var endY = path >= 0 ? sy[path] + sl[path] : 0;
            if (endX != last1 || endY != last2) { output.Add(new D(endX, last1, endY, last2)); }
            if (path < 0) { break; }
            last1 = sx[path];
            last2 = sy[path];
            path = prev[path];
        }

        output.Reverse();
        return output;
    }

    /// <summary>The longest chain of pairs (sorted by the first) in which the second grows too (patience sorting).</summary>
    private static List<(int I, int J)> Increasing(List<(int I, int J)> pairs)
    {
        var tails = new List<int>();
        var back = new int[pairs.Count];
        for (var k = 0; k < pairs.Count; k++)
        {
            var j = pairs[k].J;
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (pairs[tails[mid]].J < j) { lo = mid + 1; } else { hi = mid; }
            }

            back[k] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) { tails.Add(k); } else { tails[lo] = k; }
        }

        var output = new List<(int I, int J)>();
        for (var at = tails.Count > 0 ? tails[^1] : -1; at >= 0; at = back[at]) { output.Add(pairs[at]); }
        output.Reverse();
        return output;
    }

    /// <summary>When Myers gives up: the elements that occur exactly once in each are the anchors, and between two anchors Myers runs again.</summary>
    private static List<D> Anchored(int[] a, int[] b)
    {
        int n = a.Length, m = b.Length;
        var count1 = new Dictionary<int, int>();
        var count2 = new Dictionary<int, int>();
        var at2 = new Dictionary<int, int>();
        for (var i = 0; i < n; i++) { count1[a[i]] = count1.GetValueOrDefault(a[i]) + 1; }
        for (var i = 0; i < m; i++)
        {
            count2[b[i]] = count2.GetValueOrDefault(b[i]) + 1;
            at2[b[i]] = i;
        }

        var candidates = new List<(int I, int J)>();
        for (var i = 0; i < n; i++)
        {
            if (count1[a[i]] == 1 && count2.GetValueOrDefault(a[i]) == 1) { candidates.Add((i, at2[a[i]])); }
        }

        var output = new List<D>();
        void Gap(int x1, int x2, int y1, int y2)
        {
            while (x1 < x2 && y1 < y2 && a[x1] == b[y1])
            {
                x1++;
                y1++;
            }

            while (x2 > x1 && y2 > y1 && a[x2 - 1] == b[y2 - 1])
            {
                x2--;
                y2--;
            }

            if (x1 == x2 && y1 == y2) { return; }
            var there = new HashSet<int>();
            for (var k = y1; k < y2; k++) { there.Add(b[k]); }
            var common = false;
            for (var k = x1; k < x2 && !common; k++) { common = there.Contains(a[k]); }
            var inner = (common ? Myers(Slice(a, x1, x2), Slice(b, y1, y2)) : null) ?? [new D(0, x2 - x1, 0, y2 - y1)];
            foreach (var d in inner) { output.Add(new D(d.S1 + x1, d.E1 + x1, d.S2 + y1, d.E2 + y1)); }
        }

        int from1 = 0, from2 = 0;
        foreach (var (i, j) in Increasing(candidates))
        {
            Gap(from1, i, from2, j);
            from1 = i + 1;
            from2 = j + 1;
        }

        Gap(from1, n, from2, m);
        return output;
    }

    // ================================================================================================ cleaning up (VS Code: heuristicSequenceOptimizations)
    private static List<D> Optimize(Sequence seq1, Sequence seq2, List<D> diffs)
    {
        diffs = JoinByShifting(seq1, seq2, diffs);
        diffs = JoinByShifting(seq1, seq2, diffs);
        return ShiftToBoundaries(seq1, seq2, diffs);
    }

    private static List<D> JoinByShifting(Sequence seq1, Sequence seq2, List<D> diffs)
    {
        if (diffs.Count == 0) { return diffs; }
        var result = new List<D> { diffs[0] };
        for (var i = 1; i < diffs.Count; i++)
        {
            var prev = result[^1];
            var cur = diffs[i];
            if (cur.S1 == cur.E1 || cur.S2 == cur.E2)
            {
                var length = cur.S1 - prev.E1;
                int d;
                for (d = 1; d <= length; d++)
                {
                    if (seq1.Get(cur.S1 - d) != seq1.Get(cur.E1 - d) || seq2.Get(cur.S2 - d) != seq2.Get(cur.E2 - d)) { break; }
                }

                d--;
                if (d == length)
                {
                    result[^1] = new D(prev.S1, cur.E1 - length, prev.S2, cur.E2 - length);
                    continue;
                }

                cur = cur.Moved(-d);
            }

            result.Add(cur);
        }

        var result2 = new List<D>();
        for (var i = 0; i < result.Count - 1; i++)
        {
            var next = result[i + 1];
            var c = result[i];
            if (c.S1 == c.E1 || c.S2 == c.E2)
            {
                var length = next.S1 - c.E1;
                int k;
                for (k = 0; k < length; k++)
                {
                    if (!seq1.Strongly(c.S1 + k, c.E1 + k) || !seq2.Strongly(c.S2 + k, c.E2 + k)) { break; }
                }

                if (k == length)
                {
                    result[i + 1] = new D(c.S1 + length, next.E1, c.S2 + length, next.E2);
                    continue;
                }

                if (k > 0) { c = c.Moved(k); }
            }

            result2.Add(c);
        }

        if (result.Count > 0) { result2.Add(result[^1]); }
        return result2;
    }

    private static List<D> ShiftToBoundaries(Sequence seq1, Sequence seq2, List<D> diffs)
    {
        for (var i = 0; i < diffs.Count; i++)
        {
            D? prev = i > 0 ? diffs[i - 1] : null;
            var diff = diffs[i];
            D? next = i + 1 < diffs.Count ? diffs[i + 1] : null;
            var from1 = prev is { } p1 ? p1.E1 + 1 : 0;
            var to1 = next is { } n1 ? n1.S1 - 1 : seq1.Length;
            var from2 = prev is { } p2 ? p2.E2 + 1 : 0;
            var to2 = next is { } n2 ? n2.S2 - 1 : seq2.Length;
            if (diff.S1 == diff.E1)
            {
                diffs[i] = ToBetterPosition(diff, seq1, seq2, from1, to1, from2, to2);
            }
            else if (diff.S2 == diff.E2)
            {
                diffs[i] = ToBetterPosition(diff.Swapped(), seq2, seq1, from2, to2, from1, to1).Swapped();
            }
        }

        return diffs;
    }

    private static D ToBetterPosition(D diff, Sequence seq1, Sequence seq2, int from1, int to1, int from2, int to2)
    {
        var before = 1;
        while (diff.S1 - before >= from1 && diff.S2 - before >= from2 && seq2.Strongly(diff.S2 - before, diff.E2 - before) && before < MaxShift) { before++; }
        before--;
        var after = 0;
        while (diff.S1 + after < to1 && diff.E2 + after < to2 && seq2.Strongly(diff.S2 + after, diff.E2 + after) && after < MaxShift) { after++; }
        if (before == 0 && after == 0) { return diff; }
        var bestDelta = 0;
        var bestScore = -1;
        for (var delta = -before; delta <= after; delta++)
        {
            var score = seq1.Boundary(diff.S1 + delta) + seq2.Boundary(diff.S2 + delta) + seq2.Boundary(diff.E2 + delta);
            if (score > bestScore)
            {
                bestScore = score;
                bestDelta = delta;
            }
        }

        return diff.Moved(bestDelta);
    }

    /// <summary>Two changes with no more than two characters between them are one (but not across a paragraph break: those are changes of two paragraphs).</summary>
    private static List<D> RemoveShortMatches(CharSequence seq1, List<D> diffs)
    {
        var result = new List<D>();
        foreach (var s in diffs)
        {
            if (result.Count == 0)
            {
                result.Add(s);
                continue;
            }

            var last = result[^1];
            if ((s.S1 - last.E1 <= 2 || s.S2 - last.E2 <= 2) && seq1.LinesIn(last.E1, s.S1) == 0) { result[^1] = last.Join(s); } else { result.Add(s); }
        }

        return result;
    }

    private static List<D> ExtendToWords(CharSequence seq1, CharSequence seq2, List<D> diffs)
    {
        var equal = Invert(diffs, seq1.Length);
        var next = 0;
        var additional = new List<D>();
        int last1 = 0, last2 = 0;

        void ScanWord(int p1, int p2, D mapping)
        {
            if (p1 < last1 || p2 < last2) { return; }
            if (seq1.Word(p1) is not { } w1 || seq2.Word(p2) is not { } w2) { return; }
            var w = new D(w1.Start, w1.End, w2.Start, w2.End);
            var part = w.Intersect(mapping)!.Value;
            var equal1 = part.E1 - part.S1;
            var equal2 = part.E2 - part.S2;
            while (next < equal.Count)
            {
                var following = equal[next];
                if (!Overlap(following.S1, following.E1, w.S1, w.E1) && !Overlap(following.S2, following.E2, w.S2, w.E2)) { break; }
                if (seq1.Word(following.S1) is not { } v1 || seq2.Word(following.S2) is not { } v2) { break; }
                var v = new D(v1.Start, v1.End, v2.Start, v2.End);
                var inner = v.Intersect(following)!.Value;
                equal1 += inner.E1 - inner.S1;
                equal2 += inner.E2 - inner.S2;
                w = w.Join(v);
                if (w.E1 >= following.E1) { next++; } else { break; }
            }

            if (3 * (equal1 + equal2) < 2 * w.Size && seq1.LinesIn(w.S1, w.E1) == 0 && seq2.LinesIn(w.S2, w.E2) == 0) { additional.Add(w); }
            last1 = w.E1;
            last2 = w.E2;
        }

        while (next < equal.Count)
        {
            var stretch = equal[next];
            next++;
            if (stretch.S1 == stretch.E1) { continue; }
            ScanWord(stretch.S1, stretch.S2, stretch);
            ScanWord(stretch.E1 - 1, stretch.E2 - 1, stretch);
        }

        return Merge(diffs, additional);
    }

    private static List<D> Merge(List<D> a, List<D> b)
    {
        var result = new List<D>();
        int i = 0, j = 0;
        while (i < a.Count || j < b.Count)
        {
            var take = i < a.Count && (j >= b.Count || a[i].S1 < b[j].S1) ? a[i++] : b[j++];
            if (result.Count > 0 && result[^1].E1 >= take.S1) { result[^1] = result[^1].Join(take); } else { result.Add(take); }
        }

        return result;
    }

    private static List<D> JoinAcrossShortParagraphs(ParagraphSequence lines, List<D> diffs)
    {
        if (diffs.Count == 0) { return diffs; }
        var counter = 0;
        bool repeat;
        do
        {
            repeat = false;
            var result = new List<D> { diffs[0] };
            for (var i = 1; i < diffs.Count; i++)
            {
                var cur = diffs[i];
                var last = result[^1];
                if (lines.Visible(last.E1, cur.S1, 4) <= 4 && (last.Size > 5 || cur.Size > 5))
                {
                    repeat = true;
                    result[^1] = last.Join(cur);
                }
                else
                {
                    result.Add(cur);
                }
            }

            diffs = result;
        }
        while (counter++ < 10 && repeat);
        return diffs;
    }

    private const int Cap = (2 * 40) + 50;
    private static readonly double JoinLimit = Pow15(Pow15(Cap)) * 1.3;

    private static double Pow15(double v) => v * Math.Sqrt(v);

    private static List<D> RemoveShortTextBetweenLongDiffs(CharSequence seq1, CharSequence seq2, List<D> diffs)
    {
        if (diffs.Count == 0) { return diffs; }
        var counter = 0;
        bool repeat;
        static double Weight(int lines, int length) => Pow15(Math.Min((lines * 40) + length, Cap));
        do
        {
            repeat = false;
            var result = new List<D> { diffs[0] };
            for (var i = 1; i < diffs.Count; i++)
            {
                var cur = diffs[i];
                var last = result[^1];
                var join = false;
                var lines = seq1.LinesIn(last.E1, cur.S1);
                // (VS Code joins across one line break, the break being blanks when trimmed; here two paragraphs are two places)
                if (lines == 0 && cur.S1 - last.E1 <= 500)
                {
                    var unchanged = Trimmed(seq1.Text(last.E1, cur.S1));
                    if (unchanged.Length <= 20 && !unchanged.Contains(Sep))
                    {
                        var before = Pow15(Weight(seq1.LinesIn(last.S1, last.E1), last.E1 - last.S1) + Weight(seq2.LinesIn(last.S2, last.E2), last.E2 - last.S2));
                        var after = Pow15(Weight(seq1.LinesIn(cur.S1, cur.E1), cur.E1 - cur.S1) + Weight(seq2.LinesIn(cur.S2, cur.E2), cur.E2 - cur.S2));
                        join = before + after > JoinLimit;
                    }
                }

                if (join)
                {
                    repeat = true;
                    result[^1] = last.Join(cur);
                }
                else
                {
                    result.Add(cur);
                }
            }

            diffs = result;
        }
        while (counter++ < 10 && repeat);

        var output = new List<D>();
        const int Max = int.MaxValue;
        for (var k = 0; k < diffs.Count; k++)
        {
            D? prev = k > 0 ? diffs[k - 1] : null;
            var current = diffs[k];
            D? following = k + 1 < diffs.Count ? diffs[k + 1] : null;
            var changed = current;
            bool Mark(string text) => text.Length > 0 && Trimmed(text).Length <= 3 && current.Size > 100;
            var (fullStart, fullEnd) = seq1.FullLines(current.S1, current.E1);
            // the rest of the paragraph, without its break (VS Code counts the line break in; here it would make a change that puts in text at the end of a paragraph take its break)
            if (fullEnd > current.E1 && seq1.Codes[fullEnd - 1] == Sep) { fullEnd--; }
            var prefix = fullStart < current.S1 ? current.S1 - fullStart : 0;
            if (prefix > 0 && Mark(seq1.Text(fullStart, current.S1))) { changed = new D(changed.S1 - prefix, changed.E1, changed.S2 - prefix, changed.E2); }
            var suffix = fullEnd > current.E1 ? fullEnd - current.E1 : 0;
            if (suffix > 0 && Mark(seq1.Text(current.E1, fullEnd))) { changed = new D(changed.S1, changed.E1 + suffix, changed.S2, changed.E2 + suffix); }
            var fitting = changed.Intersect(new D(prev?.E1 ?? 0, following?.S1 ?? Max, prev?.E2 ?? 0, following?.S2 ?? Max))!.Value;
            if (output.Count > 0 && fitting.S1 == output[^1].E1 && fitting.S2 == output[^1].E2) { output[^1] = output[^1].Join(fitting); } else { output.Add(fitting); }
        }

        return output;
    }

    // ================================================================================================ a stretch of paragraphs, character by character
    /// <summary>Where the words, the runs of blanks and the other characters (one each) of a stretch begin, and the end.</summary>
    private static List<int> TokenStarts(char[] codes)
    {
        var starts = new List<int>();
        var i = 0;
        while (i < codes.Length)
        {
            starts.Add(i);
            var k = KindOf(codes[i]);
            i++;
            if (k is Lower or Upper or Number)
            {
                while (i < codes.Length && IsWordChar(codes[i])) { i++; }
            }
            else if (k == Space)
            {
                while (i < codes.Length && KindOf(codes[i]) == Space) { i++; }
            }
        }

        starts.Add(codes.Length);
        return starts;
    }

    /// <summary>When the characters are too many for Myers: the words first, then the characters inside every change of words.</summary>
    private static List<D> ByWords(CharSequence seq1, CharSequence seq2)
    {
        var a = TokenStarts(seq1.Codes);
        var b = TokenStarts(seq2.Codes);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] TokensOf(List<int> starts, CharSequence seq)
        {
            var output = new int[starts.Count - 1];
            for (var i = 0; i + 1 < starts.Count; i++)
            {
                var token = seq.Text(starts[i], starts[i + 1]);
                if (!ids.TryGetValue(token, out var id))
                {
                    id = ids.Count;
                    ids[token] = id;
                }

                output[i] = id;
            }

            return output;
        }

        var ta = TokensOf(a, seq1);
        var tb = TokensOf(b, seq2);
        var coarse = Myers(ta, tb) ?? Anchored(ta, tb);
        var chars1 = seq1.Elements();
        var chars2 = seq2.Elements();
        var output = new List<D>();
        foreach (var d in coarse)
        {
            int x1 = a[d.S1], x2 = a[d.E1], y1 = b[d.S2], y2 = b[d.E2];
            var v1 = Slice(chars1, x1, x2);
            var v2 = Slice(chars2, y1, y2);
            var fine = v1.Length + v2.Length < CharDpLimit ? Lcs(v1, v2, null, 1) : (Myers(v1, v2) ?? [new D(0, v1.Length, 0, v2.Length)]);
            foreach (var f in fine) { output.Add(new D(f.S1 + x1, f.E1 + x1, f.S2 + y1, f.E2 + y1)); }
        }

        return output;
    }

    /// <summary>The changes between two stretches of paragraphs (every paragraph followed by its break): VS Code's refineDiff.</summary>
    private static List<D> Refine(char[] codes1, char[] codes2)
    {
        var seq1 = new CharSequence(codes1);
        var seq2 = new CharSequence(codes2);
        var diffs = seq1.Length + seq2.Length < CharDpLimit
            ? Lcs(seq1.Elements(), seq2.Elements(), null, 1)
            : Myers(seq1.Elements(), seq2.Elements()) ?? ByWords(seq1, seq2);
        diffs = Optimize(seq1, seq2, diffs);
        diffs = ExtendToWords(seq1, seq2, diffs);
        diffs = RemoveShortMatches(seq1, diffs);
        return RemoveShortTextBetweenLongDiffs(seq1, seq2, diffs);
    }

    // ================================================================================================ the comparison
    private static char[] CodesOf(IReadOnlyList<string> texts, int from, int to)
    {
        var sb = new StringBuilder();
        for (var i = from; i < to; i++) { sb.Append(texts[i]).Append(Sep); }
        return sb.ToString().ToCharArray();
    }

    private static Position Locate(int[] starts, int offset)
    {
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (starts[mid] <= offset) { lo = mid; } else { hi = mid - 1; }
        }

        return new Position(lo, offset - starts[lo]);
    }

    /// <summary>Two lists of paragraphs compared: the rows, the changes place by place, the paragraphs that stand across from each other (see textdiff.js, compare).</summary>
    public static Result Compare(IReadOnlyList<string> oldInput, IReadOnlyList<string> newInput)
    {
        var oldTexts = oldInput.Select(t => t.Replace('\n', Soft)).ToList();
        var newTexts = newInput.Select(t => t.Replace('\n', Soft)).ToList();
        int n = oldTexts.Count, m = newTexts.Count;

        var hashes = new Dictionary<string, int>(StringComparer.Ordinal);
        int Hash(string text)
        {
            var key = Trimmed(text);
            if (!hashes.TryGetValue(key, out var id))
            {
                id = hashes.Count;
                hashes[key] = id;
            }

            return id;
        }

        var oldHashes = oldTexts.Select(Hash).ToArray();
        var newHashes = newTexts.Select(Hash).ToArray();
        var lines1 = new ParagraphSequence(oldTexts, oldHashes);
        var lines2 = new ParagraphSequence(newTexts, newHashes);

        // 1. the paragraphs
        var lineDiffs = n + m < LineDpLimit
            ? Lcs(oldHashes, newHashes, (i, j) => !string.Equals(oldTexts[i], newTexts[j], StringComparison.Ordinal) ? 99 : newTexts[j].Length == 0 ? 10 : 100 + (69 * FloorLog2(1 + newTexts[j].Length)), 100)
            : Anchored(oldHashes, newHashes);
        lineDiffs = Optimize(lines1, lines2, lineDiffs);
        lineDiffs = JoinAcrossShortParagraphs(lines1, lineDiffs);

        // 2. every stretch that differs, character by character
        var oldStarts = new int[n + 1];
        var newStarts = new int[m + 1];
        for (var i = 0; i < n; i++) { oldStarts[i + 1] = oldStarts[i] + oldTexts[i].Length + 1; }
        for (var i = 0; i < m; i++) { newStarts[i + 1] = newStarts[i] + newTexts[i].Length + 1; }
        var inner = new List<D>();
        void Region(int o1, int o2, int n1, int n2)
        {
            int base1 = oldStarts[o1], base2 = newStarts[n1];
            foreach (var d in Refine(CodesOf(oldTexts, o1, o2), CodesOf(newTexts, n1, n2)))
            {
                inner.Add(new D(base1 + d.S1, base1 + d.E1, base2 + d.S2, base2 + d.E2));
            }
        }

        int last1 = 0, last2 = 0;
        void Blanks(int count)
        {
            for (var k = 0; k < count; k++)
            {
                if (!string.Equals(oldTexts[last1 + k], newTexts[last2 + k], StringComparison.Ordinal)) { Region(last1 + k, last1 + k + 1, last2 + k, last2 + k + 1); }
            }
        }

        foreach (var d in lineDiffs)
        {
            Blanks(d.S1 - last1);
            last1 = d.E1;
            last2 = d.E2;
            Region(d.S1, d.E1, d.S2, d.E2);
        }

        Blanks(n - last1);

        // 3. rows, places, pairs
        inner = KeepBreaks(inner, oldTexts, newTexts, oldStarts, newStarts);
        var rows = BuildRows(inner, oldTexts, newTexts, oldStarts, newStarts);
        var changes = new List<Change>();
        if (inner.Count > 0)
        {
            var oldJoined = string.Concat(oldInput.Select(t => t + Sep));
            var newJoined = string.Concat(newInput.Select(t => t + Sep));
            foreach (var c in inner)
            {
                changes.Add(new Change(Locate(oldStarts, c.S1), Locate(oldStarts, c.E1), Locate(newStarts, c.S2), Locate(newStarts, c.E2),
                    oldJoined[c.S1..c.E1], newJoined[c.S2..c.E2]));
            }
        }

        return new Result(rows, changes, Alignment(changes, oldTexts, newTexts));
    }

    /// <summary>The character of a joined text (every paragraph followed by its break) at g.</summary>
    private static char CharOf(int[] starts, IReadOnlyList<string> texts, int g)
    {
        var p = Locate(starts, g);
        return p.C == texts[p.P].Length ? Sep : texts[p.P][p.C];
    }

    /// <summary>
    /// Text put in that holds a paragraph break, followed by the rest of the old paragraph: the rest and its break may as well be the ones in the text that was put in, where the
    /// new paragraph begins (see keepBreaks in textdiff.js). The same for text taken away.
    /// </summary>
    private static List<D> KeepBreaks(List<D> inner, IReadOnlyList<string> oldTexts, IReadOnlyList<string> newTexts, int[] oldStarts, int[] newStarts)
    {
        var oldTotal = oldStarts[oldTexts.Count];
        var newTotal = newStarts[newTexts.Count];

        static (int End, int Sep, int Length)? Rebreak(int[] aStarts, IReadOnlyList<string> aTexts, int aTotal, int a, int[] bStarts, IReadOnlyList<string> bTexts, int b1, int b2, int limit)
        {
            if (a >= aTotal) { return null; }
            var q = Locate(aStarts, a).P;
            var end = aStarts[q] + aTexts[q].Length;
            if (end + 1 >= limit) { return null; }
            var p = Locate(bStarts, b1).P;
            var sep = bStarts[p] + bTexts[p].Length;
            if (sep >= b2) { return null; }
            var length = end - a;
            if (sep - b1 < length) { return null; }
            for (var k = 0; k < length; k++)
            {
                if (CharOf(aStarts, aTexts, a + k) != CharOf(bStarts, bTexts, sep - length + k)) { return null; }
            }

            return (end, sep, length);
        }

        var output = new List<D>();
        for (var i = 0; i < inner.Count; i++)
        {
            var c = inner[i];
            D? next = i + 1 < inner.Count ? inner[i + 1] : null;
            if (c.S1 == c.E1 && c.E2 > c.S2 && Rebreak(oldStarts, oldTexts, oldTotal, c.S1, newStarts, newTexts, c.S2, c.E2, next?.S1 ?? oldTotal + 1) is { } r)
            {
                if (r.Sep - r.Length > c.S2) { output.Add(new D(c.S1, c.S1, c.S2, r.Sep - r.Length)); }
                output.Add(new D(r.End + 1, r.End + 1, r.Sep + 1, c.E2 + r.Length + 1));
            }
            else if (c.S2 == c.E2 && c.E1 > c.S1 && Rebreak(newStarts, newTexts, newTotal, c.S2, oldStarts, oldTexts, c.S1, c.E1, next?.S2 ?? newTotal + 1) is { } d)
            {
                if (d.Sep - d.Length > c.S1) { output.Add(new D(c.S1, d.Sep - d.Length, c.S2, c.S2)); }
                output.Add(new D(d.Sep + 1, c.E1 + d.Length + 1, d.End + 1, d.End + 1));
            }
            else
            {
                output.Add(c);
            }
        }

        return output;
    }

    private static List<Row> BuildRows(List<D> inner, IReadOnlyList<string> oldTexts, IReadOnlyList<string> newTexts, int[] oldStarts, int[] newStarts)
    {
        var rows = new List<Row>();
        int po = 0, pn = 0, go = 0, gn = 0;
        Row? row = null;

        Row Open() => row ??= new Row { OldFrom = go - oldStarts[po], NewFrom = gn - newStarts[pn] };

        void Segment(EditKind kind, string text)
        {
            var r = Open();
            if (r.Segments.Count > 0 && r.Segments[^1].Kind == kind) { r.Segments[^1] = new DiffSegment(kind, r.Segments[^1].Text + text); } else { r.Segments.Add(new DiffSegment(kind, text)); }
        }

        void Close(char mark, bool old, bool now)
        {
            var r = Open();
            if (old) { r.O = po; }
            if (now) { r.N = pn; }
            r.Mark = mark;
            r.OldTo = go - oldStarts[po];
            r.NewTo = gn - newStarts[pn];
            rows.Add(r);
            row = null;
        }

        void Equal(int count)
        {
            while (count > 0)
            {
                var take = Math.Min(count, oldStarts[po] + oldTexts[po].Length - go);
                if (take > 0)
                {
                    var r = Open();
                    r.O = po;
                    r.N = pn;
                    var from = go - oldStarts[po];
                    Segment(EditKind.Equal, oldTexts[po].Substring(from, take));
                    go += take;
                    gn += take;
                    count -= take;
                }

                if (count > 0)
                {
                    Close('E', true, true);
                    go++;
                    gn++;
                    po++;
                    pn++;
                    count--;
                }
            }
        }

        void Removed(int count)
        {
            while (count > 0)
            {
                var take = Math.Min(count, oldStarts[po] + oldTexts[po].Length - go);
                if (take > 0)
                {
                    var r = Open();
                    r.O = po;
                    var from = go - oldStarts[po];
                    r.Del.Add((from, from + take));
                    Segment(EditKind.Delete, oldTexts[po].Substring(from, take));
                    go += take;
                    count -= take;
                }

                if (count > 0)
                {
                    Close('D', true, false);
                    go++;
                    po++;
                    count--;
                }
            }
        }

        void Added(int count)
        {
            while (count > 0)
            {
                var take = Math.Min(count, newStarts[pn] + newTexts[pn].Length - gn);
                if (take > 0)
                {
                    var r = Open();
                    r.N = pn;
                    var from = gn - newStarts[pn];
                    r.Ins.Add((from, from + take));
                    Segment(EditKind.Insert, newTexts[pn].Substring(from, take));
                    gn += take;
                    count -= take;
                }

                if (count > 0)
                {
                    Close('I', false, true);
                    gn++;
                    pn++;
                    count--;
                }
            }
        }

        foreach (var c in inner)
        {
            Equal(c.S1 - go);
            Removed(c.E1 - c.S1);
            Added(c.E2 - c.S2);
        }

        Equal(oldStarts[oldTexts.Count] - go);

        var perOld = new int[oldTexts.Count];
        var perNew = new int[newTexts.Count];
        foreach (var r in rows)
        {
            if (r.O is { } o) { perOld[o]++; }
            if (r.N is { } n) { perNew[n]++; }
        }

        foreach (var r in rows)
        {
            r.Kind = r.O is null ? "ins"
                : r.N is null ? "del"
                : r.Mark == 'E' && r.Del.Count == 0 && r.Ins.Count == 0 && perOld[r.O.Value] == 1 && perNew[r.N.Value] == 1 ? "same" : "mod";
        }

        return rows;
    }

    private static List<(int Old, int New)> Alignment(List<Change> changes, IReadOnlyList<string> oldTexts, IReadOnlyList<string> newTexts)
    {
        int n = oldTexts.Count, m = newTexts.Count;
        int OldLength(int p) => p < n ? oldTexts[p].Length : 0;
        int NewLength(int p) => p < m ? newTexts[p].Length : 0;

        var groups = new List<(int O1, int O2, int N1, int N2, List<Change> Inner)>();
        (int O1, int O2, int N1, int N2)? previous = null;
        foreach (var c in changes)
        {
            int startDelta = 0, endDelta = 0;
            if (c.NewTo.C == 0 && c.OldTo.C == 0 && c.OldFrom.P <= c.OldTo.P && c.NewFrom.P <= c.NewTo.P) { endDelta = -1; }
            if (c.NewFrom.C >= NewLength(c.NewFrom.P) && c.OldFrom.C >= OldLength(c.OldFrom.P) && c.OldFrom.P <= c.OldTo.P + endDelta && c.NewFrom.P <= c.NewTo.P + endDelta) { startDelta = 1; }
            var g = (O1: c.OldFrom.P + startDelta, O2: c.OldTo.P + 1 + endDelta, N1: c.NewFrom.P + startDelta, N2: c.NewTo.P + 1 + endDelta);
            if (previous is { } p && ((p.O1 <= g.O2 && g.O1 <= p.O2) || (p.N1 <= g.N2 && g.N1 <= p.N2)))
            {
                var last = groups[^1];
                last.Inner.Add(c);
                groups[^1] = (Math.Min(last.O1, g.O1), Math.Max(last.O2, g.O2), Math.Min(last.N1, g.N1), Math.Max(last.N2, g.N2), last.Inner);
            }
            else
            {
                groups.Add((g.O1, g.O2, g.N1, g.N2, [c]));
            }

            previous = g;
        }

        var pairs = new List<(int Old, int New)>();
        int lastOld = 0, lastNew = 0;
        foreach (var g in groups)
        {
            for (var k = 0; lastOld + k < g.O1 && lastNew + k < g.N1; k++) { pairs.Add((lastOld + k, lastNew + k)); }
            var first = true;
            int lo = g.O1, ln = g.N1;
            void Emit(int oldEnd, int newEnd, bool force)
            {
                if (oldEnd < lo || newEnd < ln) { return; }
                if (first) { first = false; } else if (!force && (oldEnd == lo || newEnd == ln)) { return; }
                if (oldEnd == lo && newEnd == ln) { return; }
                if (oldEnd > lo && newEnd > ln) { pairs.Add((lo, ln)); }
                lo = oldEnd;
                ln = newEnd;
            }

            foreach (var c in g.Inner)
            {
                if (c.OldFrom.C > 0 && c.NewFrom.C > 0) { Emit(c.OldFrom.P, c.NewFrom.P, false); }
                if (c.OldTo.C < OldLength(c.OldTo.P)) { Emit(c.OldTo.P, c.NewTo.P, false); }
            }

            Emit(g.O2, g.N2, true);
            lastOld = g.O2;
            lastNew = g.N2;
        }

        for (var k = 0; lastOld + k < n && lastNew + k < m; k++) { pairs.Add((lastOld + k, lastNew + k)); }
        return pairs;
    }
}

namespace KommaKorrektus.Core.Diff;

public enum EditKind
{
    Equal,
    Insert,
    Delete,
}

/// <summary>One step of an edit script: an item that is in both, only in the new sequence (insert), or only in the old one (delete).</summary>
public readonly record struct Edit<T>(EditKind Kind, T Item, int OldIndex, int NewIndex);

/// <summary>
/// The shortest edit script between two sequences (Myers, "An O(ND) Difference Algorithm", 1986). The common start and end are cut off first,
/// which is most of the work for two versions of the same text. When the sequences differ by more than <see cref="MaxDistance"/> steps the part
/// in the middle is reported as one deletion and one insertion instead: that is correct, only coarser, and it bounds the memory.
/// </summary>
public static class Myers
{
    public const int MaxDistance = 3000;

    public static List<Edit<T>> Diff<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T>? comparer = null, int maxDistance = MaxDistance)
    {
        comparer ??= EqualityComparer<T>.Default;
        var result = new List<Edit<T>>(Math.Max(a.Count, b.Count));

        var start = 0;
        while (start < a.Count && start < b.Count && comparer.Equals(a[start], b[start]))
        {
            result.Add(new Edit<T>(EditKind.Equal, a[start], start, start));
            start++;
        }

        var endA = a.Count;
        var endB = b.Count;
        while (endA > start && endB > start && comparer.Equals(a[endA - 1], b[endB - 1]))
        {
            endA--;
            endB--;
        }

        MiddleDiff(a, b, start, endA, start, endB, comparer, maxDistance, result);

        for (var i = endA; i < a.Count; i++)
        {
            result.Add(new Edit<T>(EditKind.Equal, a[i], i, endB + (i - endA)));
        }

        return result;
    }

    private static void MiddleDiff<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, int aStart, int aEnd, int bStart, int bEnd, IEqualityComparer<T> comparer, int maxDistance, List<Edit<T>> output)
    {
        var n = aEnd - aStart;
        var m = bEnd - bStart;
        if (n == 0 && m == 0)
        {
            return;
        }

        if (n == 0)
        {
            for (var j = bStart; j < bEnd; j++) { output.Add(new Edit<T>(EditKind.Insert, b[j], aStart, j)); }
            return;
        }

        if (m == 0)
        {
            for (var i = aStart; i < aEnd; i++) { output.Add(new Edit<T>(EditKind.Delete, a[i], i, bStart)); }
            return;
        }

        var max = Math.Min(n + m, maxDistance);
        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var found = false;

        for (var d = 0; d <= max && !found; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                {
                    x = v[offset + k + 1];
                }
                else
                {
                    x = v[offset + k - 1] + 1;
                }

                var y = x - k;
                while (x < n && y < m && comparer.Equals(a[aStart + x], b[bStart + y]))
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    found = true;
                    Backtrack(a, b, aStart, bStart, n, m, trace, offset, d, output);
                    break;
                }
            }
        }

        if (!found)
        {
            // Too different to be worth the exact answer: replace the middle as a whole.
            for (var i = aStart; i < aEnd; i++) { output.Add(new Edit<T>(EditKind.Delete, a[i], i, bStart)); }
            for (var j = bStart; j < bEnd; j++) { output.Add(new Edit<T>(EditKind.Insert, b[j], aEnd, j)); }
        }
    }

    private static void Backtrack<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, int aStart, int bStart, int n, int m, List<int[]> trace, int offset, int dEnd, List<Edit<T>> output)
    {
        var reversed = new List<Edit<T>>();
        int x = n, y = m;
        for (var d = dEnd; d > 0; d--)
        {
            var v = trace[d];
            var k = x - y;
            int prevK;
            if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
            {
                prevK = k + 1;
            }
            else
            {
                prevK = k - 1;
            }

            var prevX = v[offset + prevK];
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                reversed.Add(new Edit<T>(EditKind.Equal, a[aStart + x], aStart + x, bStart + y));
            }

            if (x == prevX)
            {
                y--;
                reversed.Add(new Edit<T>(EditKind.Insert, b[bStart + y], aStart + x, bStart + y));
            }
            else
            {
                x--;
                reversed.Add(new Edit<T>(EditKind.Delete, a[aStart + x], aStart + x, bStart + y));
            }
        }

        while (x > 0 && y > 0)
        {
            x--;
            y--;
            reversed.Add(new Edit<T>(EditKind.Equal, a[aStart + x], aStart + x, bStart + y));
        }

        for (var i = reversed.Count - 1; i >= 0; i--)
        {
            output.Add(reversed[i]);
        }
    }
}

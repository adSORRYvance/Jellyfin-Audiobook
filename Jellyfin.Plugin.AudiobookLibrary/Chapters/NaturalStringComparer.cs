using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// Sorts file names the way people read them, so "Chapter 2" comes before "Chapter 10".
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static NaturalStringComparer Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var xRun = ReadDigits(x, ref i);
                var yRun = ReadDigits(y, ref j);

                // Compare by length after dropping leading zeros, so numbers of any size work without parsing
                var xNum = xRun.TrimStart('0');
                var yNum = yRun.TrimStart('0');
                var byValue = xNum.Length != yNum.Length
                    ? xNum.Length.CompareTo(yNum.Length)
                    : xNum.SequenceCompareTo(yNum);
                if (byValue != 0)
                {
                    return byValue;
                }

                continue;
            }

            var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        var byRest = (x.Length - i).CompareTo(y.Length - j);

        // Names that only differ in zero padding still need a fixed order
        return byRest != 0 ? byRest : string.CompareOrdinal(x, y);
    }

    private static ReadOnlySpan<char> ReadDigits(string s, ref int index)
    {
        var start = index;
        while (index < s.Length && char.IsAsciiDigit(s[index]))
        {
            index++;
        }

        return s.AsSpan(start, index - start);
    }
}

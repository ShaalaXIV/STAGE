using System;
using System.Collections.Generic;

namespace STAGE.Utils
{
    internal sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer OrdinalIgnoreCase = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0, iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                char cx = x[ix];
                char cy = y[iy];

                if (char.IsDigit(cx) && char.IsDigit(cy))
                {
                    int result = CompareNumberRuns(x, ref ix, y, ref iy);
                    if (result != 0) return result;
                    continue;
                }

                int charResult = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (charResult != 0) return charResult;
                ix++;
                iy++;
            }

            return (x.Length - ix).CompareTo(y.Length - iy);
        }

        private static int CompareNumberRuns(string x, ref int ix, string y, ref int iy)
        {
            int startX = ix;
            int startY = iy;
            while (ix < x.Length && char.IsDigit(x[ix])) ix++;
            while (iy < y.Length && char.IsDigit(y[iy])) iy++;

            int significantX = startX;
            int significantY = startY;
            while (significantX < ix && x[significantX] == '0') significantX++;
            while (significantY < iy && y[significantY] == '0') significantY++;

            int lengthX = ix - significantX;
            int lengthY = iy - significantY;
            if (lengthX != lengthY) return lengthX.CompareTo(lengthY);

            for (int i = 0; i < lengthX; i++)
            {
                int digitResult = x[significantX + i].CompareTo(y[significantY + i]);
                if (digitResult != 0) return digitResult;
            }

            return (ix - startX).CompareTo(iy - startY);
        }
    }
}

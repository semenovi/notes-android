using System.Text;

namespace Notes.Helpers;

// alphabetical string comparer that treats embedded digit runs as numbers,
// so "item2" sorts before "item10".
public class NaturalSortComparer : IComparer<string>
{
  public static readonly NaturalSortComparer Instance = new();

  public int Compare(string? x, string? y)
  {
    x ??= string.Empty;
    y ??= string.Empty;

    int ix = 0, iy = 0;
    while (ix < x.Length && iy < y.Length)
    {
      char cx = x[ix], cy = y[iy];

      if (char.IsDigit(cx) && char.IsDigit(cy))
      {
        int startX = ix;
        while (ix < x.Length && char.IsDigit(x[ix])) ix++;
        int startY = iy;
        while (iy < y.Length && char.IsDigit(y[iy])) iy++;

        var numX = x.Substring(startX, ix - startX).TrimStart('0');
        var numY = y.Substring(startY, iy - startY).TrimStart('0');

        if (numX.Length != numY.Length)
          return numX.Length - numY.Length;

        int numCompare = string.CompareOrdinal(numX, numY);
        if (numCompare != 0)
          return numCompare;
      }
      else
      {
        int charCompare = string.Compare(x[ix].ToString(), y[iy].ToString(), StringComparison.CurrentCultureIgnoreCase);
        if (charCompare != 0)
          return charCompare;

        ix++;
        iy++;
      }
    }

    return (x.Length - ix) - (y.Length - iy);
  }
}

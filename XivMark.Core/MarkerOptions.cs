namespace XivMark.Core;

public static class MarkerOptions
{
    public static int[] DefaultOrder => [0, 1, 2, 3, 4, 14, 15, 16, 5, 6, 7, 8, 9];
    public static bool IsSupported(int slot) => slot is >= 0 and <= 9 or >= 14 and <= 16;
    public static int[] NormalizeOrder(IEnumerable<int>? slots) => (slots ?? []).Concat(DefaultOrder)
        .Where(IsSupported).Distinct().ToArray();
}

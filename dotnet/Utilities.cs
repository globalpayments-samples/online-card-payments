using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GpPaymentsTests")]

namespace GpPayments;

internal static class GpUtilities
{
    internal static string ToMinorUnits(string amount) =>
        ((int)Math.Round(double.Parse(amount) * 100)).ToString();

    internal static string TwoDigitYear(string year) =>
        year.Length > 2 ? year[^2..] : year;

    private static readonly Dictionary<int, string> ColorDepthMap = new()
    {
        {  1, "ONE_BIT"         }, {  2, "TWO_BITS"        }, {  4, "FOUR_BITS"  },
        {  8, "EIGHT_BITS"      }, { 15, "FIFTEEN_BITS"    }, { 16, "SIXTEEN_BITS" },
        { 24, "TWENTY_FOUR_BITS"}, { 32, "THIRTY_TWO_BITS" }, { 48, "FORTY_EIGHT_BITS" }
    };

    internal static string MapColorDepth(string v) =>
        int.TryParse(v, out var d) && ColorDepthMap.TryGetValue(d, out var e) ? e : "TWENTY_FOUR_BITS";

    internal static string MapBool(string v) =>
        string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
}

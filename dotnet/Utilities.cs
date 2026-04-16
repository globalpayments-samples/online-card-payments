using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GpPaymentsTests")]

namespace GpPayments;

internal static class GpUtilities
{
    internal static string ToMinorUnits(string amount) =>
        ((int)Math.Round(double.Parse(amount) * 100)).ToString();

    internal static string TwoDigitYear(string year) =>
        year.Length > 2 ? year[^2..] : year;
}

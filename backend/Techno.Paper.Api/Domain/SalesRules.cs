using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Techno.Paper.Api.Domain;

public sealed class PolicyConflictException() : Exception("Conflicting ERP grants require administrator review.");

public static class SalesRules
{
    public static string DocumentId(string site, string voucher, string prefix, long number)
    {
        if (number is < 1 or > 99999999 || site.Length is < 1 or > 2 ||
            voucher.Length is < 1 or > 5 || prefix.Length is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(number));
        return "D" + site.PadRight(2) + voucher.PadRight(5) + prefix.PadLeft(5) +
            number.ToString(CultureInfo.InvariantCulture).PadLeft(8);
    }

    public static string? ResolveGrant(IEnumerable<string> grants)
    {
        var matches = grants.Distinct(StringComparer.Ordinal).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new PolicyConflictException()
        };
    }

    public static bool ValidDecimal(string value, int precision, int scale)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number)) return false;
        if (decimal.Round(number, scale) != number) return false;
        var bound = (decimal)Math.Pow(10, precision - scale);
        return number > -bound && number < bound;
    }
}

public static class LegacyCredentials
{
    public static bool Verify(string username, string password, string encoded)
    {
        if (encoded.Length < 2) return false;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var bytes = encoding.GetBytes(encoded);
        var decoded = new byte[bytes.Length - 1];
        try
        {
            for (var i = 1; i < bytes.Length; i++)
            {
                var code = bytes[i] - bytes[0];
                if (code is < 0 or > 255) return false;
                decoded[i - 1] = (byte)code;
            }
            var actual = encoding.GetString(decoded).ToUpperInvariant();
            var expected = username.Trim().ToUpperInvariant() + password.Trim().ToUpperInvariant();
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(actual)),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            CryptographicOperations.ZeroMemory(decoded);
        }
    }
}

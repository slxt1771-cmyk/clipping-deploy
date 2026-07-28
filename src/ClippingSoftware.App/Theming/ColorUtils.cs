using System.Windows.Media;

namespace ClippingSoftware.App.Theming;

/// <summary>
/// Small color-math helpers for the runtime accent-color customization (M15): tint = mix toward white,
/// shade = mix toward black - standard color-theory derivation, used because the user can pick any hex, so
/// the app can't rely on hand-tuned tint/shade values the way the original fixed lavender/red palette did.
/// </summary>
public static class ColorUtils
{
    public static Color Lighten(Color color, double amount) => Blend(color, Colors.White, amount);

    public static Color Darken(Color color, double amount) => Blend(color, Colors.Black, amount);

    private static Color Blend(Color color, Color target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(color.A, Lerp(color.R, target.R), Lerp(color.G, target.G), Lerp(color.B, target.B));
    }

    /// <summary>Accepts "#RGB", "#RRGGBB", "#AARRGGBB", or the same without the leading '#'.</summary>
    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var candidate = hex.Trim();
        if (!candidate.StartsWith('#'))
        {
            candidate = "#" + candidate;
        }

        try
        {
            if (ColorConverter.ConvertFromString(candidate) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Invalid hex string - TryParseHex reports failure via the bool return, not an exception.
        }

        return false;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

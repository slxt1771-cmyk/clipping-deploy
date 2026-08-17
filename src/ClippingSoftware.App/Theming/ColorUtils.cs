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

    /// <summary>Black or white, whichever reads legibly on <paramref name="background"/> - for text drawn
    /// directly on a user-picked color (tag chips) rather than one of the theme's own fixed surfaces, where
    /// a single hardcoded text color can land on a background dark enough to make it unreadable. Uses the
    /// standard perceptual-luminance weighting (ITU-R BT.601) rather than a flat RGB average, since the eye
    /// is far more sensitive to green than red/blue - a flat average misjudges saturated colors.</summary>
    public static Color GetReadableForeground(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6 ? Colors.Black : Colors.White;
    }
}

using System.Windows;
using System.Windows.Media;

namespace ClippingSoftware.App.Theming;

/// <summary>
/// Applies the user's 4 custom colors by replacing the Application.Resources brush entries with brand new
/// SolidColorBrush instances (not mutating an existing brush's Color property in place, the M15 approach
/// this replaced): Application.Resources silently Freezes any Freezable inserted into it - confirmed
/// empirically, a plain SolidColorBrush built fresh in C# with no XAML involved flips IsFrozen from false to
/// true the instant it's assigned into Application.Current.Resources[key] - so an in-place `.Color = x` on
/// anything living there always throws. Constructing a new brush with the final color already baked in and
/// replacing the dictionary entry sidesteps that entirely: freezing on insert doesn't matter when nothing
/// needs to mutate that same object again later. This only re-themes the app live because every XAML
/// consumer uses `{DynamicResource}` for these 10 keys (not `{StaticResource}`, which bakes in the original
/// object reference at parse time and would never see a dictionary-entry replacement).
///
/// The 4 slots, per the user's own mental model rather than Theme.xaml's original Primary(white)/
/// Secondary(black)/Tertiary(signal)/Neutral(surface ramp)/Alert structure:
///   Primary   - the main background (InkBrush - what Theme.xaml called "Neutral shade").
///   Secondary - panel/sidebar background (DeckBrush, plus a lightened RaisedBrush for hover states).
///   Tertiary  - body text, hairlines/borders, dim/eyebrow text, AND the signal accent (selection/live/
///               focus/hover) all together - one base color, tinted/shaded into each role via ColorUtils,
///               rather than 4 independently-set colors, so picking one hex genuinely "makes a pattern"
///               instead of requiring 4 separate choices for what reads as one visual family.
///   Quaternary - the alert/destructive color (TallyRedBrush: delete icons, recording indicator).
/// The original fixed Primary(white)/Secondary(black) pair backing PrimaryButtonStyle's filled "hero
/// action" look (Start Recording/Save/Export) is deliberately left alone - not part of the 4 user-facing
/// slots, since collapsing it into Primary/Secondary above would risk an unreadable button (e.g. black
/// button on a black page) whenever the user picks a dark Primary.
/// </summary>
public static class ThemeManager
{
    public const string DefaultPrimaryColorHex = "#0A0A0A";
    public const string DefaultSecondaryColorHex = "#1A1A1A";
    public const string DefaultTertiaryColorHex = "#E0E0FF";
    public const string DefaultQuaternaryColorHex = "#FF4433";

    /// <returns>True if every non-null/non-whitespace hex string parsed; a hex left null/blank is treated
    /// as "leave that slot unchanged" (not a failure), matching text-as-you-type Settings binding where an
    /// in-progress edit can be transiently blank.</returns>
    public static bool ApplyColors(string? primaryHex, string? secondaryHex, string? tertiaryHex, string? quaternaryHex)
    {
        var allValid = true;
        var resources = Application.Current.Resources;

        allValid &= ApplyOne(primaryHex, primary =>
        {
            resources["InkBrush"] = new SolidColorBrush(primary);
        });

        allValid &= ApplyOne(secondaryHex, secondary =>
        {
            resources["DeckBrush"] = new SolidColorBrush(secondary);
            resources["RaisedBrush"] = new SolidColorBrush(ColorUtils.Lighten(secondary, 0.15));
        });

        allValid &= ApplyOne(tertiaryHex, tertiary =>
        {
            resources["PaperBrush"] = new SolidColorBrush(tertiary);
            resources["TertiaryBrush"] = new SolidColorBrush(tertiary);
            resources["TertiaryTintBrush"] = new SolidColorBrush(ColorUtils.Lighten(tertiary, 0.5));
            resources["TertiaryShadeBrush"] = new SolidColorBrush(ColorUtils.Darken(tertiary, 0.35));
            resources["HairlineBrush"] = new SolidColorBrush(ColorUtils.Darken(tertiary, 0.82));
            resources["DimBrush"] = new SolidColorBrush(ColorUtils.Darken(tertiary, 0.45));
        });

        allValid &= ApplyOne(quaternaryHex, quaternary =>
        {
            resources["TallyRedBrush"] = new SolidColorBrush(quaternary);
        });

        return allValid;
    }

    /// <summary>Parses one hex slot and applies it via <paramref name="apply"/> if valid; a null/blank
    /// input is a no-op success, an unparseable non-blank one is reported as a failure.</summary>
    private static bool ApplyOne(string? hex, Action<Color> apply)
    {
        if (ColorUtils.TryParseHex(hex, out var color))
        {
            // ColorUtils.TryParseHex accepts an #AARRGGBB alpha-prefixed hex (it's a general-purpose
            // parser, also used by HexToBrushConverter's swatch preview), but this system has no
            // transparent surfaces anywhere - every one of these 4 slots backs an opaque background/
            // text/border brush, and the Window itself isn't AllowsTransparency. A user-supplied alpha
            // (e.g. pasting a color picker's "#80FF0000") would otherwise apply as "valid" but render as
            // an unreadable flat color flood instead of the translucency the alpha implied, since nothing
            // here composites against anything but an opaque surface. Forcing full opacity here - not in
            // TryParseHex itself, which stays a general #RGB/#RRGGBB/#AARRGGBB parser - keeps that
            // failure mode from reaching the theme at all.
            color.A = 255;
            apply(color);
            return true;
        }

        return string.IsNullOrWhiteSpace(hex);
    }

    public static void ResetToDefaults() =>
        ApplyColors(DefaultPrimaryColorHex, DefaultSecondaryColorHex, DefaultTertiaryColorHex, DefaultQuaternaryColorHex);
}

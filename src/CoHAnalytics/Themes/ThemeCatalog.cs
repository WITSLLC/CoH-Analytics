namespace CoHAnalytics.Themes;

public static class ThemeCatalog
{
    public static ThemeId Current { get; private set; } = ThemeId.Hero;

    public static void Apply(ThemeId themeId)
    {
        Current = themeId;
    }
}

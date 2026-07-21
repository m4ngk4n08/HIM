namespace HIM.Gateway.Services.SSH;

public enum Theme
{
    Dark,   // default (Cyan/Blue accents)
    Neon,   // Magenta/Green accents, brighter
    Retro   // Amber/Green terminal vibes
}

public static class ThemeService
{
    private static Theme _currentTheme = Theme.Dark;

    public static Theme CurrentTheme => _currentTheme;

    public static void SetTheme(Theme theme) => _currentTheme = theme;

    public static Spectre.Console.Color PrimaryColor => _currentTheme switch
    {
        Theme.Neon => Spectre.Console.Color.Magenta1,
        Theme.Retro => Spectre.Console.Color.Orange1,
        _ => Spectre.Console.Color.Cyan1
    };

    public static Spectre.Console.Color SecondaryColor => _currentTheme switch
    {
        Theme.Neon => Spectre.Console.Color.Lime,
        Theme.Retro => Spectre.Console.Color.Green,
        _ => Spectre.Console.Color.Blue
    };

    public static Spectre.Console.Color AccentColor => _currentTheme switch
    {
        Theme.Neon => Spectre.Console.Color.HotPink,
        Theme.Retro => Spectre.Console.Color.Yellow,
        _ => Spectre.Console.Color.Teal
    };

    public static Spectre.Console.Color WarningColor => Spectre.Console.Color.Orange1;
    public static Spectre.Console.Color ErrorColor => Spectre.Console.Color.Red;
    public static Spectre.Console.Color SuccessColor => Spectre.Console.Color.Green;
}
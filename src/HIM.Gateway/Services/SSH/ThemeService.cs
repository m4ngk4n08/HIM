using HIM.Gateway.Services.SSH.Interfaces;

namespace HIM.Gateway.Services.SSH;

public enum Theme
{
    Dark,   // default (Cyan/Blue accents)
    Neon,   // Magenta/Green accents, brighter
    Retro   // Amber/Green terminal vibes
}

public class ThemeService : IThemeService
{
    private Theme _currentTheme = Theme.Dark;

    public Theme CurrentTheme => _currentTheme;

    public void SetTheme(Theme theme) => _currentTheme = theme;

    public Spectre.Console.Color PrimaryColor => _currentTheme switch
    {
        Theme.Neon => Spectre.Console.Color.Magenta1,
        Theme.Retro => Spectre.Console.Color.Orange1,
        _ => Spectre.Console.Color.Cyan1
    };

    public Spectre.Console.Color SecondaryColor => _currentTheme switch
    {
        Theme.Neon => Spectre.Console.Color.Lime,
        Theme.Retro => Spectre.Console.Color.Green,
        _ => Spectre.Console.Color.Blue
    };

    public Spectre.Console.Color AccentColor => _currentTheme switch
    {
        Theme.Neon => Spectre.Console.Color.HotPink,
        Theme.Retro => Spectre.Console.Color.Yellow,
        _ => Spectre.Console.Color.Teal
    };

    public Spectre.Console.Color WarningColor => Spectre.Console.Color.Orange1;
    public Spectre.Console.Color ErrorColor => Spectre.Console.Color.Red;
    public Spectre.Console.Color SuccessColor => Spectre.Console.Color.Green;
}
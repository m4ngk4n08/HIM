namespace HIM.Gateway.Services.SSH.Interfaces;

public interface IThemeService
{
    Theme CurrentTheme { get; }

    void SetTheme(Theme theme);

    Spectre.Console.Color PrimaryColor { get; }
    Spectre.Console.Color SecondaryColor { get; }
    Spectre.Console.Color AccentColor { get; }
    Spectre.Console.Color WarningColor { get; }
    Spectre.Console.Color ErrorColor { get; }
    Spectre.Console.Color SuccessColor { get; }
}

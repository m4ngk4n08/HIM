namespace HIM.Gateway.Services.SSH.Commands
{
    /// <summary>
    /// Everything /help renders about one command, plus the handler type SlashCommandRegistry
    /// resolves when the name is matched.
    /// </summary>
    public sealed record SlashCommandDescriptor(
        string Name,
        string Usage,
        string Description,
        int HelpOrder,
        Type HandlerType);
}

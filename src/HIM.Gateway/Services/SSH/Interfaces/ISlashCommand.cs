using HIM.Gateway.Models;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    /// <summary>
    /// One slash command. Implementations carry a [SlashCommand] attribute so
    /// SlashCommandCatalog.Discover can find them at startup.
    /// </summary>
    public interface ISlashCommand
    {
        Task ExecuteAsync(CommandContext context);
    }
}

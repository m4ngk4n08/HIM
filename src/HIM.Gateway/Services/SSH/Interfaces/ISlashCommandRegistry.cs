using HIM.Gateway.Services.SSH.Commands;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    /// <summary>
    /// Case-insensitive lookup from a command's first token to its handler, plus the metadata
    /// /help renders - the same catalog backs both, so they cannot drift apart.
    /// </summary>
    public interface ISlashCommandRegistry
    {
        bool TryGet(string name, out ISlashCommand command);

        IReadOnlyList<SlashCommandDescriptor> Descriptors { get; }
    }
}

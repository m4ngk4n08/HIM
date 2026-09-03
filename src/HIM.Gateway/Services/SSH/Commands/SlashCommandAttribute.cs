using System;

namespace HIM.Gateway.Services.SSH.Commands
{
    /// <summary>
    /// Marks a class as a slash command, discovered once at startup by
    /// <see cref="SlashCommandCatalog.Discover"/>. Name and Description drive both routing
    /// (SlashCommandRegistry.TryGet) and /help's rendered table, from the same metadata, so the
    /// two can no longer drift apart.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SlashCommandAttribute : Attribute
    {
        public SlashCommandAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }

        public string Description { get; }

        /// <summary>/help's left column when set; falls back to Name otherwise.</summary>
        public string? Usage { get; init; }

        /// <summary>/help sorts ascending on this.</summary>
        public int HelpOrder { get; init; }
    }
}

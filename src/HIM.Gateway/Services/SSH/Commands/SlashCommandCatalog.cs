using System.Reflection;
using HIM.Gateway.Services.SSH.Interfaces;

namespace HIM.Gateway.Services.SSH.Commands
{
    /// <summary>
    /// The result of reflecting over an assembly once for [SlashCommand]-attributed types.
    /// Built once at startup by ServiceExtensions.AddService and registered as a singleton -
    /// an SSH connection gets its own DI scope, so scanning here (instead of in the scoped
    /// registry) means reflection runs once per process, not once per visitor.
    /// </summary>
    public sealed class SlashCommandCatalog
    {
        public IReadOnlyList<SlashCommandDescriptor> Descriptors { get; }

        private SlashCommandCatalog(IReadOnlyList<SlashCommandDescriptor> descriptors)
        {
            Descriptors = descriptors;
        }

        public static SlashCommandCatalog Discover(Assembly assembly)
        {
            var descriptors = new List<SlashCommandDescriptor>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<SlashCommandAttribute>();
                if (attribute is null) continue;

                if (!typeof(ISlashCommand).IsAssignableFrom(type))
                {
                    throw new InvalidOperationException(
                        $"{type.FullName} carries [SlashCommand(\"{attribute.Name}\")] but does not implement {nameof(ISlashCommand)}.");
                }

                if (!seenNames.Add(attribute.Name))
                {
                    throw new InvalidOperationException(
                        $"Duplicate slash command name \"{attribute.Name}\" on {type.FullName}.");
                }

                descriptors.Add(new SlashCommandDescriptor(
                    attribute.Name,
                    attribute.Usage ?? attribute.Name,
                    attribute.Description,
                    attribute.HelpOrder,
                    type));
            }

            descriptors.Sort((a, b) => a.HelpOrder.CompareTo(b.HelpOrder));
            return new SlashCommandCatalog(descriptors);
        }
    }
}

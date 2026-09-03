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

        public static SlashCommandCatalog Discover(Assembly assembly) => Discover(assembly.GetTypes());

        // Split out from Discover(Assembly) so tests can hand it an exact, small set of fixture
        // types instead of a whole assembly. Scanning a whole test assembly for two *different*
        // violation fixtures (duplicate name vs. duplicate HelpOrder) is a trap: Discover throws
        // on the first violation Type.GetTypes() happens to return, which is the same fixed
        // result for every caller scanning that assembly - so two tests expecting two different
        // violations from the same assembly-wide scan cannot both be reliably true. Explicit
        // type lists sidestep that entirely.
        internal static SlashCommandCatalog Discover(IEnumerable<Type> candidateTypes)
        {
            var descriptors = new List<SlashCommandDescriptor>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenHelpOrders = new HashSet<int>();

            foreach (var type in candidateTypes)
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

                // List<T>.Sort is not stable, so two descriptors sharing a HelpOrder could come
                // out in either order between runs - /help rendering two different tables from
                // identical code. Refusing to start on a duplicate order (like the name check
                // above) is safer than tie-breaking on Name: a shared order is almost always a
                // copy-paste mistake, not two commands that legitimately want to sit together.
                if (!seenHelpOrders.Add(attribute.HelpOrder))
                {
                    throw new InvalidOperationException(
                        $"Duplicate HelpOrder {attribute.HelpOrder} on {type.FullName}.");
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

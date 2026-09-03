using System.Diagnostics.CodeAnalysis;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Services.SSH.Commands
{
    /// <summary>
    /// Scoped so handler resolution goes through the session's own DI scope (a handler wraps a
    /// scoped I*CommandService, e.g. MenuCommand -> IMenuCommandService). The catalog it reads
    /// from is a singleton built once at startup; only the IServiceProvider here is scoped.
    /// </summary>
    public sealed class SlashCommandRegistry : ISlashCommandRegistry
    {
        private readonly SlashCommandCatalog _catalog;
        private readonly IServiceProvider _serviceProvider;

        public SlashCommandRegistry(SlashCommandCatalog catalog, IServiceProvider serviceProvider)
        {
            _catalog = catalog;
            _serviceProvider = serviceProvider;
        }

        public IReadOnlyList<SlashCommandDescriptor> Descriptors => _catalog.Descriptors;

        public bool TryGet(string name, [MaybeNullWhen(false)] out ISlashCommand command)
        {
            var descriptor = _catalog.Descriptors.FirstOrDefault(
                d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

            if (descriptor is null)
            {
                command = null!;
                return false;
            }

            command = (ISlashCommand)_serviceProvider.GetRequiredService(descriptor.HandlerType);
            return true;
        }
    }
}

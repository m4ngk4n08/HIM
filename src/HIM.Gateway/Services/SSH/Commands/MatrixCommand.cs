using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/matrix", "Digital rain animation", HelpOrder = 3)]
    public sealed class MatrixCommand : ISlashCommand
    {
        private readonly IMatrixCommandService _matrixCommandService;

        public MatrixCommand(IMatrixCommandService matrixCommandService)
        {
            _matrixCommandService = matrixCommandService;
        }

        public Task ExecuteAsync(CommandContext context) =>
            _matrixCommandService.ExecuteAsync(context.Console, context.Stream, context.Ct);
    }
}

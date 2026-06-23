using HIM.Gateway.Models.Knowledge;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher
{
    public interface IStatsCommandService
    {
        Task ExecuteAsync(IAnsiConsole console, PortfolioData data, CancellationToken cancellationToken);
    }
}

using HIM.Gateway.Services.ServiceModel.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.IGame
{
    public interface IGameInputService
    {
        /// <summary>
        /// Reads the next high-level input from the provided stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<GameInput> GetNextInputAsync(Stream stream, CancellationToken ct);
    }
}

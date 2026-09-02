using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging;

namespace HIM.Gateway.Services.SSH.Gates
{
    /// <summary>
    /// Layer 1 — IP ban check. Delegates the actual ban state to IIpBanService; this gate is
    /// just the lock-free read on the accept-loop hot path.
    /// </summary>
    public sealed class IpBanGate : IConnectionGate
    {
        private readonly IIpBanService _ipBanService;
        private readonly ILogger<IpBanGate> _logger;

        public string Layer => "L1 IpBan";

        public IpBanGate(IIpBanService ipBanService, ILogger<IpBanGate> logger)
        {
            _ipBanService = ipBanService;
            _logger = logger;
        }

        public GateResult Evaluate(ConnectionContext ctx)
        {
            if (!_ipBanService.IsBanned(ctx.IpAddress))
                return GateResult.Allow();

            _logger.LogWarning(
                "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | BANNED IP | " +
                "Rejected: {IpAddress}",
                DateTime.UtcNow, ctx.IpAddress);
            return GateResult.Reject("Banned");
        }
    }
}

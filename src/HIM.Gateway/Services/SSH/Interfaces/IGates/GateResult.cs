namespace HIM.Gateway.Services.SSH.Interfaces.IGates
{
    /// <summary>
    /// A gate's decision: allow, or reject with a reason. Reason strings are passed to
    /// TarpitAndReject and land in logs the fail2ban jail reads — they must stay byte-identical
    /// to the strings the pre-extraction if-ladder produced ("GlobalFloodLimit", "Banned",
    /// "RateOrConcurrentLimit"). Changing one silently changes what gets banned on the VPS.
    /// </summary>
    public readonly record struct GateResult
    {
        public bool IsAllowed { get; }
        public string? Reason { get; }

        private GateResult(bool isAllowed, string? reason)
        {
            IsAllowed = isAllowed;
            Reason = reason;
        }

        public static GateResult Allow() => new(true, null);

        public static GateResult Reject(string reason) => new(false, reason);
    }
}

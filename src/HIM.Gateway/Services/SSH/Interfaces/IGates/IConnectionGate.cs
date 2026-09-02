namespace HIM.Gateway.Services.SSH.Interfaces.IGates
{
    /// <summary>
    /// One accept-loop defense decision. Deliberately synchronous: every gate today is a
    /// lock-free in-memory read, so there is nothing to await — an async signature would cost a
    /// state machine on the hottest path in the process for no benefit. If a future gate needs
    /// I/O, that is when this signature changes.
    /// </summary>
    public interface IConnectionGate
    {
        /// <summary>
        /// e.g. "L3 GlobalFlood" — used in logs and pinned by the registration-order test.
        /// </summary>
        string Layer { get; }

        GateResult Evaluate(ConnectionContext ctx);
    }
}

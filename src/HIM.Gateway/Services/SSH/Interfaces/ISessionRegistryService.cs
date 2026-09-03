using System;
using System.Collections.Generic;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    /// <summary>One currently-connected session, as of the moment GetActiveSessions() was called.</summary>
    public readonly record struct SessionSnapshot(string SessionId, string IpAddress, DateTime ConnectedAtUtc);

    /// <summary>
    /// Task 24C: a singleton registry of who is connected right now, for the /who panel.
    /// Register/Deregister are a paired acquire/release on the connection-scope lifetime, the
    /// same shape as PerIpConcurrencyGate.Release - the caller must Deregister in a finally so a
    /// session that ends by exception doesn't linger forever.
    ///
    /// Holds only plain immutable values (session id, raw IP, connect timestamp) - never a
    /// reference to UserSessionState or anything else scoped. That would pin one visitor's
    /// session state for the life of the process, and ValidateScopes will not catch it: storing a
    /// scoped object in a singleton's dictionary at runtime is legal DI, just wrong.
    /// </summary>
    public interface ISessionRegistryService
    {
        /// <summary>Records a session as connected, timestamped from the injected TimeProvider.</summary>
        void Register(string sessionId, string ipAddress);

        /// <summary>Removes a session. Safe to call for a session that was never registered.</summary>
        void Deregister(string sessionId);

        /// <summary>
        /// A materialized copy of every currently-connected session - not a live view of the
        /// internal map, so a renderer iterating it never races a connect/disconnect happening
        /// underneath it.
        /// </summary>
        IReadOnlyList<SessionSnapshot> GetActiveSessions();
    }
}

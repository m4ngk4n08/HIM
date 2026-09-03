using HIM.Gateway.Services.SSH.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// Task 24C: the session registry backing /who. Registered as a singleton in
    /// ServiceExtensions.AddService; SshServerListener registers a session when its scope is
    /// created and deregisters it in the same finally that disposes that scope, so a session
    /// ending by exception is removed the same as one ending cleanly.
    /// </summary>
    public sealed class SessionRegistryService : ISessionRegistryService
    {
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, SessionSnapshot> _sessions = new(StringComparer.Ordinal);

        public SessionRegistryService(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public void Register(string sessionId, string ipAddress)
        {
            _sessions[sessionId] = new SessionSnapshot(sessionId, ipAddress, _timeProvider.GetUtcNow().UtcDateTime);
        }

        public void Deregister(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
        }

        public IReadOnlyList<SessionSnapshot> GetActiveSessions()
        {
            // Materializing into a List here (rather than handing back the ConcurrentDictionary's
            // own weakly-consistent enumerator) is what turns a live view into an actual snapshot
            // a renderer can safely hold onto, same as IpBanService.GetActiveBans.
            return _sessions.Values.ToList();
        }
    }
}

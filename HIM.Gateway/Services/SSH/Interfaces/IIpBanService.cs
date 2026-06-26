using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IIpBanService
    {
        /// <summary>
        /// Returns true if the IP is currently serving an active ban
        /// lock-free read- safe to call from the TCP accept hot path.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        bool IsBanned(string ipAddress);

        /// <summary>
        /// Increments the strike counter for an IP. Automatically applies
        /// an escalating ban when the configured threshold is reached.
        /// </summary>
        /// <param name="ipAddress"></param>
        void RecordStrike(string ipAddress);

        /// <summary>
        /// removes expired and low-risk entries from the ban map.
        /// called automatically on schedule; can be invoked manually.
        /// </summary>
        void Prune();
    }
}

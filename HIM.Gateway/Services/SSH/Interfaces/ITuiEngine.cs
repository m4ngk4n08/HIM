using Microsoft.DevTunnels.Ssh;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface ITuiEngine
    {
        Task RunAsync(SshChannel channel, uint width, uint height, CancellationToken cts);

        void HandleResize(SshChannel channel, uint width, uint height);
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface ISshServerListener
    {
        // Starts Listeneing for icnoming SSH connections.
        Task StartAsync(CancellationToken cancellationToken);
    }
}

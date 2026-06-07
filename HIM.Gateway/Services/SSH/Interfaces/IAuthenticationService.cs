using Microsoft.DevTunnels.Ssh.Events;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IAuthenticationService
    {
        bool Authenticate(object? sender, SshAuthenticatingEventArgs e);
    }
}

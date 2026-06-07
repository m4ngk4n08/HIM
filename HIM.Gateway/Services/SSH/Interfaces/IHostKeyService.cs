using Microsoft.DevTunnels.Ssh.Algorithms;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IHostKeyService
    {
        Task<IKeyPair> GetHostKeyAsync();
    }
}

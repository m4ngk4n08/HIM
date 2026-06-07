using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh.Algorithms;

using Microsoft.DevTunnels.Ssh.Keys;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HIM.Gateway.Services.SSH
{
    public class HostKeyService : IHostKeyService
    {
        private readonly SshSettings _settings;
        private IKeyPair? _cachedKey;

        public HostKeyService(IOptions<SshSettings> settings)
        {
            _settings = settings.Value;
        }
        public async Task<IKeyPair> GetHostKeyAsync()
        {
            if (_cachedKey != null) return _cachedKey;

            var rsa = new Rsa("ssh-rsa", "SHA256");

            if (File.Exists(_settings.HostKeyPath))
            {
                var json = await File.ReadAllTextAsync(_settings.HostKeyPath);
                var keyData = JsonSerializer.Deserialize<RsaKeyData>(json);

                var keyPair = (Rsa.KeyPair)rsa.GenerateKeyPair(2048);
                keyPair.ImportParameters(keyData!.ToRSAParameters());
                _cachedKey = keyPair;
            }
            else
            {
                var keyPair = (Rsa.KeyPair)rsa.GenerateKeyPair(2048);
                var paras = keyPair.ExportParameters(true);

                var keyData = RsaKeyData.FromRSAParameters(paras);
                var json = JsonSerializer.Serialize(keyData);
                await File.WriteAllTextAsync(_settings.HostKeyPath, json);

                _cachedKey = keyPair;
            }

            return _cachedKey;
        }
    }
}

using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    public class GuestAuthenticationService : IAuthenticationService
    {
        public bool Authenticate(object? sender, SshAuthenticatingEventArgs e)
        {
            // We allow any username for 'guest' access
            if(e.AuthenticationType)
        }
    }
}

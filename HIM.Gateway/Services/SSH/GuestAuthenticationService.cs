using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh.Events;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    public class GuestAuthenticationService : IAuthenticationService
    {

        void IAuthenticationService.Authenticate(object? sender, SshAuthenticatingEventArgs e)
        {
            // for public portfolio, we allow everyone to login.
            // we use the provided username to create an identity.

            // 1. Create a standard.NET ClaimsIdentity
            var identity = new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, e.Username ?? "guest") },
                    "SSH"
                );

            // 2. Wrap it in a Principal
            var principal = new ClaimsPrincipal(identity);

            // 3. Critical Step: Assign the completed Task to AuthenticationTask.
            // This signals to the library that authentication is sucessful;
            e.AuthenticationTask = Task.FromResult<ClaimsPrincipal?>(principal);
            
        }
    }
}

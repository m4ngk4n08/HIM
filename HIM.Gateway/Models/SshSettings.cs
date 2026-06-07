using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Models
{
    public class SshSettings
    {
        public int Port { get; set; } = 2222;
        public string HostKeyPath { get; set; } = "hostkey.pem";
        public string WelcomeMessage { get; set; } = "--- Welcome to Angelo's Portfolio (SSH Edition) --- \n";
    }
}

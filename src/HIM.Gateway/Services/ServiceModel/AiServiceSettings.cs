using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.ServiceModel
{
    public class AiServiceSettings
    {
        public string BaseUrl { get; set; } = "http://localhost:5247";
        public string SharedSecret { get; set; } = string.Empty;
        public string ModelDisplayName { get; set; } = "gemini-3.1-flash-lite (Gemini)";
    }
}

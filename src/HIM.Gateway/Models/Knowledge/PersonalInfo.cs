using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Models.Knowledge
{
    public class PersonalInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, string> Contact { get; set; }
    }
}

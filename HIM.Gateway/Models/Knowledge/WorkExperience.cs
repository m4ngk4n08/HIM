using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Models.Knowledge
{
    public class WorkExperience
    {
        public string Company { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public List<string> Highlights { get; set; } = new();
    }
}

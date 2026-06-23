using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.ServiceModel.CommandDispatcher
{
    public class SkillProfile
    {
        public List<Skills> Skills { get; set; } = new();
    }

    public class Skills
    {
        public string Skill { get; set; } = string.Empty;
        public int Compentency { get; set; }
    }
}

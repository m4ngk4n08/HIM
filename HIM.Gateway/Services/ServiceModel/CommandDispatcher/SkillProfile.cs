using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace HIM.Gateway.Services.ServiceModel.CommandDispatcher
{
    public class CommandDispatcherConfig
    {
        [JsonPropertyName("skill_profile")]
        public SkillProfile SkillProfile { get; set; } = new();
    }
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

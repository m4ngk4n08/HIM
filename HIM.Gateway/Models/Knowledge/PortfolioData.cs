using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace HIM.Gateway.Models.Knowledge
{
    public class PortfolioData
    {
        [JsonPropertyName("personal_info")]
        public PersonalInfo PersonalInfo { get; set; } = new();

        [JsonPropertyName("experience")]
        public List<WorkExperience> Experiences { get; set; } = new();

        [JsonPropertyName("technical_skills")]
        public Dictionary<string, List<string>> TechnicalSkills { get; set; } = new();

        [JsonPropertyName("projects")]
        public List<ProjectItem> Projects { get; set; }
    }
}

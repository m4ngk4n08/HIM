using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Commands;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 26C: TourContentBuilder maps PortfolioData into each /tour mode's step list. The
/// security requirement (Task 21D's RedactPhone-then-EscapeMarkup egress rule) is pinned here at
/// the content layer, same shape as CiteCommandTests' canary tests - TourCommandTests (26D) pins
/// the same guarantee again end-to-end, through the real command's navigation loop.
/// </summary>
public class TourContentBuilderTests
{
    private const string Canary = "555-010-2020";

    private static PortfolioData BuildData(string? phoneInSummary = null, string? phoneInContact = null) => new()
    {
        PersonalInfo = new PersonalInfo
        {
            Name = "Angelo",
            Role = "Software Engineer",
            Location = "Remote",
            Summary = phoneInSummary ?? "Builds things.",
            Contact = phoneInContact is null
                ? new Dictionary<string, string> { ["github"] = "angelodavales" }
                : new Dictionary<string, string> { ["phone"] = phoneInContact }
        },
        Experiences =
        [
            new WorkExperience { Company = "Acme", Position = "Engineer", Duration = "2020-2024", Highlights = ["Shipped things."] }
        ],
        TechnicalSkills = new Dictionary<string, List<string>> { ["backend"] = ["C#", ".NET"] },
        Projects = [new ProjectItem { Name = "HIM", Stack = ".NET 10", Status = "Live" }]
    };

    [Fact]
    public void QuickMode_ProducesTheExpectedStepTitles_InOrder()
    {
        var steps = TourContentBuilder.BuildSteps(TourMode.Quick, BuildData());

        Assert.Equal(
            new[] { "WELCOME", "SKILLS & STACK", "EXPERIENCE", "PROJECTS", "WRAP-UP" },
            steps.Select(s => s.Title));
    }

    [Fact]
    public void RecruiterMode_ProducesTheExpectedStepTitles_InOrder()
    {
        var steps = TourContentBuilder.BuildSteps(TourMode.Recruiter, BuildData());

        Assert.Equal(
            new[] { "EXPERIENCE", "SKILLS & STACK", "PROJECTS", "CONTACT" },
            steps.Select(s => s.Title));
    }

    [Fact]
    public void EngineerMode_ProducesTheExpectedStepTitles_InOrder()
    {
        var steps = TourContentBuilder.BuildSteps(TourMode.Engineer, BuildData());

        Assert.Equal(
            new[] { "ARCHITECTURE", "PROJECTS", "SKILLS & STACK", "THE AI / RAG PIPELINE", "CONTACT" },
            steps.Select(s => s.Title));
    }

    [Fact]
    public void PhoneNumberInContact_RendersRedacted_InEveryMode()
    {
        var data = BuildData(phoneInContact: Canary);

        foreach (var mode in new[] { TourMode.Quick, TourMode.Recruiter, TourMode.Engineer })
        {
            var steps = TourContentBuilder.BuildSteps(mode, data);
            var allLines = string.Join("\n", steps.SelectMany(s => s.Lines));

            Assert.DoesNotContain(Canary, allLines);
            Assert.Contains("[REDACTED_PHONE]", allLines);
        }
    }

    [Fact]
    public void PhoneNumberInSummary_RendersRedacted_InQuickModesWelcomeStep()
    {
        var data = BuildData(phoneInSummary: $"Reach me at {Canary} anytime.");

        var steps = TourContentBuilder.BuildSteps(TourMode.Quick, data);
        var welcome = steps.Single(s => s.Title == "WELCOME");
        var text = string.Join("\n", welcome.Lines);

        Assert.DoesNotContain(Canary, text);
        Assert.Contains("[REDACTED_PHONE]", text);
    }
}

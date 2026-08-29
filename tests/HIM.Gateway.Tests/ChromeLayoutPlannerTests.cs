using HIM.Gateway.Services.SSH;

namespace HIM.Gateway.Tests;

/// <summary>
/// Tests for the pure chrome-layout decision function (see ChromeLayout.cs). This is the fix for
/// the reported bug: a fixed 16-line chrome region left as few as 8 content rows on an 80x24
/// terminal. These tests exercise the decision directly - no console, no SSH.
/// </summary>
public class ChromeLayoutPlannerTests
{
    [Fact]
    public void Size80x24_SelectsCompact_WithAtLeast12ContentRowsRemaining()
    {
        var layout = ChromeLayoutPlanner.Decide(width: 80, height: 24);

        Assert.Equal(ChromeVariant.Compact, layout.Variant);

        int contentRows = 24 - layout.ChromeLines;
        Assert.True(contentRows >= 12,
            $"Expected at least 12 content rows on an 80x24 terminal, got {contentRows}.");
    }

    [Fact]
    public void Size120x50_SelectsFull()
    {
        var layout = ChromeLayoutPlanner.Decide(width: 120, height: 50);

        Assert.Equal(ChromeVariant.Full, layout.Variant);
        Assert.True(layout.ShowFiglet);
    }

    [Fact]
    public void Size80x18_SelectsNone()
    {
        var layout = ChromeLayoutPlanner.Decide(width: 80, height: 18);

        Assert.Equal(ChromeVariant.None, layout.Variant);
        Assert.Equal(0, layout.ChromeLines);
        Assert.Equal(0, layout.FirstScrollLine);
    }

    /// <summary>
    /// The invariant sweep: for every width/height combination in the swept range, chrome must
    /// never claim more than 30% of the terminal height, and either a scrolling region isn't set
    /// at all (FirstScrollLine == 0, content flows into normal scrollback) or at least 12 content
    /// rows remain. This is the test that would have caught the original bug - a fixed 16-line
    /// chrome on an 80x24 terminal left only 8 usable rows, violating both invariants at once.
    /// </summary>
    [Fact]
    public void InvariantsHold_AcrossFullSizeSweep()
    {
        var violations = new List<string>();

        for (int width = 40; width <= 200; width += 8)
        {
            for (int height = 10; height <= 60; height += 2)
            {
                var layout = ChromeLayoutPlanner.Decide(width, height);

                if (layout.ChromeLines > height * 0.30)
                {
                    violations.Add(
                        $"{width}x{height}: chrome {layout.ChromeLines} lines exceeds 30% of height ({height * 0.30}).");
                }

                if (layout.FirstScrollLine != 0)
                {
                    int contentRows = height - layout.ChromeLines;
                    if (contentRows < 12)
                    {
                        violations.Add(
                            $"{width}x{height}: FirstScrollLine={layout.FirstScrollLine} but only {contentRows} content rows remain.");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Theory]
    [InlineData(200, 60)]
    [InlineData(40, 10)]
    [InlineData(60, 29)]
    [InlineData(59, 30)]
    public void NoneVariant_NeverSetsAScrollingRegion(int width, int height)
    {
        var layout = ChromeLayoutPlanner.Decide(width, height);

        if (layout.Variant == ChromeVariant.None)
        {
            Assert.Equal(0, layout.ChromeLines);
            Assert.Equal(0, layout.FirstScrollLine);
        }
    }
}

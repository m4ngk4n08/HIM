namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// Which chrome the terminal has room for. Ordered smallest to largest so
    /// <see cref="ChromeLayoutPlanner"/> can "degrade" by walking backwards through the enum.
    /// </summary>
    internal enum ChromeVariant
    {
        /// <summary>Nothing but the prompt - the terminal is too small for any chrome at all.</summary>
        None,

        /// <summary>One status line: "● HIM │ &lt;model&gt; │ SSH ACTIVE".</summary>
        Compact,

        /// <summary>Figlet panel, status bar, welcome text, help text, fun-fact footer.</summary>
        Full
    }

    /// <summary>
    /// What to draw for a given terminal size, and where the scrolling region (if any) begins.
    /// </summary>
    /// <param name="Variant">The chrome variant chosen for this terminal size.</param>
    /// <param name="ChromeLines">
    /// How many rows the chrome occupies. This is a nominal figure used only to pick and
    /// validate the variant; the actual DECSTBM boundary the renderer sets is computed from the
    /// real rendered output (see <c>TerminalLayoutService</c>), never guessed.
    /// </param>
    /// <param name="FirstScrollLine">
    /// 1-based row where the scrolling region should start, or 0 if no DECSTBM region should be
    /// set at all - content should flow into normal scrollback instead.
    /// </param>
    /// <param name="ShowFiglet">Whether the header includes the Figlet banner (only <see cref="ChromeVariant.Full"/>).</param>
    internal readonly record struct ChromeLayout(
        ChromeVariant Variant,
        int ChromeLines,
        int FirstScrollLine,
        bool ShowFiglet);

    /// <summary>
    /// Pure decision function for the adaptive terminal chrome. Takes only the terminal's
    /// width/height and returns what to draw - no <c>IAnsiConsole</c>, no I/O, so it can be
    /// unit-tested directly without a console or an SSH session.
    /// </summary>
    /// <remarks>
    /// This is the fix for the original bug: a fixed 16-line chrome region left as few as 8
    /// content rows on an 80x24 terminal. The two invariants below make that impossible for any
    /// terminal size - see the invariant-sweep test in HIM.Gateway.Tests.
    /// </remarks>
    internal static class ChromeLayoutPlanner
    {
        // Nominal, non-guessed line counts for each variant's static chrome content. "Nominal"
        // because they describe content whose size is fixed by construction (a single status
        // line, or a header + status bar + welcome + help + footer composed of literal text) -
        // unlike the old GetHeaderLineCount, these aren't an estimate of something that varies.
        // TerminalLayoutService renders the same composition and measures the true output via
        // Segment.SplitLines for the actual DECSTBM boundary; these constants exist only so the
        // decision below can be evaluated without a console.
        internal const int CompactChromeLines = 1;
        internal const int FullChromeLines = 13;

        // Invariant 1: chrome may never eat more than this fraction of the terminal height.
        private const double MaxChromeFraction = 0.30;

        // Invariant 2: content needs at least this many rows to be worth reserving a scrolling
        // region for; below this, let output flow into normal scrollback instead.
        private const int MinContentRows = 12;

        private const int MinHeightForAnyChrome = 20;
        private const int MinWidthForAnyChrome = 40;
        private const int MinHeightForFull = 30;
        private const int MinWidthForFull = 60;

        public static ChromeLayout Decide(int width, int height)
        {
            var variant = SelectBaselineVariant(width, height);

            // Invariant 1: degrade until the chrome fits within 30% of the height. This is the
            // rule that makes the reported bug (a fixed-size region regardless of terminal size)
            // impossible - no variant can ever claim a disproportionate share of a short terminal.
            while (variant != ChromeVariant.None && NominalChromeLines(variant) > height * MaxChromeFraction)
            {
                variant = Degrade(variant);
            }

            int chromeLines = NominalChromeLines(variant);
            int contentRows = height - chromeLines;

            // Invariant 2: only reserve a scrolling region if it leaves a usable amount of room.
            int firstScrollLine = variant == ChromeVariant.None || contentRows < MinContentRows
                ? 0
                : chromeLines + 1;

            return new ChromeLayout(variant, chromeLines, firstScrollLine, variant == ChromeVariant.Full);
        }

        private static ChromeVariant SelectBaselineVariant(int width, int height)
        {
            if (height < MinHeightForAnyChrome || width < MinWidthForAnyChrome)
                return ChromeVariant.None;

            if (height >= MinHeightForFull && width >= MinWidthForFull)
                return ChromeVariant.Full;

            return ChromeVariant.Compact;
        }

        private static int NominalChromeLines(ChromeVariant variant) => variant switch
        {
            ChromeVariant.None => 0,
            ChromeVariant.Compact => CompactChromeLines,
            ChromeVariant.Full => FullChromeLines,
            _ => 0
        };

        private static ChromeVariant Degrade(ChromeVariant variant) => variant switch
        {
            ChromeVariant.Full => ChromeVariant.Compact,
            _ => ChromeVariant.None
        };
    }
}

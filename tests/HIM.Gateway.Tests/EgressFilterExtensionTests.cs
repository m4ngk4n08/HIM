using HIM.Gateway.Extensions;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 14D (SEC-02), the streaming half. CommandService.Redact()'d safeQuestion and
/// safeResponse only ever fed the logger - console.Write(panel) rendered the unredacted AI
/// response to the visitor. EgressFilterExtension.RedactPiiAsync is the fix: it wraps the token
/// stream and must catch a phone-shaped pattern no matter where the underlying HTTP chunk
/// boundaries happen to fall - written first, per the brief, because a naive per-chunk regex
/// passes tests whose chunk boundaries just happen to avoid the pattern.
/// </summary>
public class EgressFilterExtensionTests
{
    // A fictional NANP number (555-01xx is reserved for fiction) shaped exactly like
    // SanitizerExtension.PhoneRegex expects - a stand-in canary, never the real retired number.
    private const string Canary = "555-010-2020";
    private const string RedactedMarker = "[REDACTED_PHONE]";

    private static async IAsyncEnumerable<string> AsChunks(params string[] chunks)
    {
        foreach (var c in chunks)
        {
            yield return c;
            await Task.Yield();
        }
    }

    private static async Task<string> CollectAsync(IAsyncEnumerable<string> stream)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var part in stream)
            sb.Append(part);
        return sb.ToString();
    }

    [Fact]
    public async Task WholeCanaryInOneChunk_IsRedacted()
    {
        var result = await CollectAsync(AsChunks($"Call me at {Canary} anytime.").RedactPiiAsync());

        Assert.DoesNotContain(Canary, result);
        Assert.Contains(RedactedMarker, result);
    }

    [Theory]
    // Every possible 2-way split point across the canary string itself.
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)]
    public async Task CanarySplitAcrossTwoChunks_AtEveryOffset_IsRedacted(int splitAt)
    {
        var prefix = "Reach him at ";
        var suffix = " if it's urgent.";
        var full = prefix + Canary + suffix;

        // Split the *entire* surrounding text at this offset, not just the canary, so the split
        // point walks through the prefix, the canary, and the suffix as splitAt scans upward.
        var cutIndex = prefix.Length + splitAt;
        var chunkA = full[..cutIndex];
        var chunkB = full[cutIndex..];

        var result = await CollectAsync(AsChunks(chunkA, chunkB).RedactPiiAsync());

        Assert.DoesNotContain(Canary, result);
        Assert.Contains(RedactedMarker, result);
    }

    [Theory]
    [InlineData(2, 5)] [InlineData(4, 9)] [InlineData(6, 10)] [InlineData(1, 11)]
    public async Task CanarySplitAcrossThreeChunks_AtVariousOffsets_IsRedacted(int split1, int split2)
    {
        var prefix = "His number: ";
        var full = prefix + Canary + ".";

        var c1 = prefix.Length + split1;
        var c2 = prefix.Length + split2;

        var chunkA = full[..c1];
        var chunkB = full[c1..c2];
        var chunkC = full[c2..];

        var result = await CollectAsync(AsChunks(chunkA, chunkB, chunkC).RedactPiiAsync());

        Assert.DoesNotContain(Canary, result);
        Assert.Contains(RedactedMarker, result);
    }

    [Fact]
    public async Task ManyTinyChunks_OneCharacterEach_StillRedacts()
    {
        var full = "Text before. " + Canary + " Text after.";
        var chunks = full.Select(c => c.ToString()).ToArray();

        var result = await CollectAsync(AsChunks(chunks).RedactPiiAsync());

        Assert.DoesNotContain(Canary, result);
        Assert.Contains(RedactedMarker, result);
        // Nothing else from the surrounding text should have been touched.
        Assert.Contains("Text before.", result);
        Assert.Contains("Text after.", result);
    }

    [Fact]
    public async Task EmailAddress_PassesThroughUnredacted()
    {
        // The egress filter is deliberately phone-only - the contact email must still reach
        // the visitor. Regression guard against accidentally wiring up the email+phone Redact().
        var result = await CollectAsync(AsChunks("Email angelodavales0528@gmail.com for details.").RedactPiiAsync());

        Assert.Contains("angelodavales0528@gmail.com", result);
    }

    [Fact]
    public async Task NoPii_PassesThroughUnchanged()
    {
        var full = "Angelo works in C# and builds RAG pipelines with ONNX embeddings.";
        var result = await CollectAsync(AsChunks(full[..20], full[20..]).RedactPiiAsync());

        Assert.Equal(full, result);
    }
}

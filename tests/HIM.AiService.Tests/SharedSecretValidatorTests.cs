using HIM.AiService.Security;

namespace HIM.AiService.Tests;

public class SharedSecretValidatorTests
{
    private const string ConfiguredSecret = "correct-horse-battery-staple";

    [Fact]
    public void IsValid_ReturnsTrue_WhenSecretMatches()
    {
        Assert.True(SharedSecretValidator.IsValid(ConfiguredSecret, ConfiguredSecret));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenProvidedIsNull()
    {
        Assert.False(SharedSecretValidator.IsValid(null, ConfiguredSecret));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenProvidedIsEmpty()
    {
        Assert.False(SharedSecretValidator.IsValid("", ConfiguredSecret));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenSecretWrong()
    {
        Assert.False(SharedSecretValidator.IsValid("not-the-secret", ConfiguredSecret));
    }
}

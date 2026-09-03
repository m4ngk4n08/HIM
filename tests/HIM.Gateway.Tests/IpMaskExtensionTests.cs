using HIM.Gateway.Extensions;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 23C: the ban table renders visitor IPs, so this is the one place allowed to know how a
/// masked address is built - documentation-range addresses only (203.0.113.0/24, 2001:db8::/32),
/// never a real IP, same convention as the phone-number canary elsewhere in this repo.
/// </summary>
public class IpMaskExtensionTests
{
    [Fact]
    public void Ipv4_MasksTheLastOctet()
    {
        Assert.Equal("203.0.113.x", IpMaskExtension.MaskIp("203.0.113.9"));
    }

    [Fact]
    public void Ipv6_KeepsTheFirst48Bits_AndMasksTheRest()
    {
        // 2001:db8::1 -> first three hextets (2001, 0db8, 0000) kept, rest masked.
        Assert.Equal("2001:db8:0::x", IpMaskExtension.MaskIp("2001:db8::1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("999.999.999.999")]
    public void MalformedOrEmptyInput_DoesNotThrow_AndDoesNotRenderTheRawInput(string? input)
    {
        var result = IpMaskExtension.MaskIp(input);

        Assert.False(string.IsNullOrEmpty(result));
        if (!string.IsNullOrEmpty(input)) Assert.DoesNotContain(input, result);
    }

    [Fact]
    public void DifferentIpv4Addresses_InTheSameNetwork_MaskToTheSameValue()
    {
        Assert.Equal(IpMaskExtension.MaskIp("203.0.113.1"), IpMaskExtension.MaskIp("203.0.113.254"));
    }
}

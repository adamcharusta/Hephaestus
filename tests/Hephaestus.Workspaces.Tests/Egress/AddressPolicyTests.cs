using System.Net;
using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Workspaces.Tests.Egress;

/// <summary>Story 3.4, AD-7, T7, T12: which addresses the proxy connects to; a loopback, link-local, or private one never.</summary>
public class AddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void An_IPv4_address_that_is_not_public_is_refused(string address)
    {
        Assert.False(AddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.0.1.1")]
    [InlineData("192.0.3.1")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.0")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("223.255.255.255")]
    [InlineData("104.18.32.7")]
    public void A_public_IPv4_address_is_allowed(string address)
    {
        Assert.True(AddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("2001::1")]
    [InlineData("2001:2::1")]
    [InlineData("2001:10::1")]
    [InlineData("2001:1f::1")]
    [InlineData("2001:20::1")]
    [InlineData("2001:2f::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:0001::")]
    [InlineData("2002:c0a8:0001::")]
    [InlineData("3fff::1")]
    public void An_IPv6_address_that_is_not_public_is_refused(string address)
    {
        Assert.False(AddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:3::1")]
    [InlineData("2001:30::1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2a00:1450:4001:80b::200e")]
    [InlineData("2002:0101:0101::")]
    [InlineData("::ffff:1.1.1.1")]
    public void A_public_IPv6_address_is_allowed_including_a_mapped_or_6to4_public_IPv4_one(string address)
    {
        Assert.True(AddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Fact]
    public void An_address_of_no_family_the_proxy_carries_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => AddressPolicy.IsPublic(null!));
    }
}

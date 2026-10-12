using RimTalk.Util;
using Xunit;

namespace RimTalk.Tests;

public class LocalHostTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.1.1")]
    [InlineData("mypc.local")]
    [InlineData("nas")]
    public void IsLocal_TrueForLocalAndLanHosts(string host) => Assert.True(LocalHost.IsLocal(host));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("api.openai.com")]
    [InlineData("10.example.com")]
    [InlineData("192.169.0.1")]
    public void IsLocal_FalseForPublicHosts(string host) => Assert.False(LocalHost.IsLocal(host));
}

using RoG.Core.Api.Generated;
using RoG.Linux.App;
using RoG.Linux.App.Services;

namespace RoG.Linux.Tests.App;

/// <summary>Eine Oberfläche für Linux und macOS – unter Linux bleiben Kennungen und Verdrahtung wie bisher.</summary>
public class HostPlatformTests
{
    [Fact]
    public void Linux_keeps_its_identity()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "Linux-Kennung");
        Assert.False(HostPlatform.IsMac);
        Assert.Equal("linux", HostPlatform.Client);
        Assert.StartsWith("RoG-Linux/", HostPlatform.UserAgent, StringComparison.Ordinal);
        Assert.Equal(ManageDevicesPlatform.Linux, HostPlatform.DevicePlatform);
        Assert.StartsWith("Linux-App", HostPlatform.DeviceName("Wahke"), StringComparison.Ordinal);
    }

    [Fact]
    public void Smoke_test_argument_is_parsed()
    {
        Assert.True(LaunchOptions.Parse(["--smoke-test"], _ => null).SmokeTest);
        Assert.False(LaunchOptions.Parse([], _ => null).SmokeTest);
    }
}

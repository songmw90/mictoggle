using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace MicToggle.Tests;

public sealed class MicrophoneStateHostTests
{
    private static readonly Type HostType =
        Type.GetType("MicToggle.MicrophoneStateHost, MicToggle", throwOnError: false)!;

    [Fact]
    public void Host_exposes_only_a_read_only_microphone_state_to_script()
    {
        Assert.NotNull(HostType);

        var enabled = HostType.GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(enabled);
        Assert.True(enabled!.CanRead);
        Assert.Null(enabled.SetMethod);
        Assert.Null(HostType.GetMethod("SetEnabled", BindingFlags.Instance | BindingFlags.Public));
        Assert.True(HostType.GetCustomAttribute<ComVisibleAttribute>()?.Value);
    }

    [Fact]
    public void Native_state_update_is_synchronous_and_not_public()
    {
        Assert.NotNull(HostType);

        var host = Activator.CreateInstance(HostType, nonPublic: true)!;
        var update = HostType.GetMethod(
            "SetEnabled",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var setAccess = HostType.GetMethod(
            "SetAccessAllowed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var enabled = HostType.GetProperty("Enabled")!;

        Assert.NotNull(update);
        Assert.NotNull(setAccess);
        Assert.False((bool)enabled.GetValue(host)!);

        update!.Invoke(host, [true]);
        Assert.False((bool)enabled.GetValue(host)!);

        setAccess!.Invoke(host, [true]);
        Assert.True((bool)enabled.GetValue(host)!);

        update.Invoke(host, [false]);
        Assert.False((bool)enabled.GetValue(host)!);

        update.Invoke(host, [true]);
        setAccess.Invoke(host, [false]);
        Assert.False((bool)enabled.GetValue(host)!);
    }
}

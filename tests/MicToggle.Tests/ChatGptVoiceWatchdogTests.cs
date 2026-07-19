using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptVoiceWatchdogTests
{
    private static readonly Type? WatchdogType =
        Type.GetType("MicToggle.ChatGptVoiceWatchdog, MicToggle", throwOnError: false);
    private static readonly Type? StateType =
        Type.GetType("MicToggle.ChatGptVoiceModeState, MicToggle", throwOnError: false);

    [Fact]
    public void Inactive_voice_mode_requests_immediate_recovery()
    {
        var watchdog = CreateWatchdog();

        Assert.Equal("Recover", Observe(watchdog, "Inactive", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Loading_must_remain_continuous_for_the_full_threshold()
    {
        var watchdog = CreateWatchdog();
        var startedAt = DateTimeOffset.UnixEpoch;

        Assert.Equal("None", Observe(watchdog, "Loading", startedAt));
        Assert.Equal("None", Observe(watchdog, "Loading", startedAt.AddSeconds(44)));
        Assert.Equal("Recover", Observe(watchdog, "Loading", startedAt.AddSeconds(45)));
    }

    [Fact]
    public void Healthy_or_unknown_dom_state_resets_the_loading_window()
    {
        var watchdog = CreateWatchdog();
        var startedAt = DateTimeOffset.UnixEpoch;

        Assert.Equal("None", Observe(watchdog, "Loading", startedAt));
        Assert.Equal("None", Observe(watchdog, "Active", startedAt.AddSeconds(44)));
        Assert.Equal("None", Observe(watchdog, "Loading", startedAt.AddSeconds(45)));
        Assert.Equal("None", Observe(watchdog, "Unknown", startedAt.AddSeconds(89)));
        Assert.Equal("None", Observe(watchdog, "Loading", startedAt.AddSeconds(90)));
    }

    private static object CreateWatchdog()
    {
        Assert.NotNull(WatchdogType);
        return Activator.CreateInstance(WatchdogType!, [TimeSpan.FromSeconds(45)])!;
    }

    private static string Observe(object watchdog, string stateName, DateTimeOffset observedAt)
    {
        Assert.NotNull(StateType);
        var state = Enum.Parse(StateType!, stateName);
        var method = WatchdogType!.GetMethod(
            "Observe",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!.Invoke(watchdog, [state, observedAt])!.ToString()!;
    }
}

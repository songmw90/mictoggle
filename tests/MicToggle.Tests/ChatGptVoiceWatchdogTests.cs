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

    [Fact]
    public void Active_voice_mode_is_force_refreshed_after_five_idle_minutes()
    {
        var watchdog = CreateWatchdog();
        var startedAt = DateTimeOffset.UnixEpoch;

        Assert.Equal("None", Observe(watchdog, "Active", startedAt));
        Assert.Equal("None", Observe(watchdog, "Active", startedAt.AddMinutes(5).AddTicks(-1)));
        Assert.Equal("Refresh", Observe(watchdog, "Active", startedAt.AddMinutes(5)));
        Assert.Equal("Refresh", Observe(watchdog, "Active", startedAt.AddMinutes(5).AddSeconds(1)));
    }

    [Fact]
    public void Push_to_talk_activity_delays_the_forced_refresh()
    {
        var watchdog = CreateWatchdog();
        var startedAt = DateTimeOffset.UnixEpoch;

        Assert.Equal("None", Observe(watchdog, "Active", startedAt));
        RecordActivity(watchdog, startedAt.AddMinutes(4));
        Assert.Equal("None", Observe(watchdog, "Active", startedAt.AddMinutes(5)));
        Assert.Equal("Refresh", Observe(watchdog, "Active", startedAt.AddMinutes(9)));
    }

    private static object CreateWatchdog()
    {
        Assert.NotNull(WatchdogType);
        return Activator.CreateInstance(
            WatchdogType!,
            [TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(5)])!;
    }

    private static void RecordActivity(object watchdog, DateTimeOffset observedAt)
    {
        var method = WatchdogType!.GetMethod(
            "RecordActivity",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(watchdog, [observedAt]);
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

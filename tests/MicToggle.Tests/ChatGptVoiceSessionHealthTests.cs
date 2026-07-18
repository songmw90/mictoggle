using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptVoiceSessionHealthTests
{
    private static readonly Type? HealthType =
        Type.GetType("MicToggle.ChatGptVoiceSessionHealth, MicToggle", throwOnError: false);

    [Fact]
    public void Any_fresh_active_track_prevents_scheduled_recovery()
    {
        var health = CreateHealth();
        Invoke(health, "Reset", 0L);
        Invoke(health, "Observe", "top", 0, 4_000L);
        Invoke(health, "Observe", "voice-frame", 1, 4_000L);

        Assert.False(Invoke<bool>(health, "TryBeginScheduledRecovery", 5_000L));
    }

    [Fact]
    public void Fresh_track_counts_are_aggregated_across_document_bridges()
    {
        var health = CreateHealth();
        Invoke(health, "Reset", 0L);

        Assert.Equal(0, Invoke<int>(health, "Observe", "top", 0, 1_000L));
        Assert.Equal(1, Invoke<int>(health, "Observe", "voice-frame", 1, 1_000L));
        Assert.Equal(2, Invoke<int>(health, "Observe", "second-frame", 1, 1_000L));
        Assert.Equal(1, Invoke<int>(health, "Observe", "voice-frame", 0, 2_000L));
    }

    [Fact]
    public void Sustained_fresh_zero_track_heartbeats_request_one_recovery()
    {
        var health = CreateHealth();
        Invoke(health, "Reset", 0L);
        Invoke(health, "Observe", "voice-frame", 0, 0L);
        Invoke(health, "Observe", "voice-frame", 0, 4_999L);

        Assert.False(Invoke<bool>(health, "TryBeginScheduledRecovery", 4_999L));

        Invoke(health, "Observe", "voice-frame", 0, 5_000L);
        Assert.True(Invoke<bool>(health, "TryBeginScheduledRecovery", 5_000L));
        Assert.False(Invoke<bool>(health, "TryBeginScheduledRecovery", 5_000L));
    }

    [Fact]
    public void Active_track_resets_the_disconnect_grace_period()
    {
        var health = CreateHealth();
        Invoke(health, "Reset", 0L);
        Invoke(health, "Observe", "voice-frame", 0, 0L);
        Invoke(health, "Observe", "voice-frame", 1, 4_000L);
        Invoke(health, "Observe", "voice-frame", 0, 5_000L);

        Assert.False(Invoke<bool>(health, "TryBeginScheduledRecovery", 9_999L));
        Invoke(health, "Observe", "voice-frame", 0, 10_000L);
        Assert.True(Invoke<bool>(health, "TryBeginScheduledRecovery", 10_000L));
    }

    [Fact]
    public void Stale_heartbeat_does_not_cycle_automatically_but_ptt_can_recover()
    {
        var health = CreateHealth();
        Invoke(health, "Reset", 0L);
        Invoke(health, "Observe", "voice-frame", 1, 0L);

        Assert.False(Invoke<bool>(health, "TryBeginScheduledRecovery", 10_000L));
        Assert.True(Invoke<bool>(health, "TryBeginPushRecovery", 10_000L));
    }

    [Fact]
    public void Recovery_cooldown_blocks_repeated_cycles()
    {
        var health = CreateHealth();
        Invoke(health, "Reset", 0L);
        Invoke(health, "Observe", "voice-frame", 0, 0L);
        Invoke(health, "Observe", "voice-frame", 0, 5_000L);
        Assert.True(Invoke<bool>(health, "TryBeginScheduledRecovery", 5_000L));

        Invoke(health, "CompleteRecovery", 6_000L);
        Invoke(health, "Observe", "voice-frame", 0, 11_000L);
        Assert.False(Invoke<bool>(health, "TryBeginScheduledRecovery", 15_999L));
        Invoke(health, "Observe", "voice-frame", 0, 16_000L);
        Assert.True(Invoke<bool>(health, "TryBeginScheduledRecovery", 16_000L));
    }

    private static object CreateHealth()
    {
        Assert.NotNull(HealthType);
        return Activator.CreateInstance(HealthType!, [5_000L, 2_000L, 10_000L])!;
    }

    private static object? Invoke(object target, string method, params object[] arguments)
    {
        var methodInfo = target.GetType().GetMethod(method);
        Assert.NotNull(methodInfo);
        return methodInfo!.Invoke(target, arguments);
    }

    private static T Invoke<T>(object target, string method, params object[] arguments) =>
        (T)Invoke(target, method, arguments)!;
}

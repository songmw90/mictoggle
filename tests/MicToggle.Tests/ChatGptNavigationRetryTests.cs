using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptNavigationRetryTests
{
    private static readonly Type? RetryType =
        Type.GetType("MicToggle.ChatGptNavigationRetry, MicToggle", throwOnError: false);

    [Fact]
    public void Retry_uses_exponential_backoff_capped_at_the_configured_maximum()
    {
        var retry = CreateRetry();
        var startedAt = DateTimeOffset.UnixEpoch;

        RecordFailure(retry, startedAt);
        Assert.False(ShouldRetry(retry, startedAt.AddMilliseconds(999)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(1)));
        Assert.False(ShouldRetry(retry, startedAt.AddSeconds(2)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(3)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(7)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(15)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(31)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(61)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(91)));
    }

    [Fact]
    public void Repeated_failures_do_not_postpone_an_already_scheduled_retry()
    {
        var retry = CreateRetry();
        var startedAt = DateTimeOffset.UnixEpoch;

        RecordFailure(retry, startedAt);
        RecordFailure(retry, startedAt.AddMilliseconds(900));

        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(1)));
    }

    [Fact]
    public void Successful_navigation_resets_the_backoff()
    {
        var retry = CreateRetry();
        var startedAt = DateTimeOffset.UnixEpoch;

        RecordFailure(retry, startedAt);
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(1)));
        Invoke(retry, "Reset");
        RecordFailure(retry, startedAt.AddSeconds(10));

        Assert.False(ShouldRetry(retry, startedAt.AddSeconds(10.999)));
        Assert.True(ShouldRetry(retry, startedAt.AddSeconds(11)));
    }

    private static object CreateRetry()
    {
        Assert.NotNull(RetryType);
        return Activator.CreateInstance(
            RetryType!,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)])!;
    }

    private static void RecordFailure(object retry, DateTimeOffset observedAt)
    {
        Invoke(retry, "RecordFailure", observedAt);
    }

    private static bool ShouldRetry(object retry, DateTimeOffset observedAt)
    {
        return (bool)Invoke(retry, "ShouldRetry", observedAt)!;
    }

    private static object? Invoke(object target, string methodName, params object[] args)
    {
        var method = RetryType!.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!.Invoke(target, args);
    }
}

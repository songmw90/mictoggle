using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptWindowLifecycleTests
{
    private static readonly Type StateType =
        Type.GetType("MicToggle.ChatGptWindowState, MicToggle", throwOnError: false)!;

    [Fact]
    public void Normal_navigation_start_keeps_release_commands_available()
    {
        Assert.NotNull(StateType);
        var state = Activator.CreateInstance(StateType)!;

        Invoke(state, "MarkBridgeCommandsAvailable");
        Invoke(state, "SetDesiredMicrophoneEnabled", true);

        Invoke(state, "ObserveNavigationStarting");
        Invoke(state, "SetDesiredMicrophoneEnabled", false);

        Assert.True(Get<bool>(state, "BridgeCommandsAvailable"));
        Assert.True(Get<bool>(state, "NavigationInProgress"));
        Assert.False(Get<bool>(state, "DesiredMicrophoneEnabled"));
    }

    [Fact]
    public void Navigation_completion_makes_new_bridge_available_with_latest_desired_state()
    {
        Assert.NotNull(StateType);
        var state = Activator.CreateInstance(StateType)!;

        Invoke(state, "SetDesiredMicrophoneEnabled", true);
        Invoke(state, "ObserveNavigationStarting");
        Assert.False(Get<bool>(state, "BridgeCommandsAvailable"));

        Invoke(state, "SetDesiredMicrophoneEnabled", false);

        Invoke(state, "CompleteNavigation");

        Assert.False(Get<bool>(state, "DesiredMicrophoneEnabled"));
        Assert.True(Get<bool>(state, "BridgeCommandsAvailable"));
        Assert.False(Get<bool>(state, "NavigationInProgress"));
    }

    [Fact]
    public void Process_failure_blocks_commands_until_replacement_navigation_completes()
    {
        Assert.NotNull(StateType);
        var state = Activator.CreateInstance(StateType)!;

        Invoke(state, "MarkBridgeCommandsAvailable");
        Invoke(state, "MarkBridgeCommandsUnavailable");

        Assert.False(Get<bool>(state, "BridgeCommandsAvailable"));

        Invoke(state, "CompleteNavigation");

        Assert.True(Get<bool>(state, "BridgeCommandsAvailable"));
    }

    [Fact]
    public void Recovery_is_single_flight_until_completed()
    {
        Assert.NotNull(StateType);
        var state = Activator.CreateInstance(StateType)!;

        Assert.True((bool)Invoke(state, "TryBeginRecovery")!);
        Assert.False((bool)Invoke(state, "TryBeginRecovery")!);

        Invoke(state, "EndRecovery");

        Assert.True((bool)Invoke(state, "TryBeginRecovery")!);
    }

    [Fact]
    public void Initialization_failure_exposes_retry_until_success()
    {
        Assert.NotNull(StateType);
        var state = Activator.CreateInstance(StateType)!;

        Invoke(state, "MarkInitializationFailed");

        Assert.True(Get<bool>(state, "RetryAvailable"));

        Invoke(state, "MarkInitializationSucceeded");

        Assert.False(Get<bool>(state, "RetryAvailable"));
    }

    [Theory]
    [InlineData("BrowserProcessExited", true)]
    [InlineData("RenderProcessExited", true)]
    [InlineData("RenderProcessUnresponsive", true)]
    [InlineData("FrameRenderProcessExited", true)]
    [InlineData("UtilityProcessExited", false)]
    [InlineData("GpuProcessExited", false)]
    public void Recovery_policy_recreates_only_browser_and_renderer_failures(
        string failureKind,
        bool expected)
    {
        Assert.NotNull(StateType);
        var enumType = Type.GetType(
            "Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind, Microsoft.Web.WebView2.Core",
            throwOnError: true)!;
        var value = Enum.Parse(enumType, failureKind);

        var actual = StateType.GetMethod("RequiresControlRecreation")!.Invoke(null, [value]);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false, 0, "Voice microphone disconnected - no active track")]
    [InlineData(true, 0, "Voice microphone disconnected - no active track")]
    [InlineData(false, 1, "Microphone off - 1 active track")]
    [InlineData(true, 2, "Microphone on - 2 active tracks")]
    public void Microphone_status_distinguishes_disconnected_and_live_tracks(
        bool enabled,
        int trackCount,
        string expected)
    {
        Assert.NotNull(StateType);

        var actual = StateType.GetMethod("FormatMicrophoneStatus")!
            .Invoke(null, [enabled, trackCount]);

        Assert.Equal(expected, actual);
    }

    private static object? Invoke(object target, string method, params object[] arguments)
    {
        var methodInfo = StateType.GetMethod(method);
        Assert.NotNull(methodInfo);
        return methodInfo!.Invoke(target, arguments);
    }

    private static T Get<T>(object target, string property) =>
        (T)StateType.GetProperty(property)!.GetValue(target)!;
}

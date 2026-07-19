using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptWindowContractTests
{
    [Fact]
    public void ChatGptWindow_replaces_SystemMicrophone_contract()
    {
        var windowType = Type.GetType("MicToggle.ChatGptWindow, MicToggle", throwOnError: false);

        Assert.NotNull(windowType);
        Assert.True(windowType!.IsSubclassOf(typeof(Form)));
        Assert.Equal(
            typeof(Task),
            windowType.GetMethod("SetMicrophoneEnabledAsync", [typeof(bool)])?.ReturnType);
        Assert.Equal(typeof(void), windowType.GetMethod("ShowWindow")?.ReturnType);
        Assert.Null(Type.GetType("MicToggle.SystemMicrophone, MicToggle", throwOnError: false));
    }

    [Fact]
    public void Navigation_handler_does_not_cancel_or_replay_requests()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var handlerStart = source.IndexOf(
            "private void HandleNavigationStarting",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private void HandleContentLoading",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.DoesNotContain(".Cancel", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigate(", handler, StringComparison.Ordinal);
        Assert.Contains("args.Uri", handler, StringComparison.Ordinal);
        Assert.Contains("args.NavigationId", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_wires_native_state_and_recursive_frame_fanout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains(
            "private readonly MicrophoneStateHost _microphoneStateHost = new()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private readonly ChatGptFrameRegistry _frameRegistry = new()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private readonly ChatGptTopLevelHostGate _topLevelHostGate = new()",
            source,
            StringComparison.Ordinal);
        Assert.Contains("core.AddHostObjectToScript", source, StringComparison.Ordinal);
        Assert.DoesNotContain("frame.AddHostObjectToScript", source, StringComparison.Ordinal);
        Assert.DoesNotContain("core.RemoveHostObjectFromScript", source, StringComparison.Ordinal);
        Assert.Contains("core.ContentLoading += HandleContentLoading", source, StringComparison.Ordinal);
        Assert.Contains("core.FrameCreated += HandleFrameCreated", source, StringComparison.Ordinal);
        Assert.Contains("frame.FrameCreated += HandleFrameCreated", source, StringComparison.Ordinal);
        Assert.Contains("frame.NavigationStarting += HandleFrameNavigationStarting", source, StringComparison.Ordinal);
        Assert.Contains("frame.ContentLoading += HandleFrameContentLoading", source, StringComparison.Ordinal);
        Assert.Contains("frame.NavigationCompleted += HandleFrameNavigationCompleted", source, StringComparison.Ordinal);
        Assert.Contains("frame.Destroyed += HandleFrameDestroyed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("frame.ContentLoading -= HandleFrameContentLoading", source, StringComparison.Ordinal);
        Assert.DoesNotContain("frame.FrameCreated -= HandleFrameCreated", source, StringComparison.Ordinal);
        Assert.Contains("core.PostWebMessageAsJson", source, StringComparison.Ordinal);
        Assert.Contains("frame.PostWebMessageAsJson", source, StringComparison.Ordinal);

        var setterStart = source.IndexOf(
            "public Task SetMicrophoneEnabledAsync",
            StringComparison.Ordinal);
        var setterEnd = source.IndexOf("public void ShowWindow", setterStart, StringComparison.Ordinal);
        var setter = source[setterStart..setterEnd];
        var hostUpdate = setter.IndexOf(
            "_microphoneStateHost.SetEnabled(enabled)",
            StringComparison.Ordinal);
        var desiredUpdate = setter.IndexOf(
            "_state.SetDesiredMicrophoneEnabled(enabled)",
            StringComparison.Ordinal);
        var drain = setter.IndexOf("DrainMicrophoneStateAsync()", StringComparison.Ordinal);
        Assert.True(hostUpdate >= 0 && hostUpdate < desiredUpdate && desiredUpdate < drain);

        var initializeStart = source.IndexOf(
            "private async Task<bool> InitializeWebViewAsync",
            StringComparison.Ordinal);
        var initializeEnd = source.IndexOf(
            "private async Task DrainMicrophoneStateAsync",
            initializeStart,
            StringComparison.Ordinal);
        var initialize = source[initializeStart..initializeEnd];
        var scriptRegistration = initialize.IndexOf(
            "core.AddScriptToExecuteOnDocumentCreatedAsync",
            StringComparison.Ordinal);
        var hostRegistration = initialize.IndexOf(
            "core.AddHostObjectToScript",
            StringComparison.Ordinal);
        var contentRegistration = initialize.IndexOf(
            "core.ContentLoading += HandleContentLoading",
            StringComparison.Ordinal);
        var firstNavigation = initialize.IndexOf("core.Navigate(HomeAddress)", StringComparison.Ordinal);
        Assert.Contains("_microphoneStateHost.SetAccessAllowed(false)", initialize, StringComparison.Ordinal);
        Assert.True(hostRegistration >= 0
            && hostRegistration < scriptRegistration
            && scriptRegistration < contentRegistration
            && contentRegistration < firstNavigation);
    }

    [Fact]
    public void Destroyed_frame_handler_only_forgets_managed_tracking_state()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var handlerStart = source.IndexOf(
            "private void HandleFrameDestroyed",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private void BroadcastToFrames",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.Contains("ForgetFrame(frame)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveFrame", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("DetachFrameEvents", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("frame.FrameId", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDestroyed", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Frame_content_loading_uses_navigation_id_and_error_page_state()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var handlerStart = source.IndexOf(
            "private void HandleFrameContentLoading",
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0);
        var handlerEnd = source.IndexOf(
            "private void HandleFrameNavigationCompleted",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.Contains("_frameRegistry.ObserveContentLoading", handler, StringComparison.Ordinal);
        Assert.Contains("args.NavigationId", handler, StringComparison.Ordinal);
        Assert.Contains("args.IsErrorPage", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostObjectToScript", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Top_level_content_loading_only_records_document_state()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var handlerStart = source.IndexOf(
            "private void HandleContentLoading",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private async void HandleNavigationCompleted",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.Contains("_topLevelHostGate.ObserveContentLoading", handler, StringComparison.Ordinal);
        Assert.Contains("_microphoneStateHost.SetAccessAllowed", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostObjectToScript", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveHostObjectFromScript", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_uses_compact_dark_product_chrome()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.DoesNotContain("ToolStripButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolStripStatusLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ToolStrip", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new StatusStrip", source, StringComparison.Ordinal);
        Assert.Contains("CreateNavigationButton", source, StringComparison.Ordinal);
        Assert.Contains("Segoe Fluent Icons", source, StringComparison.Ordinal);
        Assert.Contains("WindowTheme.ApplyDarkTitleBar(Handle)", source, StringComparison.Ordinal);
        Assert.Contains("StatusDot", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_toolbar_does_not_repeat_the_native_caption_or_fill_its_right_side()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var headerStart = source.IndexOf(
            "private Control CreateHeader()",
            StringComparison.Ordinal);
        var headerEnd = source.IndexOf(
            "private Control CreateStatusBar()",
            headerStart,
            StringComparison.Ordinal);
        Assert.True(headerStart >= 0 && headerEnd > headerStart);
        var header = source[headerStart..headerEnd];

        Assert.DoesNotContain("Text = \"ChatGPT Voice\"", header, StringComparison.Ordinal);
        Assert.Contains("header.Controls.Add(navigation, 0, 0)", header, StringComparison.Ordinal);
        Assert.Contains("header.Controls.Add(separator, 1, 0)", header, StringComparison.Ordinal);
        Assert.Contains("header.Controls.Add(pttSurface, 2, 0)", header, StringComparison.Ordinal);
        Assert.Contains("new ColumnStyle(SizeType.Percent, 100F)", header, StringComparison.Ordinal);
        Assert.DoesNotContain("header.Controls.Add(pttFrame", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Ptt_indicator_updates_before_web_bridge_availability_is_checked()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var applyStart = source.IndexOf(
            "private Task ApplyMicrophoneStateAsync",
            StringComparison.Ordinal);
        var applyEnd = source.IndexOf(
            "private Task RunOnUiThreadAsync",
            applyStart,
            StringComparison.Ordinal);
        Assert.True(applyStart >= 0 && applyEnd > applyStart);
        var apply = source[applyStart..applyEnd];

        var indicatorUpdate = apply.IndexOf("UpdatePttIndicator(enabled)", StringComparison.Ordinal);
        var bridgeCheck = apply.IndexOf("_state.BridgeCommandsAvailable", StringComparison.Ordinal);
        Assert.True(indicatorUpdate >= 0 && indicatorUpdate < bridgeCheck);
        Assert.Contains("Listening", source, StringComparison.Ordinal);
        Assert.Contains("Mic off", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_uses_the_embedded_application_icon_for_form_chrome()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains(
            "Icon.ExtractAssociatedIcon(Application.ExecutablePath)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("_windowIcon.Dispose()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_navigation_drains_ptt_state_before_auto_starting_voice_mode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var handlerStart = source.IndexOf(
            "private async void HandleNavigationCompleted",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private void HandleFrameCreated",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        var drain = handler.IndexOf("await DrainMicrophoneStateAsync()", StringComparison.Ordinal);
        var failureCheck = handler.IndexOf("if (failureStatus is not null)", StringComparison.Ordinal);
        var autoStart = handler.IndexOf("_ = TryAutoStartVoiceModeAsync(core)", StringComparison.Ordinal);
        Assert.True(drain >= 0 && drain < failureCheck && failureCheck < autoStart);
    }

    [Fact]
    public void Auto_start_checks_the_current_origin_before_showing_progress()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var methodStart = source.IndexOf(
            "private async Task TryAutoStartVoiceModeAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void DetachWebViewEvents",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        var originCheck = method.IndexOf(
            "ChatGptOriginPolicy.AllowsMicrophone(core.Source)",
            StringComparison.Ordinal);
        var progress = method.IndexOf(
            "SetStatus(\"Starting voice mode...\")",
            StringComparison.Ordinal);
        Assert.True(originCheck >= 0 && originCheck < progress);
    }

    [Fact]
    public void Window_refreshes_voice_mode_after_five_minutes_without_ptt_activity()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains(
            "private const int VoiceIdleRestartIntervalMilliseconds = 5 * 60 * 1000;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private readonly System.Windows.Forms.Timer _voiceIdleTimer = new()",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Interval = VoiceIdleRestartIntervalMilliseconds", source, StringComparison.Ordinal);
        Assert.Contains("_voiceIdleTimer.Tick += HandleVoiceIdleElapsed", source, StringComparison.Ordinal);
        Assert.Contains("_voiceIdleTimer.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("_voiceIdleTimer.Dispose()", source, StringComparison.Ordinal);

        var applyStart = source.IndexOf(
            "private Task ApplyMicrophoneStateAsync",
            StringComparison.Ordinal);
        var applyEnd = source.IndexOf("private Task RunOnUiThreadAsync", applyStart, StringComparison.Ordinal);
        Assert.True(applyStart >= 0 && applyEnd > applyStart);
        Assert.Contains("RestartVoiceIdleTimer()", source[applyStart..applyEnd], StringComparison.Ordinal);

        var handlerStart = source.IndexOf(
            "private async void HandleVoiceIdleElapsed",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private CoreWebView2? GetVoiceRecoveryCore",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];
        Assert.Contains("_voiceIdleTimer.Stop()", handler, StringComparison.Ordinal);
        Assert.Contains("await RecoverVoiceModeAsync(core)", handler, StringComparison.Ordinal);
        Assert.Contains("RestartVoiceIdleTimer()", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Voice_recovery_rearms_before_cycling_an_unhealthy_session()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        var methodStart = source.IndexOf(
            "private async Task RecoverVoiceModeAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void DetachWebViewEvents",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        var rearm = method.IndexOf("starter.Rearm()", StringComparison.Ordinal);
        var stop = method.IndexOf("ChatGptVoiceModeAutoStarter.TryStopScript", StringComparison.Ordinal);
        var waitForStart = method.IndexOf("WaitForVoiceModeReadyToStartAsync(core)", StringComparison.Ordinal);
        var restart = method.IndexOf("TryAutoStartVoiceModeAsync(core)", StringComparison.Ordinal);
        Assert.True(rearm >= 0 && rearm < stop && stop < waitForStart && waitForStart < restart);
        Assert.DoesNotContain("await Task.Delay(500)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Idle_refresh_mutes_output_around_the_dom_cycle_then_restores_current_volume()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));
        Assert.Contains(
            "private const int VoiceRefreshMuteTailMilliseconds = 2000;",
            source,
            StringComparison.Ordinal);

        var handlerStart = source.IndexOf(
            "private async void HandleVoiceIdleElapsed",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private void RestartVoiceIdleTimer",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        var mute = handler.IndexOf("_voiceRefreshMuted = true", StringComparison.Ordinal);
        var forceZero = handler.IndexOf("ApplyOutputVolume(reportErrors: false)", mute, StringComparison.Ordinal);
        var refresh = handler.IndexOf("await RecoverVoiceModeAsync(core)", forceZero, StringComparison.Ordinal);
        var tail = handler.IndexOf("Task.Delay(VoiceRefreshMuteTailMilliseconds)", refresh, StringComparison.Ordinal);
        var unmute = handler.IndexOf("_voiceRefreshMuted = false", tail, StringComparison.Ordinal);
        var restore = handler.IndexOf("ApplyOutputVolume(reportErrors: false)", unmute, StringComparison.Ordinal);
        Assert.True(mute >= 0
            && mute < forceZero
            && forceZero < refresh
            && refresh < tail
            && tail < unmute
            && unmute < restore);

        var applyStart = source.IndexOf(
            "private void ApplyOutputVolume",
            StringComparison.Ordinal);
        var applyEnd = source.IndexOf(
            "private void TrySaveOutputVolume",
            applyStart,
            StringComparison.Ordinal);
        Assert.True(applyStart >= 0 && applyEnd > applyStart);
        Assert.Contains(
            "var volumePercent = _voiceRefreshMuted ? 0 : _outputVolumeSlider.Value",
            source[applyStart..applyEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_launch_initializes_invisibly_then_remains_available_from_the_tray()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains("public ChatGptWindow(bool startHidden = false)", source, StringComparison.Ordinal);
        Assert.Contains("Opacity = 0", source, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false", source, StringComparison.Ordinal);
        Assert.Contains(
            "protected override bool ShowWithoutActivation => _hideAfterStartupInitialization;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Hide()", source, StringComparison.Ordinal);

        var showStart = source.IndexOf("public void ShowWindow()", StringComparison.Ordinal);
        var showEnd = source.IndexOf("public void ExitApplication()", showStart, StringComparison.Ordinal);
        Assert.True(showStart >= 0 && showEnd > showStart);
        var showWindow = source[showStart..showEnd];
        Assert.Contains("Opacity = 1", showWindow, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = true", showWindow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MicToggle.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

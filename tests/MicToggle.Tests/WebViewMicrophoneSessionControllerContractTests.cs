using Xunit;

namespace MicToggle.Tests;

public sealed class WebViewMicrophoneSessionControllerContractTests
{
    [Fact]
    public void Controller_defaults_to_muted_and_scopes_capture_sessions_to_the_host_tree()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "WebViewMicrophoneSessionController.cs");
        Assert.True(File.Exists(sourcePath));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private int _desiredMuted = 1", source, StringComparison.Ordinal);
        Assert.Contains("DataFlow.Capture", source, StringComparison.Ordinal);
        Assert.Contains(
            "ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "targetProcessIds.Contains((int)session.GetProcessID)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("volume.Mute = muted", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AudioEndpointVolume", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_tracks_new_capture_sessions_from_an_mta_worker()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "WebViewMicrophoneSessionController.cs"));

        Assert.Contains("SetApartmentState(ApartmentState.MTA)", source, StringComparison.Ordinal);
        Assert.Contains("_sessionManager.OnSessionCreated += HandleSessionCreated", source, StringComparison.Ordinal);
        Assert.Contains("_sessionManager.OnSessionCreated -= HandleSessionCreated", source, StringComparison.Ordinal);
        Assert.Contains("new AudioSessionControl(newSession)", source, StringComparison.Ordinal);
        Assert.Contains("RequestMuted(bool muted)", source, StringComparison.Ordinal);
        Assert.Contains("SetMutedAsync(bool muted)", source, StringComparison.Ordinal);
        Assert.Contains("PeriodicReassertIntervalMilliseconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_gates_page_microphone_state_with_the_capture_session_mute()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains(
            "private readonly WebViewMicrophoneSessionController _microphoneSessionController = new(Environment.ProcessId)",
            source,
            StringComparison.Ordinal);

        var setterStart = source.IndexOf(
            "public async Task SetMicrophoneEnabledAsync",
            StringComparison.Ordinal);
        var setterEnd = source.IndexOf("public void ShowWindow", setterStart, StringComparison.Ordinal);
        Assert.True(setterStart >= 0 && setterEnd > setterStart);
        var setter = source[setterStart..setterEnd];
        var desiredUpdate = setter.IndexOf(
            "_state.SetDesiredMicrophoneEnabled(enabled)",
            StringComparison.Ordinal);
        var releaseGuard = setter.IndexOf("if (!enabled)", StringComparison.Ordinal);
        var hostMute = setter.IndexOf("_microphoneStateHost.SetEnabled(false)", StringComparison.Ordinal);
        var captureMute = setter.IndexOf(
            "_microphoneSessionController.RequestMuted(true)",
            StringComparison.Ordinal);
        var drain = setter.IndexOf("DrainMicrophoneStateAsync()", StringComparison.Ordinal);
        Assert.True(desiredUpdate >= 0
            && desiredUpdate < releaseGuard
            && releaseGuard < hostMute
            && hostMute < captureMute
            && captureMute < drain);

        var drainStart = source.IndexOf(
            "private async Task DrainMicrophoneStateAsync",
            StringComparison.Ordinal);
        var drainEnd = source.IndexOf(
            "private Task ApplyMicrophoneStateAsync",
            drainStart,
            StringComparison.Ordinal);
        Assert.True(drainStart >= 0 && drainEnd > drainStart);
        var drainMethod = source[drainStart..drainEnd];
        var nativeGate = drainMethod.IndexOf(
            "await _microphoneSessionController.SetMutedAsync(!desired.Enabled)",
            StringComparison.Ordinal);
        var staleCheck = drainMethod.IndexOf(
            "if (!_state.IsCurrentDesiredState(desired.Version))",
            StringComparison.Ordinal);
        var hostEnable = drainMethod.IndexOf(
            "_microphoneStateHost.SetEnabled(desired.Enabled)",
            StringComparison.Ordinal);
        var pageUpdate = drainMethod.IndexOf(
            "ApplyMicrophoneStateAsync(desired.Enabled)",
            StringComparison.Ordinal);
        Assert.True(nativeGate >= 0
            && nativeGate < staleCheck
            && staleCheck < hostEnable
            && hostEnable < pageUpdate);

        Assert.Contains("_microphoneSessionController.Dispose()", source, StringComparison.Ordinal);
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

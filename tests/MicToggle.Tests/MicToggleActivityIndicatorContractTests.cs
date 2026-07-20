using Xunit;

namespace MicToggle.Tests;

public sealed class MicToggleActivityIndicatorContractTests
{
    [Fact]
    public void App_context_shows_and_hides_the_overlay_before_waiting_for_WebView()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "MicToggleAppContext.cs"));
        var methodStart = source.IndexOf(
            "private async Task SetHoldingAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void ShowBalloon",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        var show = method.IndexOf("_activityOverlay.ShowForAllScreens()", StringComparison.Ordinal);
        var hide = method.IndexOf("_activityOverlay.Hide()", StringComparison.Ordinal);
        var webView = method.IndexOf("await _window.SetMicrophoneEnabledAsync", StringComparison.Ordinal);

        Assert.True(show >= 0 && show < webView);
        Assert.True(hide >= 0 && hide < webView);
    }

    [Fact]
    public void Overlay_enumerates_all_screens_without_foreground_screen_routing()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "MicrophoneActivityOverlay.cs"));

        Assert.Contains("Screen.AllScreens", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetForegroundWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Screen.FromHandle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_context_forwards_real_mic_activity_and_disposes_the_overlay()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "MicToggleAppContext.cs"));

        Assert.Contains("_window.MicrophoneActivityChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("_activityOverlay.UpdateActivity", source, StringComparison.Ordinal);
        Assert.Contains("_window.MicrophoneActivityChanged -=", source, StringComparison.Ordinal);
        Assert.Contains("_activityOverlay.Dispose()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Edge_windows_are_nonactivating_and_click_through()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "MicrophoneActivityOverlay.cs"));

        Assert.Contains("WS_EX_NOACTIVATE", source, StringComparison.Ordinal);
        Assert.Contains("WS_EX_TRANSPARENT", source, StringComparison.Ordinal);
        Assert.Contains("WS_EX_TOOLWINDOW", source, StringComparison.Ordinal);
        Assert.Contains("ShowWithoutActivation", source, StringComparison.Ordinal);
        Assert.Contains("HTTRANSPARENT", source, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("MicToggle repository root was not found.");
    }
}

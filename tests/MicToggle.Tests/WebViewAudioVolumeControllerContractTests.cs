using Xunit;

namespace MicToggle.Tests;

public sealed class WebViewAudioVolumeControllerContractTests
{
    [Fact]
    public void Controller_scopes_audio_sessions_to_the_host_process_tree()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "WebViewAudioVolumeController.cs");
        Assert.True(File.Exists(sourcePath));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains(
            "ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "targetProcessIds.Contains((int)session.GetProcessID)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GetProcessesByName(\"msedgewebview2\")",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AudioEndpointVolume", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Activation_scope_mutes_new_process_tree_sessions_from_an_mta_thread()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "WebViewAudioVolumeController.cs"));

        Assert.Contains("SetApartmentState(ApartmentState.MTA)", source, StringComparison.Ordinal);
        Assert.Contains("_sessionManager.OnSessionCreated += HandleSessionCreated", source, StringComparison.Ordinal);
        Assert.Contains("_sessionManager.OnSessionCreated -= HandleSessionCreated", source, StringComparison.Ordinal);
        Assert.Contains("new AudioSessionControl(newSession)", source, StringComparison.Ordinal);
        Assert.Contains(
            "targetProcessIds.Contains((int)session.GetProcessID)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("volume.Volume = 0F", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_has_a_persistent_output_slider_and_session_refresh_timer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains("private readonly TrackBar _outputVolumeSlider", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Output volume\"", source, StringComparison.Ordinal);
        Assert.Contains("Minimum = 0", source, StringComparison.Ordinal);
        Assert.Contains("Maximum = 100", source, StringComparison.Ordinal);
        Assert.Contains("Interval = 1000", source, StringComparison.Ordinal);
        Assert.Contains("_audioVolumeController.ApplyVolume", source, StringComparison.Ordinal);
        Assert.Contains("_outputVolumeStore.Save", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_pins_the_stable_wasapi_package()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "MicToggle.csproj"));

        Assert.Contains(
            "<PackageReference Include=\"NAudio.Wasapi\" Version=\"2.3.0\" />",
            project,
            StringComparison.Ordinal);
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

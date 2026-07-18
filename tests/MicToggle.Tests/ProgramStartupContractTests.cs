using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace MicToggle.Tests;

public sealed class ProgramStartupContractTests
{
    [Fact]
    public void Startup_configures_the_bundled_WebView2_loader_before_WinForms_initialization()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "Program.cs"));
        var configure = source.IndexOf("ConfigureWebView2Loader();", StringComparison.Ordinal);
        var initialize = source.IndexOf("ApplicationConfiguration.Initialize();", StringComparison.Ordinal);

        Assert.True(configure >= 0 && configure < initialize);
        Assert.DoesNotContain("SanitizeWebView2LoaderSearchPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Loader_directory_resolves_the_architecture_specific_single_file_extraction()
    {
        var programType = Type.GetType("MicToggle.Program, MicToggle", throwOnError: true)!;
        var resolve = programType.GetMethod(
            "FindBundledWebView2LoaderDirectory",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(resolve);

        var root = Path.Combine(Path.GetTempPath(), $"MicToggleLoaderTest-{Guid.NewGuid():N}");
        var loaderDirectory = Path.Combine(root, "runtimes", "win-x64", "native");
        Directory.CreateDirectory(loaderDirectory);
        File.WriteAllText(Path.Combine(loaderDirectory, "WebView2Loader.dll"), "test");

        try
        {
            var nativeSearchDirectories = string.Join(Path.PathSeparator, root, root);
            var result = (string?)resolve!.Invoke(
                null,
                [nativeSearchDirectories, Architecture.X64]);

            Assert.Equal(loaderDirectory, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("--startup", true)]
    [InlineData("--STARTUP", true)]
    [InlineData("--other", false)]
    public void Startup_mode_requires_the_explicit_startup_argument(string argument, bool expected)
    {
        var programType = Type.GetType("MicToggle.Program, MicToggle", throwOnError: true)!;
        var isStartupLaunch = programType.GetMethod(
            "IsStartupLaunch",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(isStartupLaunch);

        Assert.Equal(expected, isStartupLaunch!.Invoke(null, [new[] { argument }]));
    }

    [Fact]
    public void Startup_mode_is_forwarded_to_the_application_context_and_window()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "Program.cs"));
        var context = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MicToggle",
            "MicToggleAppContext.cs"));

        Assert.Contains("private static void Main(string[] args)", program, StringComparison.Ordinal);
        Assert.Contains("new MicToggleAppContext(IsStartupLaunch(args))", program, StringComparison.Ordinal);
        Assert.Contains("public MicToggleAppContext(bool startHidden)", context, StringComparison.Ordinal);
        Assert.Contains("_window = new ChatGptWindow(startHidden)", context, StringComparison.Ordinal);
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

using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace MicToggle;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\MicToggle.CtrlAltHoldMic";
    private const string StartupArgument = "--startup";
    private const string WebView2LoaderFileName = "WebView2Loader.dll";

    [STAThread]
    private static void Main(string[] args)
    {
        ConfigureWebView2Loader();

        using var mutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MicToggleAppContext(IsStartupLaunch(args)));
    }

    private static bool IsStartupLaunch(string[] args) =>
        args.Any(argument => string.Equals(
            argument,
            StartupArgument,
            StringComparison.OrdinalIgnoreCase));

    private static void ConfigureWebView2Loader()
    {
        var loaderDirectory = FindBundledWebView2LoaderDirectory(
            AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string,
            RuntimeInformation.ProcessArchitecture);
        if (loaderDirectory is not null)
        {
            CoreWebView2Environment.SetLoaderDllFolderPath(loaderDirectory);
        }
    }

    private static string? FindBundledWebView2LoaderDirectory(
        string? nativeSearchDirectories,
        Architecture architecture)
    {
        if (string.IsNullOrWhiteSpace(nativeSearchDirectories))
        {
            return null;
        }

        var runtimeIdentifier = architecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => null,
        };
        if (runtimeIdentifier is null)
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in nativeSearchDirectories.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var root = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
            var candidates = new[]
            {
                Path.Combine(root, "runtimes", runtimeIdentifier, "native"),
                root,
            };

            foreach (var candidate in candidates)
            {
                if (visited.Add(candidate) &&
                    File.Exists(Path.Combine(candidate, WebView2LoaderFileName)))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

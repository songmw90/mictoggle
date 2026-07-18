using Xunit;

namespace MicToggle.Tests;

public sealed class BrandingContractTests
{
    [Fact]
    public void Main_window_uses_the_MicToggle_product_name()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "ChatGptWindow.cs"));

        Assert.Contains("Text = \"MicToggle\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"ChatGPT Voice\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_menu_uses_the_MicToggle_product_name()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "MicToggleAppContext.cs"));

        Assert.Contains("menu.Items.Add(\"Show MicToggle\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("menu.Items.Add(\"Show ChatGPT\"", source, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}

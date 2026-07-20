using System.Drawing;
using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class MicrophoneActivityOverlayTests
{
    private static readonly Type? OverlayType =
        Type.GetType("MicToggle.MicrophoneActivityOverlay, MicToggle", throwOnError: false);

    [Fact]
    public void Overlay_uses_the_product_mint_accent()
    {
        Assert.NotNull(OverlayType);
        var property = OverlayType!.GetProperty(
            "AccentColor",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(property);
        var actual = (Color)property!.GetValue(null)!;

        Assert.Equal(Color.FromArgb(76, 217, 130).ToArgb(), actual.ToArgb());
    }

    [Fact]
    public void Overlay_uses_four_thin_edges_for_every_monitor()
    {
        Assert.NotNull(OverlayType);
        var method = OverlayType!.GetMethod(
            "CreateAllEdgeBounds",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);

        Rectangle[] screens =
        [
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(0, -360, 2560, 1440),
        ];
        var edges = (Rectangle[])method!.Invoke(null, [screens, 4])!;

        Assert.Equal(
            [
                new Rectangle(-1920, 0, 1920, 4),
                new Rectangle(-1920, 1076, 1920, 4),
                new Rectangle(-1920, 4, 4, 1072),
                new Rectangle(-4, 4, 4, 1072),
                new Rectangle(0, -360, 2560, 4),
                new Rectangle(0, 1076, 2560, 4),
                new Rectangle(0, -356, 4, 1432),
                new Rectangle(2556, -356, 4, 1432),
            ],
            edges);
        Assert.Equal(8, edges.Length);
        Assert.All(edges, edge => Assert.True(edge.Width == 4 || edge.Height == 4));
    }

    [Theory]
    [InlineData(false, 0, 0.42)]
    [InlineData(true, 0, 0.68)]
    [InlineData(true, 0.5, 0.82)]
    [InlineData(true, 1, 0.96)]
    [InlineData(true, 3, 0.96)]
    public void Overlay_brightens_with_confirmed_input_without_changing_size(
        bool trackConnected,
        double level,
        double expected)
    {
        Assert.NotNull(OverlayType);
        var method = OverlayType!.GetMethod(
            "CalculateOpacity",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);

        var actual = (double)method!.Invoke(null, [trackConnected, level])!;

        Assert.Equal(expected, actual, precision: 2);
    }
}

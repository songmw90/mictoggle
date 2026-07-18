using Xunit;

namespace MicToggle.Tests;

public sealed class OutputVolumeStoreTests
{
    private static readonly Type? StoreType =
        Type.GetType("MicToggle.OutputVolumeStore, MicToggle", throwOnError: false);

    [Fact]
    public void Missing_or_malformed_settings_use_full_volume()
    {
        using var sandbox = new TemporaryDirectory();
        var path = Path.Combine(sandbox.Path, "volume.json");
        var store = CreateStore(path);

        Assert.Equal(100, Invoke<int>(store, "Load"));

        File.WriteAllText(path, "not-json");
        Assert.Equal(100, Invoke<int>(store, "Load"));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(37, 37)]
    [InlineData(105, 100)]
    public void Saved_volume_is_clamped_and_persisted(int requested, int expected)
    {
        using var sandbox = new TemporaryDirectory();
        var path = Path.Combine(sandbox.Path, "volume.json");
        var store = CreateStore(path);

        Invoke(store, "Save", requested);

        Assert.Equal(expected, Invoke<int>(store, "Load"));
    }

    private static object CreateStore(string path)
    {
        Assert.NotNull(StoreType);
        return Activator.CreateInstance(StoreType!, [path])!;
    }

    private static object? Invoke(object target, string method, params object[] arguments)
    {
        var methodInfo = target.GetType().GetMethod(method);
        Assert.NotNull(methodInfo);
        return methodInfo!.Invoke(target, arguments);
    }

    private static T Invoke<T>(object target, string method, params object[] arguments) =>
        (T)Invoke(target, method, arguments)!;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MicToggle.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

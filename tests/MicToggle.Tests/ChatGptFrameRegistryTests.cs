using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptFrameRegistryTests
{
    private static readonly Type RegistryType =
        Type.GetType("MicToggle.ChatGptFrameRegistry, MicToggle", throwOnError: false)!;

    [Fact]
    public void New_frames_bootstrap_only_known_exact_ChatGPT_origins()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;

        Invoke(registry, "Register", 7u);

        Assert.Equal(
            new[] { "https://chatgpt.com", "https://voice.chatgpt.com" },
            GetHostOrigins(registry, 7u));

        Invoke(registry, "SetNavigationUri", 7u, 7001UL, "https://example.com/");

        Assert.Empty(GetHostOrigins(registry, 7u));
    }

    [Fact]
    public void Registry_fans_out_only_to_exact_https_ChatGPT_frame_origins()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;

        Invoke(registry, "Register", 101u);
        Invoke(registry, "Register", 202u);
        Invoke(registry, "Register", 303u);

        Assert.Equal(
            "https://chatgpt.com",
            Invoke(registry, "SetNavigationUri", 101u, 1001UL, "https://chatgpt.com/c/one"));
        Assert.Equal(
            "https://voice.chatgpt.com",
            Invoke(registry, "SetNavigationUri", 202u, 2001UL, "https://voice.chatgpt.com/session"));
        Assert.Null(Invoke(
            registry,
            "SetNavigationUri",
            303u,
            3001UL,
            "https://chatgpt.com.example.com/steal"));

        Assert.Empty(GetTargets(registry));
        Assert.Equal(
            new[] { "https://chatgpt.com" },
            GetHostOrigins(registry, 101u));
        Assert.Equal(
            new[] { "https://voice.chatgpt.com" },
            GetHostOrigins(registry, 202u));

        Invoke(registry, "ObserveContentLoading", 101u, 1001UL, false);
        Invoke(registry, "ObserveContentLoading", 202u, 2001UL, false);
        Invoke(registry, "ObserveContentLoading", 303u, 3001UL, false);

        Assert.Equal(new uint[] { 101, 202 }, GetTargets(registry));
    }

    [Fact]
    public void Registry_prunes_destroyed_frames_and_restricts_new_frame_navigations()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;

        Invoke(registry, "Register", 11u);
        Invoke(registry, "Register", 22u);
        Invoke(registry, "SetNavigationUri", 11u, 1101UL, "https://chatgpt.com/");
        Invoke(registry, "SetNavigationUri", 22u, 2201UL, "https://voice.chatgpt.com/");
        Invoke(registry, "ObserveContentLoading", 11u, 1101UL, false);
        Invoke(registry, "ObserveContentLoading", 22u, 2201UL, false);
        Invoke(registry, "CompleteNavigation", 11u, 1101UL, true);
        Invoke(registry, "CompleteNavigation", 22u, 2201UL, true);
        Assert.Equal(new uint[] { 11, 22 }, GetTargets(registry));

        Assert.True((bool)Invoke(registry, "Remove", 11u)!);
        Assert.Equal(new uint[] { 22 }, GetTargets(registry));

        Assert.Null(Invoke(
            registry,
            "SetNavigationUri",
            22u,
            2202UL,
            "http://voice.chatgpt.com/"));
        Assert.Equal(
            new[] { "https://voice.chatgpt.com" },
            GetHostOrigins(registry, 22u));
        Assert.Equal(new uint[] { 22 }, GetTargets(registry));

        Invoke(registry, "CompleteNavigation", 22u, 2202UL, false);
        Assert.Equal(new uint[] { 22 }, GetTargets(registry));

        Invoke(registry, "SetNavigationUri", 22u, 2203UL, "http://voice.chatgpt.com/");
        Invoke(registry, "ObserveContentLoading", 22u, 2203UL, false);
        Assert.Empty(GetTargets(registry));
        Invoke(registry, "CompleteNavigation", 22u, 2203UL, true);
        Assert.Empty(GetTargets(registry));
    }

    [Fact]
    public void Older_canceled_completion_preserves_current_document_until_new_content()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;
        Invoke(registry, "Register", 42u);

        Invoke(registry, "SetNavigationUri", 42u, 100UL, "https://chatgpt.com/c/a");
        Invoke(registry, "ObserveContentLoading", 42u, 100UL, false);
        Assert.Equal(new uint[] { 42 }, GetTargets(registry));

        Invoke(registry, "SetNavigationUri", 42u, 200UL, "https://example.com/b");
        Assert.Equal(new uint[] { 42 }, GetTargets(registry));
        Assert.Equal(new[] { "https://chatgpt.com" }, GetHostOrigins(registry, 42u));

        Invoke(registry, "CompleteNavigation", 42u, 100UL, false);

        Assert.Equal(new uint[] { 42 }, GetTargets(registry));
        Assert.Equal(new[] { "https://chatgpt.com" }, GetHostOrigins(registry, 42u));

        Invoke(registry, "ObserveContentLoading", 42u, 200UL, false);

        Assert.Empty(GetTargets(registry));
        Assert.Empty(GetHostOrigins(registry, 42u));
    }

    [Fact]
    public void External_to_allowed_becomes_a_broadcast_target_only_at_content_loading()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;
        Invoke(registry, "Register", 77u);

        Invoke(registry, "SetNavigationUri", 77u, 1UL, "https://example.com/start");
        Invoke(registry, "ObserveContentLoading", 77u, 1UL, false);
        Invoke(registry, "CompleteNavigation", 77u, 1UL, true);
        Assert.Empty(GetTargets(registry));

        Invoke(registry, "SetNavigationUri", 77u, 2UL, "https://chatgpt.com/c/a");
        Assert.Empty(GetTargets(registry));
        Assert.Equal(new[] { "https://chatgpt.com" }, GetHostOrigins(registry, 77u));

        Invoke(registry, "ObserveContentLoading", 77u, 2UL, false);
        Assert.Equal(new uint[] { 77 }, GetTargets(registry));
    }

    [Fact]
    public void Allowed_to_external_stops_broadcasting_at_content_loading()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;
        Invoke(registry, "Register", 88u);

        Invoke(registry, "SetNavigationUri", 88u, 1UL, "https://chatgpt.com/current");
        Invoke(registry, "ObserveContentLoading", 88u, 1UL, false);
        Invoke(registry, "CompleteNavigation", 88u, 1UL, true);
        Assert.Equal(new uint[] { 88 }, GetTargets(registry));

        Invoke(registry, "SetNavigationUri", 88u, 2UL, "https://example.com/next");
        Assert.Equal(new uint[] { 88 }, GetTargets(registry));
        Assert.Equal(new[] { "https://chatgpt.com" }, GetHostOrigins(registry, 88u));

        Invoke(registry, "ObserveContentLoading", 88u, 2UL, false);
        Assert.Empty(GetTargets(registry));

        Invoke(registry, "CompleteNavigation", 88u, 2UL, true);
        Assert.Empty(GetTargets(registry));
    }

    [Fact]
    public void Error_page_content_is_not_broadcast_or_host_accessible()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;
        Invoke(registry, "Register", 99u);

        Invoke(registry, "SetNavigationUri", 99u, 10UL, "https://chatgpt.com/failing");
        Assert.Empty(GetTargets(registry));
        Assert.Equal(new[] { "https://chatgpt.com" }, GetHostOrigins(registry, 99u));

        Invoke(registry, "ObserveContentLoading", 99u, 10UL, true);

        Assert.Empty(GetTargets(registry));
        Assert.Empty(GetHostOrigins(registry, 99u));
        Invoke(registry, "CompleteNavigation", 99u, 10UL, false);
        Assert.Empty(GetTargets(registry));
    }

    [Fact]
    public void Redirect_updates_only_the_matching_navigation_before_content_loading()
    {
        Assert.NotNull(RegistryType);
        var registry = Activator.CreateInstance(RegistryType, nonPublic: true)!;
        Invoke(registry, "Register", 111u);

        Invoke(registry, "SetNavigationUri", 111u, 900UL, "https://chatgpt.com/redirect");
        Invoke(registry, "SetNavigationUri", 111u, 901UL, "https://files.chatgpt.com/other");
        Invoke(registry, "SetNavigationUri", 111u, 900UL, "https://voice.chatgpt.com/final");

        Assert.Empty(GetTargets(registry));
        Assert.Equal(
            new[] { "https://voice.chatgpt.com", "https://files.chatgpt.com" },
            GetHostOrigins(registry, 111u));

        Invoke(registry, "ObserveContentLoading", 111u, 900UL, false);
        Assert.Equal(new uint[] { 111 }, GetTargets(registry));

        Invoke(registry, "CompleteNavigation", 111u, 901UL, false);
        Assert.Equal(new[] { "https://voice.chatgpt.com" }, GetHostOrigins(registry, 111u));

        Invoke(registry, "CompleteNavigation", 111u, 900UL, false);
        Assert.Equal(new uint[] { 111 }, GetTargets(registry));
    }

    [Theory]
    [InlineData("https://chatgpt.com/", "https://chatgpt.com")]
    [InlineData("https://voice.chatgpt.com:443/call", "https://voice.chatgpt.com")]
    [InlineData("http://chatgpt.com/", null)]
    [InlineData("https://evilchatgpt.com/", null)]
    [InlineData("https://chatgpt.com.example.com/", null)]
    [InlineData("not a URI", null)]
    public void Allowed_origin_normalization_uses_the_shared_policy(
        string uri,
        string? expected)
    {
        Assert.NotNull(RegistryType);

        var actual = RegistryType.GetMethod(
            "GetAllowedOrigin",
            BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [uri]);

        Assert.Equal(expected, actual);
    }

    private static object? Invoke(object target, string method, params object[] arguments)
    {
        var methodInfo = RegistryType.GetMethod(method);
        Assert.NotNull(methodInfo);
        return methodInfo!.Invoke(target, arguments);
    }

    private static uint[] GetTargets(object registry) =>
        ((IEnumerable<uint>)Invoke(registry, "GetBroadcastTargets")!).ToArray();

    private static string[] GetHostOrigins(object registry, uint frameId) =>
        ((IEnumerable<string>)Invoke(registry, "GetHostOrigins", frameId)!).ToArray();
}

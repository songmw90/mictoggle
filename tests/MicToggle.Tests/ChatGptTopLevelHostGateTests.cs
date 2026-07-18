using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptTopLevelHostGateTests
{
    private static readonly Type GateType =
        Type.GetType("MicToggle.ChatGptTopLevelHostGate, MicToggle", throwOnError: false)!;

    [Fact]
    public void Content_boundary_exposes_state_only_to_allowed_ChatGPT_documents()
    {
        Assert.NotNull(GateType);
        var gate = Activator.CreateInstance(GateType, nonPublic: true)!;

        Assert.False(GetCurrentAllowed(gate));
        Start(gate, 1UL, "https://chatgpt.com/");
        Assert.True(Content(gate, 1UL, isErrorPage: false));
        Assert.True(GetCurrentAllowed(gate));
        Complete(gate, 1UL, isSuccess: true);

        Start(gate, 2UL, "https://example.com/");
        Assert.True(GetCurrentAllowed(gate));
        Assert.False(Content(gate, 2UL, isErrorPage: false));
        Assert.False(GetCurrentAllowed(gate));
        Complete(gate, 2UL, isSuccess: true);

        Start(gate, 3UL, "https://voice.chatgpt.com/call");
        Assert.True(Content(gate, 3UL, isErrorPage: false));
        Assert.True(GetCurrentAllowed(gate));
    }

    [Fact]
    public void Failed_navigation_without_content_keeps_the_current_origin_state()
    {
        Assert.NotNull(GateType);
        var gate = Activator.CreateInstance(GateType, nonPublic: true)!;
        EstablishCurrent(gate, 10UL, "https://chatgpt.com/");

        Start(gate, 20UL, "https://example.com/slow");
        Complete(gate, 20UL, isSuccess: false);

        Assert.True(GetCurrentAllowed(gate));
        Assert.Empty(GetPendingIds(gate));
    }

    [Fact]
    public void Redirects_and_interleaved_completions_are_correlated_by_navigation_id()
    {
        Assert.NotNull(GateType);
        var gate = Activator.CreateInstance(GateType, nonPublic: true)!;

        Start(gate, 100UL, "https://example.com/start");
        Start(gate, 100UL, "https://chatgpt.com/redirect");
        Start(gate, 200UL, "https://example.com/other");
        Assert.Equal(new ulong[] { 100, 200 }, GetPendingIds(gate));

        Assert.True(Content(gate, 100UL, isErrorPage: false));
        Complete(gate, 200UL, isSuccess: false);
        Assert.True(GetCurrentAllowed(gate));
        Assert.Equal(new ulong[] { 100 }, GetPendingIds(gate));

        Complete(gate, 100UL, isSuccess: true);
        Assert.Empty(GetPendingIds(gate));

        Start(gate, 300UL, "https://chatgpt.com/start");
        Start(gate, 300UL, "https://evil.example/final");
        Assert.False(Content(gate, 300UL, isErrorPage: false));
        Assert.False(GetCurrentAllowed(gate));
    }

    [Fact]
    public void Error_and_unknown_documents_never_expose_native_state()
    {
        Assert.NotNull(GateType);
        var gate = Activator.CreateInstance(GateType, nonPublic: true)!;

        Start(gate, 400UL, "https://chatgpt.com/");
        Assert.False(Content(gate, 400UL, isErrorPage: true));
        Assert.False(Content(gate, 999UL, isErrorPage: false));
        Assert.False(GetCurrentAllowed(gate));
    }

    private static void EstablishCurrent(object gate, ulong navigationId, string uri)
    {
        Start(gate, navigationId, uri);
        Content(gate, navigationId, isErrorPage: false);
        Complete(gate, navigationId, isSuccess: true);
    }

    private static void Start(object gate, ulong navigationId, string uri) =>
        Invoke(gate, "ObserveNavigationStarting", navigationId, uri);

    private static bool Content(object gate, ulong navigationId, bool isErrorPage) =>
        (bool)Invoke(gate, "ObserveContentLoading", navigationId, isErrorPage)!;

    private static void Complete(object gate, ulong navigationId, bool isSuccess) =>
        Invoke(gate, "CompleteNavigation", navigationId, isSuccess);

    private static bool GetCurrentAllowed(object gate) =>
        (bool)GateType.GetProperty("CurrentOriginAllowed")!.GetValue(gate)!;

    private static ulong[] GetPendingIds(object gate) =>
        ((IEnumerable<ulong>)Invoke(gate, "GetPendingNavigationIds")!).ToArray();

    private static object? Invoke(object target, string method, params object[] arguments)
    {
        var methodInfo = GateType.GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(methodInfo);
        return methodInfo!.Invoke(target, arguments);
    }
}

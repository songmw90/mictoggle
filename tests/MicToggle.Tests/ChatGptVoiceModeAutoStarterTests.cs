using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptVoiceModeAutoStarterTests
{
    private static readonly Type? StarterType =
        Type.GetType("MicToggle.ChatGptVoiceModeAutoStarter, MicToggle", throwOnError: false);

    [Fact]
    public void Successful_attempt_is_single_flight_until_explicitly_rearmed()
    {
        var starter = CreateStarter();

        Assert.True(Invoke<bool>(starter, "TryBegin"));
        Assert.False(Invoke<bool>(starter, "TryBegin"));

        Invoke(starter, "Complete", true);

        Assert.False(Invoke<bool>(starter, "TryBegin"));
        Assert.True(Invoke<bool>(starter, "Rearm"));
        Assert.True(Invoke<bool>(starter, "TryBegin"));
    }

    [Fact]
    public void Running_attempt_cannot_be_rearmed()
    {
        var starter = CreateStarter();

        Assert.True(Invoke<bool>(starter, "TryBegin"));

        Assert.False(Invoke<bool>(starter, "Rearm"));
        Assert.False(Invoke<bool>(starter, "TryBegin"));
    }

    [Fact]
    public void Missing_button_allows_a_later_navigation_to_retry()
    {
        var starter = CreateStarter();

        Assert.True(Invoke<bool>(starter, "TryBegin"));
        Invoke(starter, "Complete", false);

        Assert.True(Invoke<bool>(starter, "TryBegin"));
    }

    [Fact]
    public void Replacement_starter_allows_recovery_to_start_voice_mode_again()
    {
        var starter = CreateStarter();

        Assert.True(Invoke<bool>(starter, "TryBegin"));
        Invoke(starter, "Complete", true);
        var replacement = CreateStarter();

        Assert.True(Invoke<bool>(replacement, "TryBegin"));
    }

    [Theory]
    [InlineData("{\"started\":true,\"clicked\":true}", true)]
    [InlineData("{\"started\":true,\"clicked\":false}", true)]
    [InlineData("{\"started\":false}", false)]
    [InlineData("null", false)]
    [InlineData("not-json", false)]
    public void Script_result_requires_a_true_started_property(string json, bool expected)
    {
        var type = RequireStarterType();
        var method = type.GetMethod(
            "DidStart",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal(expected, method!.Invoke(null, [json]));
    }

    [Fact]
    public void Script_matches_voice_start_but_explicitly_excludes_dictation()
    {
        var type = RequireStarterType();
        var property = type.GetProperty(
            "TryStartScript",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(property);
        var script = Assert.IsType<string>(property!.GetValue(null));

        Assert.Contains("voice", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("음성", script, StringComparison.Ordinal);
        Assert.Contains("받아쓰기", script, StringComparison.Ordinal);
        Assert.Contains("dictation", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("button.click()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_script_only_clicks_an_active_voice_end_button()
    {
        var type = RequireStarterType();
        var property = type.GetProperty(
            "TryStopScript",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(property);
        var script = Assert.IsType<string>(property!.GetValue(null));

        Assert.Contains("voice", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("끝내기", script, StringComparison.Ordinal);
        Assert.Contains("dictation", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("button.click()", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"stopped\":true}", true)]
    [InlineData("{\"stopped\":false}", false)]
    [InlineData("null", false)]
    public void Stop_script_result_requires_a_true_stopped_property(string json, bool expected)
    {
        var type = RequireStarterType();
        var method = type.GetMethod(
            "DidStop",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal(expected, method!.Invoke(null, [json]));
    }

    private static object CreateStarter()
    {
        var type = RequireStarterType();
        return Activator.CreateInstance(type)!;
    }

    private static Type RequireStarterType()
    {
        Assert.NotNull(StarterType);
        return StarterType!;
    }

    private static object? Invoke(object target, string method, params object[] arguments)
    {
        var methodInfo = target.GetType().GetMethod(method);
        Assert.NotNull(methodInfo);
        return methodInfo!.Invoke(target, arguments);
    }

    private static T Invoke<T>(object target, string method, params object[] arguments) =>
        (T)Invoke(target, method, arguments)!;
}

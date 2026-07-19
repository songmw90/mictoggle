using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
    public void Recovery_scripts_handle_live_voice_end_and_loading_cancel_states()
    {
        var type = RequireStarterType();
        var stopProperty = type.GetProperty(
            "TryStopScript",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var readyProperty = type.GetProperty(
            "ReadyToStartScript",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(stopProperty);
        Assert.NotNull(readyProperty);
        var stopScript = Assert.IsType<string>(stopProperty!.GetValue(null));
        var readyScript = Assert.IsType<string>(readyProperty!.GetValue(null));

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            stopScript,
            readyScript,
        })));

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "-e", RecoveryHarness, payload },
        })!;

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Node voice recovery harness failed.{Environment.NewLine}{output}{error}");

        Assert.Contains("cancel loading", stopScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("로딩 취소", stopScript, StringComparison.Ordinal);
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

    private const string RecoveryHarness = """
        const assert = require('node:assert/strict');
        const vm = require('node:vm');
        const payload = JSON.parse(Buffer.from(process.argv[1], 'base64').toString('utf8'));

        function execute(script, labels) {
          const buttons = labels.map(label => ({
            label,
            disabled: false,
            clicked: false,
            getAttribute(name) {
              return name === 'aria-label' ? this.label : null;
            },
            getBoundingClientRect() {
              return { width: 40, height: 40 };
            },
            click() {
              this.clicked = true;
            }
          }));
          const context = {
            document: {
              querySelectorAll(selector) {
                assert.equal(selector, 'button');
                return buttons;
              }
            },
            window: {
              getComputedStyle() {
                return { display: 'block', visibility: 'visible' };
              }
            }
          };
          return { result: vm.runInNewContext(script, context), buttons };
        }

        let execution = execute(payload.stopScript, ['Voice 끝내기']);
        assert.equal(execution.result.stopped, true);
        assert.equal(execution.buttons[0].clicked, true);

        execution = execute(payload.stopScript, ['로딩 취소']);
        assert.equal(execution.result.stopped, true);
        assert.equal(execution.buttons[0].clicked, true);

        execution = execute(payload.stopScript, ['Cancel loading']);
        assert.equal(execution.result.stopped, true);
        assert.equal(execution.buttons[0].clicked, true);

        execution = execute(payload.stopScript, ['받아쓰기 종료']);
        assert.equal(execution.result.stopped, false);
        assert.equal(execution.buttons[0].clicked, false);

        execution = execute(payload.readyScript, ['Voice 끝내기']);
        assert.equal(execution.result.ready, false);

        execution = execute(payload.readyScript, ['Voice 시작']);
        assert.equal(execution.result.ready, true);
        """;
}

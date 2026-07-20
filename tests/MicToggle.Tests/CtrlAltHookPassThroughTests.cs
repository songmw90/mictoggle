using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace MicToggle.Tests;

public sealed class CtrlAltTriggerTests
{
    [Fact]
    public void Trigger_polls_modifier_state_without_installing_a_global_keyboard_hook()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MicToggle",
            "CtrlAltHook.cs"));

        Assert.Contains("GetAsyncKeyState", source, StringComparison.Ordinal);
        Assert.Contains("_stop.Wait(PollIntervalMilliseconds)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowsHookEx", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WH_KEYBOARD_LL", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreadPool.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentQueue", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Trigger_reports_press_and_release_from_polled_left_ctrl_alt_state()
    {
        var hookType = Type.GetType("MicToggle.CtrlAltHook, MicToggle", throwOnError: true)!;
        var keys = new ConcurrentDictionary<Keys, bool>();
        var queued = new ConcurrentQueue<Action>();
        Action<Action> dispatch = action => queued.Enqueue(action);
        Func<Keys, bool> isKeyDown = key => keys.TryGetValue(key, out var down) && down;
        var constructor = hookType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Action<Action>), typeof(Func<Keys, bool>)],
            modifiers: null);
        Assert.NotNull(constructor);

        using var trigger = (IDisposable)constructor!.Invoke([dispatch, isKeyDown]);
        var pressedCount = 0;
        var releasedCount = 0;
        hookType.GetEvent("Pressed")!.AddEventHandler(
            trigger,
            new EventHandler((_, _) => pressedCount++));
        hookType.GetEvent("Released")!.AddEventHandler(
            trigger,
            new EventHandler((_, _) => releasedCount++));
        hookType.GetMethod("Start")!.Invoke(trigger, null);

        keys[Keys.LControlKey] = true;
        keys[Keys.LMenu] = true;
        WaitForQueuedAction(queued)();

        Assert.Equal(1, pressedCount);
        Assert.Equal(0, releasedCount);

        keys[Keys.LMenu] = false;
        WaitForQueuedAction(queued)();

        Assert.Equal(1, pressedCount);
        Assert.Equal(1, releasedCount);
    }

    private static Action WaitForQueuedAction(ConcurrentQueue<Action> queued)
    {
        Assert.True(SpinWait.SpinUntil(
            () => !queued.IsEmpty,
            TimeSpan.FromSeconds(1)));
        Assert.True(queued.TryDequeue(out var action));
        return action;
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

        throw new DirectoryNotFoundException("MicToggle repository root was not found.");
    }
}

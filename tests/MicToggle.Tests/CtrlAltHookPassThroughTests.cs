using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Xunit;

namespace MicToggle.Tests;

public sealed class CtrlAltHookPassThroughTests
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    [Fact]
    public void CtrlAltChordIsPassedToTheNextHook()
    {
        var hookType = Type.GetType("MicToggle.CtrlAltHook, MicToggle", throwOnError: true)!;
        var forwarded = new List<(IntPtr Hook, int Code, IntPtr Message, IntPtr Data)>();
        var expectedResult = new IntPtr(0x5A5A);
        Func<IntPtr, int, IntPtr, IntPtr, IntPtr> callNext = (hook, code, message, data) =>
        {
            forwarded.Add((hook, code, message, data));
            return expectedResult;
        };
        var constructor = hookType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<IntPtr, int, IntPtr, IntPtr, IntPtr>)],
            modifiers: null);
        Assert.NotNull(constructor);

        using var hook = (IDisposable)constructor!.Invoke([callNext]);
        var callback = hookType.GetMethod("HookCallback", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(expectedResult, SendKey(callback, hook, Keys.LControlKey, WmKeyDown));
        Assert.Equal(expectedResult, SendKey(callback, hook, Keys.LMenu, WmSysKeyDown));
        Assert.Collection(
            forwarded,
            call =>
            {
                Assert.Equal(IntPtr.Zero, call.Hook);
                Assert.Equal(0, call.Code);
                Assert.Equal(new IntPtr(WmKeyDown), call.Message);
                Assert.NotEqual(IntPtr.Zero, call.Data);
            },
            call =>
            {
                Assert.Equal(IntPtr.Zero, call.Hook);
                Assert.Equal(0, call.Code);
                Assert.Equal(new IntPtr(WmSysKeyDown), call.Message);
                Assert.NotEqual(IntPtr.Zero, call.Data);
            });
    }

    private static IntPtr SendKey(MethodInfo callback, object hook, Keys key, int message)
    {
        var keyData = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(keyData, (int)key);
            return (IntPtr)callback.Invoke(hook, [0, new IntPtr(message), keyData])!;
        }
        finally
        {
            Marshal.FreeHGlobal(keyData);
        }
    }
}

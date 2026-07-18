using System.Runtime.InteropServices;
using System.Threading;

namespace MicToggle;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class MicrophoneStateHost
{
    private int _enabled;
    private int _accessAllowed;

    public bool Enabled =>
        Volatile.Read(ref _accessAllowed) != 0
        && Volatile.Read(ref _enabled) != 0;

    internal void SetEnabled(bool enabled) =>
        Volatile.Write(ref _enabled, enabled ? 1 : 0);

    internal void SetAccessAllowed(bool allowed) =>
        Volatile.Write(ref _accessAllowed, allowed ? 1 : 0);
}

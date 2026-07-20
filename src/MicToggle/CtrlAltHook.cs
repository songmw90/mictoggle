using System.Runtime.InteropServices;

namespace MicToggle;

internal sealed class CtrlAltHook : IDisposable
{
    private const int PollIntervalMilliseconds = 5;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly Action<Action> _dispatch;
    private readonly Func<Keys, bool> _isKeyDown;
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _pollThread;
    private bool _chordActive;
    private int _disposed;

    internal CtrlAltHook(Action<Action> dispatch)
        : this(dispatch, IsNativeKeyDown)
    {
    }

    internal CtrlAltHook(Action<Action> dispatch, Func<Keys, bool> isKeyDown)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isKeyDown = isKeyDown ?? throw new ArgumentNullException(nameof(isKeyDown));
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pollThread is not null)
        {
            return;
        }

        _pollThread = new Thread(RunPollingLoop)
        {
            IsBackground = true,
            Name = "MicToggle Ctrl+Alt trigger",
        };
        _pollThread.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Set();
        var pollThread = _pollThread;
        if (pollThread is not null &&
            pollThread.IsAlive &&
            Thread.CurrentThread != pollThread)
        {
            pollThread.Join(ShutdownTimeout);
        }

        if (pollThread is null || !pollThread.IsAlive)
        {
            _stop.Dispose();
        }
    }

    private void RunPollingLoop()
    {
        while (!_stop.IsSet)
        {
            UpdateChordState(IsChordPressed(_isKeyDown));
            if (_stop.Wait(PollIntervalMilliseconds))
            {
                return;
            }
        }
    }

    private static bool IsChordPressed(Func<Keys, bool> isKeyDown)
    {
        return isKeyDown(Keys.LControlKey) &&
            (isKeyDown(Keys.LMenu) || isKeyDown(Keys.RMenu));
    }

    private static bool IsNativeKeyDown(Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private void UpdateChordState(bool chordPressed)
    {
        if (chordPressed == _chordActive)
        {
            return;
        }

        _chordActive = chordPressed;
        DispatchEvent(chordPressed ? Pressed : Released);
    }

    private void DispatchEvent(EventHandler? handler)
    {
        if (handler is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _dispatch(() =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    handler(this, EventArgs.Empty);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The UI dispatcher can disappear while the application is shutting down.
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

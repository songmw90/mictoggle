using System.Runtime.InteropServices;

namespace MicToggle;

internal sealed class MicrophoneActivityOverlay : IDisposable
{
    private const int EdgeThickness = 4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly List<ActivityEdgeWindow> _edges = [];
    private int _activeEdgeCount;
    private bool _visible;

    internal static Color AccentColor { get; } = Color.FromArgb(76, 217, 130);

    public MicrophoneActivityOverlay()
    {
        EnsureEdgeCount(Screen.AllScreens.Length * 4);
    }

    public void ShowForAllScreens()
    {
        var edgeBounds = CreateAllEdgeBounds(
            Screen.AllScreens.Select(screen => screen.Bounds).ToArray(),
            EdgeThickness);
        EnsureEdgeCount(edgeBounds.Length);

        var opacity = CalculateOpacity(trackConnected: false, level: 0);
        ShowEdges(edgeBounds, opacity);
        HideEdges(edgeBounds.Length, _edges.Count - edgeBounds.Length);

        _activeEdgeCount = edgeBounds.Length;
        _visible = _activeEdgeCount > 0;
    }

    public void UpdateActivity(bool trackConnected, double level)
    {
        if (!_visible)
        {
            return;
        }

        var opacity = CalculateOpacity(trackConnected, level);
        for (var index = 0; index < _activeEdgeCount; index++)
        {
            _edges[index].SetOpacity(opacity);
        }
    }

    public void Hide()
    {
        _visible = false;
        _activeEdgeCount = 0;
        HideEdges(0, _edges.Count);
    }

    public void Dispose()
    {
        _visible = false;
        _activeEdgeCount = 0;
        foreach (var edge in _edges)
        {
            edge.Dispose();
        }
    }

    internal static Rectangle[] CreateAllEdgeBounds(
        Rectangle[] screenBounds,
        int thickness)
    {
        ArgumentNullException.ThrowIfNull(screenBounds);

        return screenBounds
            .SelectMany(bounds => CreateEdgeBounds(bounds, thickness))
            .ToArray();
    }

    internal static Rectangle[] CreateEdgeBounds(Rectangle screenBounds, int thickness)
    {
        if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screenBounds));
        }

        if (thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness));
        }

        var edgeThickness = Math.Min(
            thickness,
            Math.Max(1, Math.Min(screenBounds.Width, screenBounds.Height) / 2));
        var verticalHeight = Math.Max(0, screenBounds.Height - (edgeThickness * 2));

        return
        [
            new Rectangle(
                screenBounds.Left,
                screenBounds.Top,
                screenBounds.Width,
                edgeThickness),
            new Rectangle(
                screenBounds.Left,
                screenBounds.Bottom - edgeThickness,
                screenBounds.Width,
                edgeThickness),
            new Rectangle(
                screenBounds.Left,
                screenBounds.Top + edgeThickness,
                edgeThickness,
                verticalHeight),
            new Rectangle(
                screenBounds.Right - edgeThickness,
                screenBounds.Top + edgeThickness,
                edgeThickness,
                verticalHeight),
        ];
    }

    internal static double CalculateOpacity(bool trackConnected, double level)
    {
        if (!trackConnected)
        {
            return 0.42;
        }

        return 0.68 + (Math.Clamp(level, 0, 1) * 0.28);
    }

    private void EnsureEdgeCount(int count)
    {
        while (_edges.Count < count)
        {
            var edge = new ActivityEdgeWindow(AccentColor);
            edge.PrepareHandle();
            _edges.Add(edge);
        }
    }

    private void ShowEdges(Rectangle[] bounds, double opacity)
    {
        for (var index = 0; index < bounds.Length; index++)
        {
            _edges[index].SetOpacity(opacity);
        }

        var deferred = BeginDeferWindowPos(bounds.Length);
        if (deferred != IntPtr.Zero)
        {
            for (var index = 0; index < bounds.Length; index++)
            {
                var edgeBounds = bounds[index];
                deferred = DeferWindowPos(
                    deferred,
                    _edges[index].WindowHandle,
                    HwndTopmost,
                    edgeBounds.Left,
                    edgeBounds.Top,
                    edgeBounds.Width,
                    edgeBounds.Height,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
                if (deferred == IntPtr.Zero)
                {
                    break;
                }
            }

            if (deferred != IntPtr.Zero && EndDeferWindowPos(deferred))
            {
                return;
            }
        }

        for (var index = 0; index < bounds.Length; index++)
        {
            _edges[index].ShowEdge(bounds[index], opacity);
        }
    }

    private void HideEdges(int startIndex, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var deferred = BeginDeferWindowPos(count);
        if (deferred != IntPtr.Zero)
        {
            for (var index = startIndex; index < startIndex + count; index++)
            {
                deferred = DeferWindowPos(
                    deferred,
                    _edges[index].WindowHandle,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOSIZE |
                    SWP_NOMOVE |
                    SWP_NOZORDER |
                    SWP_NOACTIVATE |
                    SWP_HIDEWINDOW);
                if (deferred == IntPtr.Zero)
                {
                    break;
                }
            }

            if (deferred != IntPtr.Zero && EndDeferWindowPos(deferred))
            {
                return;
            }
        }

        for (var index = startIndex; index < startIndex + count; index++)
        {
            _edges[index].HideEdge();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginDeferWindowPos(int windowCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DeferWindowPos(
        IntPtr deferredWindowPosition,
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDeferWindowPos(IntPtr deferredWindowPosition);

    private sealed class ActivityEdgeWindow : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SW_HIDE = 0;
        private const uint LWA_ALPHA = 0x00000002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HwndTopmost = new(-1);
        private byte? _opacity;

        public IntPtr WindowHandle => Handle;

        public ActivityEdgeWindow(Color color)
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = color;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_LAYERED |
                    WS_EX_NOACTIVATE |
                    WS_EX_TRANSPARENT |
                    WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        public void PrepareHandle()
        {
            _ = Handle;
        }

        public void ShowEdge(Rectangle bounds, double opacity)
        {
            SetOpacity(opacity);

            _ = SetWindowPos(
                Handle,
                HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public void SetOpacity(double opacity)
        {
            var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * byte.MaxValue);
            if (_opacity == alpha)
            {
                return;
            }

            if (SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA))
            {
                _opacity = alpha;
            }
        }

        public void HideEdge()
        {
            if (IsHandleCreated)
            {
                _ = ShowWindow(Handle, SW_HIDE);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_NCHITTEST)
            {
                message.Result = new IntPtr(HTTRANSPARENT);
                return;
            }

            base.WndProc(ref message);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr windowHandle,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}

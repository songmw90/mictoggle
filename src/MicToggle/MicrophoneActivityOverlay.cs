using System.Runtime.InteropServices;

namespace MicToggle;

internal sealed class MicrophoneActivityOverlay : IDisposable
{
    private const int EdgeThickness = 4;

    private readonly ActivityEdgeWindow[] _edges =
    [
        new ActivityEdgeWindow(),
        new ActivityEdgeWindow(),
        new ActivityEdgeWindow(),
        new ActivityEdgeWindow(),
    ];
    private bool _visible;

    internal static Color AccentColor { get; } = Color.FromArgb(76, 217, 130);

    public void ShowForForegroundScreen()
    {
        var foregroundWindow = GetForegroundWindow();
        var screen = foregroundWindow == IntPtr.Zero
            ? Screen.PrimaryScreen
            : Screen.FromHandle(foregroundWindow);
        screen ??= Screen.FromPoint(Cursor.Position);

        var edgeBounds = CreateEdgeBounds(screen.Bounds, EdgeThickness);
        var opacity = CalculateOpacity(trackConnected: false, level: 0);
        for (var index = 0; index < _edges.Length; index++)
        {
            _edges[index].ShowEdge(edgeBounds[index], AccentColor, opacity);
        }

        _visible = true;
    }

    public void UpdateActivity(bool trackConnected, double level)
    {
        if (!_visible)
        {
            return;
        }

        var opacity = CalculateOpacity(trackConnected, level);
        foreach (var edge in _edges)
        {
            edge.Opacity = opacity;
        }
    }

    public void Hide()
    {
        _visible = false;
        foreach (var edge in _edges)
        {
            edge.Hide();
        }
    }

    public void Dispose()
    {
        _visible = false;
        foreach (var edge in _edges)
        {
            edge.Dispose();
        }
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private sealed class ActivityEdgeWindow : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HwndTopmost = new(-1);

        public ActivityEdgeWindow()
        {
            AutoScaleMode = AutoScaleMode.None;
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
                parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        public void ShowEdge(Rectangle bounds, Color color, double opacity)
        {
            Bounds = bounds;
            BackColor = color;
            Opacity = opacity;
            if (!Visible)
            {
                Show();
            }

            _ = SetWindowPos(
                Handle,
                HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
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

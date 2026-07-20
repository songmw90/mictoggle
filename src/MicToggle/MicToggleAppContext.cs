namespace MicToggle;

internal sealed class MicToggleAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayIconAsset;
    private readonly CtrlAltHook _hook = new();
    private readonly ChatGptWindow _window;
    private readonly MicrophoneActivityOverlay _activityOverlay = new();
    private bool _isHolding;

    public MicToggleAppContext(bool startHidden)
    {
        _window = new ChatGptWindow(startHidden);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show MicToggle", null, (_, _) => _window.ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIconAsset = CreateTrayIcon();
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _trayIconAsset,
            Text = "MicToggle - PTT released",
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => _window.ShowWindow();
        _window.MicrophoneActivityChanged += HandleMicrophoneActivityChanged;

        _hook.Pressed += async (_, _) => await SetHoldingAsync(true);
        _hook.Released += async (_, _) => await SetHoldingAsync(false);
        _window.Show();
        try
        {
            _hook.Start();
        }
        catch (Exception ex)
        {
            ShowBalloon("MicToggle hook failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private static Icon CreateTrayIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? SystemIcons.Information;
    }

    private async Task SetHoldingAsync(bool isHolding)
    {
        if (_isHolding == isHolding)
        {
            return;
        }

        _isHolding = isHolding;
        _trayIcon.Text = $"MicToggle - PTT {(isHolding ? "held" : "released")}";
        try
        {
            if (isHolding)
            {
                _activityOverlay.ShowForForegroundScreen();
            }
            else
            {
                _activityOverlay.Hide();
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("MicToggle indicator failed", ex.Message, ToolTipIcon.Error);
        }

        try
        {
            await _window.SetMicrophoneEnabledAsync(isHolding);
        }
        catch (Exception ex)
        {
            ShowBalloon("MicToggle error", ex.Message, ToolTipIcon.Error);
        }
    }

    private void HandleMicrophoneActivityChanged(
        object? sender,
        MicrophoneActivityEventArgs args)
    {
        if (!_isHolding)
        {
            return;
        }

        _activityOverlay.UpdateActivity(
            args.Enabled && args.TrackCount > 0,
            args.Level);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(2500);
    }

    protected override void ExitThreadCore()
    {
        try
        {
            _window.MicrophoneActivityChanged -= HandleMicrophoneActivityChanged;
            _activityOverlay.Dispose();
        }
        finally
        {
            try
            {
                _hook.Dispose();
            }
            finally
            {
                try
                {
                    _window.ExitApplication();
                }
                finally
                {
                    try
                    {
                        _trayIcon.Visible = false;
                    }
                    finally
                    {
                        try
                        {
                            _trayIcon.Dispose();
                        }
                        finally
                        {
                            try
                            {
                                _trayIconAsset.Dispose();
                            }
                            finally
                            {
                                base.ExitThreadCore();
                            }
                        }
                    }
                }
            }
        }
    }
}

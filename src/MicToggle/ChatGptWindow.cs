using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MicToggle;

internal sealed class ChatGptWindow : Form
{
    private const string HomeAddress = "https://chatgpt.com/";
    private const string BackGlyph = "\uE72B";
    private const string ReloadGlyph = "\uE72C";
    private const string HomeGlyph = "\uE80F";
    private const string MicrophoneGlyph = "\uE720";
    private const string VolumeGlyph = "\uE767";
    private const string MutedVolumeGlyph = "\uE74F";
    private const int VoiceModeStartAttempts = 40;
    private const int VoiceModeStartRetryDelayMilliseconds = 250;
    private const int VoiceIdleRestartIntervalMilliseconds = 10 * 60 * 1000;

    private static readonly Color WindowBackground = Color.FromArgb(17, 19, 21);
    private static readonly Color HeaderBackground = Color.FromArgb(24, 26, 29);
    private static readonly Color StatusBackground = Color.FromArgb(20, 22, 25);
    private static readonly Color HoverBackground = Color.FromArgb(42, 45, 49);
    private static readonly Color PressedBackground = Color.FromArgb(52, 56, 61);
    private static readonly Color BorderColor = Color.FromArgb(48, 52, 58);
    private static readonly Color PrimaryText = Color.FromArgb(242, 244, 247);
    private static readonly Color SecondaryText = Color.FromArgb(164, 171, 181);
    private static readonly Color AccentColor = Color.FromArgb(76, 217, 130);
    private static readonly Color BusyColor = Color.FromArgb(245, 185, 66);
    private static readonly Color ErrorColor = Color.FromArgb(255, 107, 107);

    private readonly Panel _webViewHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = WindowBackground,
    };
    private readonly Button _backButton;
    private readonly Button _reloadButton;
    private readonly Button _homeButton;
    private readonly Button _retryButton;
    private readonly Label _pttIconLabel = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = SecondaryText,
        Text = MicrophoneGlyph,
        TextAlign = ContentAlignment.MiddleCenter,
    };
    private readonly Label _pttStatusLabel = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = SecondaryText,
        Text = "Mic off",
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = SecondaryText,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Label _outputVolumeIconLabel = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe Fluent Icons", 10F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = SecondaryText,
        Text = VolumeGlyph,
        TextAlign = ContentAlignment.MiddleCenter,
    };
    private readonly TrackBar _outputVolumeSlider = new()
    {
        AccessibleName = "Output volume",
        AutoSize = false,
        BackColor = StatusBackground,
        Dock = DockStyle.Fill,
        LargeChange = 10,
        Maximum = 100,
        Minimum = 0,
        SmallChange = 5,
        TickStyle = TickStyle.None,
    };
    private readonly Label _outputVolumeValueLabel = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = SecondaryText,
        Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleRight,
    };
    private readonly StatusDot _statusDot = new() { Dock = DockStyle.Fill };
    private readonly ToolTip _toolTip = new()
    {
        AutomaticDelay = 350,
        AutoPopDelay = 5000,
        ReshowDelay = 100,
        ShowAlways = true,
    };
    private readonly Icon? _windowIcon;
    private readonly SemaphoreSlim _microphoneStateGate = new(1, 1);
    private readonly ChatGptWindowState _state = new();
    private readonly MicrophoneStateHost _microphoneStateHost = new();
    private readonly ChatGptFrameRegistry _frameRegistry = new();
    private readonly ChatGptTopLevelHostGate _topLevelHostGate = new();
    private readonly Dictionary<uint, CoreWebView2Frame> _frames = [];
    private readonly WebViewAudioVolumeController _audioVolumeController = new(Environment.ProcessId);
    private readonly OutputVolumeStore _outputVolumeStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MicToggle",
        "output-volume.json"));
    private readonly System.Windows.Forms.Timer _audioVolumeRefreshTimer = new()
    {
        Interval = 1000,
    };
    private readonly System.Windows.Forms.Timer _voiceIdleTimer = new()
    {
        Interval = VoiceIdleRestartIntervalMilliseconds,
    };

    private ChatGptVoiceModeAutoStarter _voiceModeAutoStarter = new();
    private WebView2? _webView;
    private bool _allowClose;

    public ChatGptWindow()
    {
        Text = "MicToggle";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1050, 760);
        MinimumSize = new Size(640, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _windowIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_windowIcon is not null)
        {
            Icon = _windowIcon;
        }

        _backButton = CreateNavigationButton(BackGlyph, "Back", enabled: false);
        _reloadButton = CreateNavigationButton(ReloadGlyph, "Reload", enabled: false);
        _homeButton = CreateNavigationButton(HomeGlyph, "Home", enabled: false);
        _retryButton = CreateNavigationButton(ReloadGlyph, "Retry", visible: false);
        _outputVolumeSlider.Value = _outputVolumeStore.Load();
        UpdateOutputVolumePresentation();

        Controls.Add(_webViewHost);
        Controls.Add(CreateStatusBar());
        Controls.Add(CreateHeader());
        AttachReplacementWebView();

        _backButton.Click += (_, _) => GoBack();
        _reloadButton.Click += (_, _) => Reload();
        _homeButton.Click += (_, _) => NavigateHome();
        _retryButton.Click += async (_, _) => await RetryWebViewInitializationAsync();
        _outputVolumeSlider.ValueChanged += HandleOutputVolumeChanged;
        _audioVolumeRefreshTimer.Tick += HandleAudioVolumeRefresh;
        _voiceIdleTimer.Tick += HandleVoiceIdleElapsed;
        FormClosing += HandleFormClosing;
        Shown += async (_, _) =>
        {
            if (_webView is not null)
            {
                await InitializeWebViewAsync(_webView);
            }
        };

        SetStatus("Starting ChatGPT...");
        _audioVolumeRefreshTimer.Start();
        _voiceIdleTimer.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.ApplyDarkTitleBar(Handle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _audioVolumeRefreshTimer.Stop();
            _audioVolumeRefreshTimer.Dispose();
            _voiceIdleTimer.Stop();
            _voiceIdleTimer.Dispose();
            TrySaveOutputVolume(reportErrors: false);
            _toolTip.Dispose();
            if (_windowIcon is not null)
            {
                _windowIcon.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private Control CreateHeader()
    {
        var frame = new Panel
        {
            Dock = DockStyle.Top,
            Height = 45,
            BackColor = BorderColor,
            Padding = new Padding(0, 0, 0, 1),
        };
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = HeaderBackground,
            ColumnCount = 4,
            RowCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Padding = new Padding(10, 4, 12, 4),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 17F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = HeaderBackground,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false,
        };
        navigation.Controls.AddRange([_backButton, _reloadButton, _homeButton, _retryButton]);

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BorderColor,
            Margin = new Padding(8, 7, 8, 7),
        };
        var pttSurface = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = HeaderBackground,
            ColumnCount = 2,
            RowCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = new Padding(4, 0, 0, 0),
            Padding = Padding.Empty,
        };
        pttSurface.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
        pttSurface.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pttSurface.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        pttSurface.Controls.Add(_pttIconLabel, 0, 0);
        pttSurface.Controls.Add(_pttStatusLabel, 1, 0);

        header.Controls.Add(navigation, 0, 0);
        header.Controls.Add(separator, 1, 0);
        header.Controls.Add(pttSurface, 2, 0);
        frame.Controls.Add(header);
        return frame;
    }

    private Control CreateStatusBar()
    {
        var frame = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 33,
            BackColor = BorderColor,
            Padding = new Padding(0, 1, 0, 0),
        };
        var statusBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = StatusBackground,
            ColumnCount = 5,
            RowCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Padding = new Padding(10, 0, 12, 0),
        };
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46F));
        statusBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        statusBar.Controls.Add(_statusDot, 0, 0);
        statusBar.Controls.Add(_statusLabel, 1, 0);
        statusBar.Controls.Add(_outputVolumeIconLabel, 2, 0);
        statusBar.Controls.Add(_outputVolumeSlider, 3, 0);
        statusBar.Controls.Add(_outputVolumeValueLabel, 4, 0);
        _toolTip.SetToolTip(_outputVolumeIconLabel, "ChatGPT output volume");
        _toolTip.SetToolTip(_outputVolumeSlider, "ChatGPT output volume");
        _toolTip.SetToolTip(_outputVolumeValueLabel, "ChatGPT output volume");
        frame.Controls.Add(statusBar);
        return frame;
    }

    private Button CreateNavigationButton(
        string glyph,
        string accessibleName,
        bool enabled = true,
        bool visible = true)
    {
        var button = new Button
        {
            AccessibleName = accessibleName,
            BackColor = HeaderBackground,
            Cursor = Cursors.Hand,
            Enabled = enabled,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 4, 0),
            Size = new Size(36, 36),
            TabStop = true,
            Text = glyph,
            UseVisualStyleBackColor = false,
            Visible = visible,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = HoverBackground;
        button.FlatAppearance.MouseDownBackColor = PressedBackground;
        _toolTip.SetToolTip(button, accessibleName);
        return button;
    }

    public Task SetMicrophoneEnabledAsync(bool enabled)
    {
        _microphoneStateHost.SetEnabled(enabled);
        _state.SetDesiredMicrophoneEnabled(enabled);
        return DrainMicrophoneStateAsync();
    }

    public void ShowWindow()
    {
        if (IsDisposed)
        {
            return;
        }

        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    public void ExitApplication()
    {
        if (IsDisposed)
        {
            return;
        }

        _microphoneStateHost.SetEnabled(false);
        _state.SetDesiredMicrophoneEnabled(false);
        _allowClose = true;
        Close();
    }

    private async Task<bool> InitializeWebViewAsync(WebView2 webView)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MicToggle",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await webView.EnsureCoreWebView2Async(environment);
            if (!ReferenceEquals(_webView, webView) || webView.IsDisposed)
            {
                return false;
            }

            var core = webView.CoreWebView2;
            core.IsMuted = _outputVolumeSlider.Value == 0;
            ApplyOutputVolume(reportErrors: false);
            _topLevelHostGate.Clear();
            _microphoneStateHost.SetAccessAllowed(false);
            core.AddHostObjectToScript(
                ChatGptMicrophoneBridge.HostObjectName,
                _microphoneStateHost);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ChatGptMicrophoneBridge.InitializationScript);

            core.PermissionRequested += HandlePermissionRequested;
            core.NavigationStarting += HandleNavigationStarting;
            core.ContentLoading += HandleContentLoading;
            core.NavigationCompleted += HandleNavigationCompleted;
            core.FrameCreated += HandleFrameCreated;
            core.WebMessageReceived += HandleWebMessageReceived;
            core.ProcessFailed += HandleProcessFailed;

            _reloadButton.Enabled = true;
            _homeButton.Enabled = true;
            _state.MarkInitializationSucceeded();
            UpdateRetryButton();
            core.Navigate(HomeAddress);
            return true;
        }
        catch (Exception ex)
        {
            _state.MarkInitializationFailed();
            UpdateRetryButton();
            SetStatus($"ChatGPT initialization failed: {ex.Message}");
            return false;
        }
    }

    private async Task DrainMicrophoneStateAsync()
    {
        await _microphoneStateGate.WaitAsync();
        try
        {
            while (true)
            {
                var desired = _state.GetDesiredMicrophoneState();
                await RunOnUiThreadAsync(() => ApplyMicrophoneStateAsync(desired.Enabled));

                if (_state.IsCurrentDesiredState(desired.Version))
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatus($"Microphone update failed: {ex.Message}");
                return Task.CompletedTask;
            });
        }
        finally
        {
            _microphoneStateGate.Release();
        }
    }

    private Task ApplyMicrophoneStateAsync(bool enabled)
    {
        RestartVoiceIdleTimer();
        UpdatePttIndicator(enabled);

        var webView = _webView;
        var core = webView?.CoreWebView2;
        if (!_state.BridgeCommandsAvailable || core is null || IsDisposed)
        {
            return Task.CompletedTask;
        }

        var messageJson = ChatGptMicrophoneBridge.BuildStateMessageJson(enabled);
        try
        {
            if (ChatGptOriginPolicy.AllowsMicrophone(core.Source))
            {
                core.PostWebMessageAsJson(messageJson);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Top-level microphone broadcast failed: {ex.Message}");
        }

        BroadcastToFrames(messageJson);
        return Task.CompletedTask;
    }

    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (IsDisposed)
        {
            return Task.CompletedTask;
        }

        if (!InvokeRequired)
        {
            return action();
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }));
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing)
        {
            completion.SetResult();
        }

        return completion.Task;
    }

    private void HandlePermissionRequested(
        object? sender,
        CoreWebView2PermissionRequestedEventArgs args)
    {
        try
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.Microphone
                && ChatGptOriginPolicy.AllowsMicrophone(args.Uri))
            {
                args.State = CoreWebView2PermissionState.Allow;
                args.Handled = true;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Permission request failed: {ex.Message}");
        }
    }

    private void HandleNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        // Keep the outgoing bridge commandable so a release can mute it before commit.
        _state.ObserveNavigationStarting();
        SetStatus("Loading ChatGPT...");

        try
        {
            _topLevelHostGate.ObserveNavigationStarting(
                args.NavigationId,
                args.Uri);
        }
        catch (Exception ex)
        {
            SetStatus($"Navigation host setup failed: {ex.Message}");
        }
    }

    private void HandleContentLoading(
        object? sender,
        CoreWebView2ContentLoadingEventArgs args)
    {
        try
        {
            _microphoneStateHost.SetAccessAllowed(
                _topLevelHostGate.ObserveContentLoading(
                    args.NavigationId,
                    args.IsErrorPage));
        }
        catch (Exception ex)
        {
            SetStatus($"Content host setup failed: {ex.Message}");
        }
    }

    private async void HandleNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            _topLevelHostGate.CompleteNavigation(
                args.NavigationId,
                args.IsSuccess);
        }
        catch (Exception ex)
        {
            SetStatus($"Navigation host cleanup failed: {ex.Message}");
        }

        try
        {
            _backButton.Enabled = _webView?.CoreWebView2?.CanGoBack == true;
            var core = _webView?.CoreWebView2;
            _state.CompleteNavigation();

            var failureStatus = args.IsSuccess
                ? null
                : $"Navigation failed: {args.WebErrorStatus}";
            if (failureStatus is null)
            {
                SetStatus("ChatGPT ready.");
            }

            await DrainMicrophoneStateAsync();

            if (failureStatus is not null)
            {
                SetStatus(failureStatus);
                return;
            }

            if (core is not null)
            {
                _ = TryAutoStartVoiceModeAsync(core);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Navigation handling failed: {ex.Message}");
        }
    }

    private void HandleFrameCreated(
        object? sender,
        CoreWebView2FrameCreatedEventArgs args)
    {
        try
        {
            RegisterFrame(args.Frame);
        }
        catch (Exception ex)
        {
            SetStatus($"Frame registration failed: {ex.Message}");
        }
    }

    private void RegisterFrame(CoreWebView2Frame frame)
    {
        var frameId = frame.FrameId;
        if (_frames.ContainsKey(frameId))
        {
            return;
        }

        _frames.Add(frameId, frame);
        _frameRegistry.Register(frameId);
        frame.NavigationStarting += HandleFrameNavigationStarting;
        frame.ContentLoading += HandleFrameContentLoading;
        frame.NavigationCompleted += HandleFrameNavigationCompleted;
        frame.Destroyed += HandleFrameDestroyed;
        frame.FrameCreated += HandleFrameCreated;
        frame.WebMessageReceived += HandleWebMessageReceived;
    }

    private void HandleFrameNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (sender is not CoreWebView2Frame frame)
        {
            return;
        }

        try
        {
            _frameRegistry.SetNavigationUri(
                frame.FrameId,
                args.NavigationId,
                args.Uri);
        }
        catch (Exception ex)
        {
            SetStatus($"Frame navigation start failed: {ex.Message}");
        }
    }

    private void HandleFrameContentLoading(
        object? sender,
        CoreWebView2ContentLoadingEventArgs args)
    {
        if (sender is not CoreWebView2Frame frame)
        {
            return;
        }

        try
        {
            _frameRegistry.ObserveContentLoading(
                frame.FrameId,
                args.NavigationId,
                args.IsErrorPage);
        }
        catch (Exception ex)
        {
            SetStatus($"Frame content loading failed: {ex.Message}");
        }
    }

    private void HandleFrameNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (sender is not CoreWebView2Frame frame)
        {
            return;
        }

        try
        {
            _frameRegistry.CompleteNavigation(
                frame.FrameId,
                args.NavigationId,
                args.IsSuccess);
            if (_frameRegistry.GetBroadcastTargets().Contains(frame.FrameId))
            {
                frame.PostWebMessageAsJson(ChatGptMicrophoneBridge.BuildStateMessageJson(
                    _microphoneStateHost.Enabled));
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Frame navigation completion failed: {ex.Message}");
        }
    }

    private void HandleFrameDestroyed(object? sender, object args)
    {
        if (sender is CoreWebView2Frame frame)
        {
            ForgetFrame(frame);
        }
    }

    private void BroadcastToFrames(string messageJson)
    {
        foreach (var frameId in _frameRegistry.GetBroadcastTargets())
        {
            if (!_frames.TryGetValue(frameId, out var frame))
            {
                _frameRegistry.Remove(frameId);
                continue;
            }

            try
            {
                if (frame.IsDestroyed() != 0)
                {
                    ForgetFrame(frame);
                    continue;
                }

                frame.PostWebMessageAsJson(messageJson);
            }
            catch (Exception ex)
            {
                SetStatus($"Frame microphone broadcast failed: {ex.Message}");
                if (IsFrameDestroyed(frame))
                {
                    ForgetFrame(frame);
                }
            }
        }
    }

    private static bool IsFrameDestroyed(CoreWebView2Frame frame)
    {
        try
        {
            return frame.IsDestroyed() != 0;
        }
        catch
        {
            return true;
        }
    }

    private void ForgetFrame(CoreWebView2Frame frame)
    {
        var tracked = _frames.FirstOrDefault(pair => ReferenceEquals(pair.Value, frame));
        if (tracked.Value is null)
        {
            return;
        }

        _frames.Remove(tracked.Key);
        _frameRegistry.Remove(tracked.Key);
    }

    private void DetachTrackedFrames()
    {
        _frames.Clear();
        _frameRegistry.Clear();
    }

    private void HandleWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (!ChatGptOriginPolicy.AllowsMicrophone(args.Source))
            {
                return;
            }

            UpdateMicrophoneStatus(args.WebMessageAsJson);
        }
        catch (Exception ex)
        {
            SetStatus($"Web message failed: {ex.Message}");
        }
    }

    private void HandleProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        SetStatus($"WebView process failed: {args.ProcessFailedKind}");
        if (!ChatGptWindowState.RequiresControlRecreation(args.ProcessFailedKind))
        {
            return;
        }

        if (!_state.TryBeginRecovery())
        {
            return;
        }

        _state.MarkBridgeCommandsUnavailable();
        try
        {
            BeginInvoke(new Action(
                async () => await RecoverWebViewAsync(args.ProcessFailedKind.ToString())));
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing)
        {
            _state.EndRecovery();
        }
    }

    private async Task RecoverWebViewAsync(string recoveryReason)
    {
        await _microphoneStateGate.WaitAsync();
        try
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            SetStatus($"Recovering WebView after {recoveryReason}...");
            _backButton.Enabled = false;
            _reloadButton.Enabled = false;
            _homeButton.Enabled = false;

            var oldWebView = _webView;
            _webView = null;
            if (oldWebView is not null)
            {
                try
                {
                    DetachWebViewEvents(oldWebView);
                }
                catch (Exception ex)
                {
                    SetStatus($"WebView detach warning: {ex.Message}");
                }

                try
                {
                    _webViewHost.Controls.Remove(oldWebView);
                }
                catch (Exception ex)
                {
                    SetStatus($"WebView removal warning: {ex.Message}");
                }

                try
                {
                    oldWebView.Dispose();
                }
                catch (Exception ex)
                {
                    SetStatus($"WebView disposal warning: {ex.Message}");
                }
            }

            var replacement = AttachReplacementWebView();
            await InitializeWebViewAsync(replacement);
        }
        catch (Exception ex)
        {
            _state.MarkInitializationFailed();
            SetStatus($"WebView recovery failed: {ex.Message}");
        }
        finally
        {
            _microphoneStateGate.Release();
            _state.EndRecovery();
            UpdateRetryButton();
        }
    }

    private async Task RetryWebViewInitializationAsync()
    {
        if (!_state.TryBeginRecovery())
        {
            return;
        }

        _retryButton.Enabled = false;
        _state.MarkBridgeCommandsUnavailable();
        await RecoverWebViewAsync("manual retry");
    }

    private void UpdateRetryButton()
    {
        _retryButton.Visible = _state.RetryAvailable;
        _retryButton.Enabled = _state.RetryAvailable;
    }

    private WebView2 AttachReplacementWebView()
    {
        _voiceModeAutoStarter = new ChatGptVoiceModeAutoStarter();
        var replacement = new WebView2 { Dock = DockStyle.Fill };
        _webView = replacement;
        _webViewHost.Controls.Add(replacement);
        replacement.BringToFront();
        return replacement;
    }

    private async Task TryAutoStartVoiceModeAsync(CoreWebView2 core)
    {
        var starter = _voiceModeAutoStarter;
        if (!starter.TryBegin())
        {
            return;
        }

        var started = false;
        try
        {
            var initialWebView = _webView;
            if (IsDisposed
                || initialWebView is null
                || initialWebView.IsDisposed
                || !ReferenceEquals(initialWebView.CoreWebView2, core)
                || _state.NavigationInProgress
                || !ChatGptOriginPolicy.AllowsMicrophone(core.Source))
            {
                return;
            }

            SetStatus("Starting voice mode...");
            for (var attempt = 0; attempt < VoiceModeStartAttempts; attempt++)
            {
                var webView = _webView;
                if (IsDisposed
                    || webView is null
                    || webView.IsDisposed
                    || !ReferenceEquals(webView.CoreWebView2, core)
                    || _state.NavigationInProgress
                    || !ChatGptOriginPolicy.AllowsMicrophone(core.Source))
                {
                    return;
                }

                var result = await core.ExecuteScriptAsync(
                    ChatGptVoiceModeAutoStarter.TryStartScript);
                if (ChatGptVoiceModeAutoStarter.DidStart(result))
                {
                    started = true;
                    SetStatus("Voice mode starting...");
                    return;
                }

                await Task.Delay(VoiceModeStartRetryDelayMilliseconds);
            }

            SetStatus("ChatGPT ready. Voice mode was not available.");
        }
        catch (Exception ex)
        {
            var webView = _webView;
            if (!IsDisposed
                && webView is not null
                && !webView.IsDisposed
                && ReferenceEquals(webView.CoreWebView2, core))
            {
                SetStatus($"Voice mode start failed: {ex.Message}");
            }
        }
        finally
        {
            starter.Complete(started);
        }
    }

    private async void HandleVoiceIdleElapsed(object? sender, EventArgs args)
    {
        _voiceIdleTimer.Stop();
        try
        {
            var core = GetVoiceRecoveryCore();
            if (core is not null)
            {
                await RecoverVoiceModeAsync(core);
            }
        }
        finally
        {
            RestartVoiceIdleTimer();
        }
    }

    private void RestartVoiceIdleTimer()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        _voiceIdleTimer.Stop();
        _voiceIdleTimer.Start();
    }

    private CoreWebView2? GetVoiceRecoveryCore()
    {
        if (IsDisposed
            || Disposing
            || _state.NavigationInProgress
            || !_state.BridgeCommandsAvailable)
        {
            return null;
        }

        var webView = _webView;
        var core = webView?.CoreWebView2;
        return webView is not null
            && !webView.IsDisposed
            && core is not null
            && ChatGptOriginPolicy.AllowsMicrophone(core.Source)
                ? core
                : null;
    }

    private bool IsCurrentVoiceCore(CoreWebView2 core)
    {
        var webView = _webView;
        return !IsDisposed
            && !Disposing
            && webView is not null
            && !webView.IsDisposed
            && ReferenceEquals(webView.CoreWebView2, core)
            && !_state.NavigationInProgress
            && ChatGptOriginPolicy.AllowsMicrophone(core.Source);
    }

    private async Task RecoverVoiceModeAsync(CoreWebView2 core)
    {
        try
        {
            var starter = _voiceModeAutoStarter;
            if (!IsCurrentVoiceCore(core) || !starter.Rearm())
            {
                return;
            }

            SetStatus("Restoring voice mode...");
            var stopResult = await core.ExecuteScriptAsync(
                ChatGptVoiceModeAutoStarter.TryStopScript);
            if (ChatGptVoiceModeAutoStarter.DidStop(stopResult))
            {
                await Task.Delay(500);
            }

            if (!IsCurrentVoiceCore(core))
            {
                return;
            }

            await TryAutoStartVoiceModeAsync(core);
        }
        catch (Exception ex)
        {
            if (IsCurrentVoiceCore(core))
            {
                SetStatus($"Voice mode recovery failed: {ex.Message}");
            }
        }
    }

    private void DetachWebViewEvents(WebView2 webView)
    {
        var core = webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        DetachTrackedFrames();
        core.PermissionRequested -= HandlePermissionRequested;
        core.NavigationStarting -= HandleNavigationStarting;
        core.ContentLoading -= HandleContentLoading;
        core.NavigationCompleted -= HandleNavigationCompleted;
        core.FrameCreated -= HandleFrameCreated;
        core.WebMessageReceived -= HandleWebMessageReceived;
        core.ProcessFailed -= HandleProcessFailed;
        _topLevelHostGate.Clear();
        _microphoneStateHost.SetAccessAllowed(false);
    }

    private void UpdateMicrophoneStatus(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("type", out var type)
            && type.GetString() != "microphone-status")
        {
            return;
        }

        if (!root.TryGetProperty("enabled", out var enabledProperty)
            || enabledProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        var enabled = enabledProperty.GetBoolean();
        var trackCount = root.TryGetProperty("trackCount", out var trackCountProperty)
            && trackCountProperty.TryGetInt32(out var parsedTrackCount)
                ? parsedTrackCount
                : 0;
        SetStatus(ChatGptWindowState.FormatMicrophoneStatus(enabled, trackCount));
    }

    private void GoBack()
    {
        try
        {
            if (_webView?.CoreWebView2?.CanGoBack == true)
            {
                _webView.CoreWebView2.GoBack();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Back failed: {ex.Message}");
        }
    }

    private void Reload()
    {
        try
        {
            _webView?.CoreWebView2?.Reload();
        }
        catch (Exception ex)
        {
            SetStatus($"Reload failed: {ex.Message}");
        }
    }

    private void NavigateHome()
    {
        try
        {
            _webView?.CoreWebView2?.Navigate(HomeAddress);
        }
        catch (Exception ex)
        {
            SetStatus($"Home failed: {ex.Message}");
        }
    }

    private void HandleOutputVolumeChanged(object? sender, EventArgs args)
    {
        UpdateOutputVolumePresentation();
        ApplyOutputVolume(reportErrors: true);
        TrySaveOutputVolume(reportErrors: true);
    }

    private void HandleAudioVolumeRefresh(object? sender, EventArgs args)
    {
        ApplyOutputVolume(reportErrors: false);
    }

    private void ApplyOutputVolume(bool reportErrors)
    {
        try
        {
            var volumePercent = _outputVolumeSlider.Value;
            var core = _webView?.CoreWebView2;
            if (core is not null)
            {
                core.IsMuted = volumePercent == 0;
            }

            _audioVolumeController.ApplyVolume(volumePercent);
        }
        catch (Exception ex)
        {
            if (reportErrors)
            {
                SetStatus($"Output volume update failed: {ex.Message}");
            }
        }
    }

    private void TrySaveOutputVolume(bool reportErrors)
    {
        try
        {
            _outputVolumeStore.Save(_outputVolumeSlider.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (reportErrors)
            {
                SetStatus($"Output volume save failed: {ex.Message}");
            }
        }
    }

    private void UpdateOutputVolumePresentation()
    {
        var volumePercent = _outputVolumeSlider.Value;
        _outputVolumeIconLabel.Text = volumePercent == 0
            ? MutedVolumeGlyph
            : VolumeGlyph;
        _outputVolumeIconLabel.ForeColor = volumePercent == 0
            ? SecondaryText
            : PrimaryText;
        _outputVolumeValueLabel.Text = $"{volumePercent}%";
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs closingArgs)
    {
        if (!_allowClose && closingArgs.CloseReason == CloseReason.UserClosing)
        {
            closingArgs.Cancel = true;
            Hide();
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        _statusDot.DotColor = GetStatusColor(text);
        _toolTip.SetToolTip(_statusLabel, text);
    }

    private void UpdatePttIndicator(bool enabled)
    {
        var color = enabled ? AccentColor : SecondaryText;
        _pttIconLabel.ForeColor = color;
        _pttStatusLabel.ForeColor = color;
        _pttStatusLabel.Text = enabled ? "Listening" : "Mic off";
    }

    private static Color GetStatusColor(string text)
    {
        if (text.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorColor;
        }

        if (text.Contains("Starting", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Loading", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Recovering", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Restoring", StringComparison.OrdinalIgnoreCase))
        {
            return BusyColor;
        }

        if (text.Contains("ready", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Microphone on", StringComparison.OrdinalIgnoreCase))
        {
            return AccentColor;
        }

        return SecondaryText;
    }
}

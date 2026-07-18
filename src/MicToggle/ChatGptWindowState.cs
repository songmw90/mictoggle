using Microsoft.Web.WebView2.Core;

namespace MicToggle;

internal sealed class ChatGptWindowState
{
    private readonly object _sync = new();
    private bool _desiredMicrophoneEnabled;
    private bool _bridgeCommandsAvailable;
    private bool _navigationInProgress;
    private bool _recoveryInProgress;
    private bool _retryAvailable;
    private long _desiredStateVersion;

    public bool DesiredMicrophoneEnabled
    {
        get
        {
            lock (_sync)
            {
                return _desiredMicrophoneEnabled;
            }
        }
    }

    public bool BridgeCommandsAvailable
    {
        get
        {
            lock (_sync)
            {
                return _bridgeCommandsAvailable;
            }
        }
    }

    public bool NavigationInProgress
    {
        get
        {
            lock (_sync)
            {
                return _navigationInProgress;
            }
        }
    }

    public bool RetryAvailable
    {
        get
        {
            lock (_sync)
            {
                return _retryAvailable;
            }
        }
    }

    public void SetDesiredMicrophoneEnabled(bool enabled)
    {
        lock (_sync)
        {
            _desiredMicrophoneEnabled = enabled;
            _desiredStateVersion++;
        }
    }

    public (bool Enabled, long Version) GetDesiredMicrophoneState()
    {
        lock (_sync)
        {
            return (_desiredMicrophoneEnabled, _desiredStateVersion);
        }
    }

    public bool IsCurrentDesiredState(long version)
    {
        lock (_sync)
        {
            return version == _desiredStateVersion;
        }
    }

    public void CompleteNavigation()
    {
        lock (_sync)
        {
            _navigationInProgress = false;
            _bridgeCommandsAvailable = true;
        }
    }

    public void ObserveNavigationStarting()
    {
        lock (_sync)
        {
            _navigationInProgress = true;
        }
    }

    public void MarkBridgeCommandsAvailable()
    {
        lock (_sync)
        {
            _bridgeCommandsAvailable = true;
        }
    }

    public void MarkBridgeCommandsUnavailable()
    {
        lock (_sync)
        {
            _bridgeCommandsAvailable = false;
        }
    }

    public void MarkInitializationFailed()
    {
        lock (_sync)
        {
            _retryAvailable = true;
        }
    }

    public void MarkInitializationSucceeded()
    {
        lock (_sync)
        {
            _retryAvailable = false;
        }
    }

    public bool TryBeginRecovery()
    {
        lock (_sync)
        {
            if (_recoveryInProgress)
            {
                return false;
            }

            _recoveryInProgress = true;
            return true;
        }
    }

    public void EndRecovery()
    {
        lock (_sync)
        {
            _recoveryInProgress = false;
        }
    }

    public static bool RequiresControlRecreation(CoreWebView2ProcessFailedKind failureKind) =>
        failureKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
            or CoreWebView2ProcessFailedKind.FrameRenderProcessExited;

    public static string FormatMicrophoneStatus(bool enabled, int trackCount)
    {
        if (trackCount <= 0)
        {
            return "Voice microphone disconnected - no active track";
        }

        var trackLabel = trackCount == 1 ? "track" : "tracks";
        return $"Microphone {(enabled ? "on" : "off")} - {trackCount} active {trackLabel}";
    }
}

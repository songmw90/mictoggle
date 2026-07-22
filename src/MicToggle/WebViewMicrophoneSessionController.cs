using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicToggle;

internal sealed class WebViewMicrophoneSessionController : IDisposable
{
    private const int PeriodicReassertIntervalMilliseconds = 1000;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly int _rootProcessId;
    private readonly object _sync = new();
    private readonly BlockingCollection<MuteRequest> _requests = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _worker;
    private Exception? _startupException;
    private int _desiredMuted = 1;
    private int _disposed;

    public WebViewMicrophoneSessionController(int rootProcessId)
    {
        _rootProcessId = rootProcessId;
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "MicToggle microphone-session controller",
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();

        if (!_ready.Wait(StartupTimeout))
        {
            Dispose();
            throw new TimeoutException("Microphone-session controller did not initialize in time.");
        }

        if (_startupException is not null)
        {
            var startupException = _startupException;
            Dispose();
            throw new InvalidOperationException(
                "Microphone-session controller could not initialize.",
                startupException);
        }
    }

    public void RequestMuted(bool muted)
    {
        Enqueue(muted, completion: null);
    }

    public Task SetMutedAsync(bool muted)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(muted, completion);
        return completion.Task;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _desiredMuted, 1);
            _stop.Set();
            _requests.CompleteAdding();
        }

        if (Thread.CurrentThread != _worker)
        {
            _worker.Join(ShutdownTimeout);
        }

        if (!_worker.IsAlive)
        {
            _requests.Dispose();
            _ready.Dispose();
            _stop.Dispose();
        }
    }

    private bool DesiredMuted => Volatile.Read(ref _desiredMuted) != 0;

    private void Enqueue(bool muted, TaskCompletionSource? completion)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            Volatile.Write(ref _desiredMuted, muted ? 1 : 0);
            _requests.Add(new MuteRequest(completion));
        }
    }

    private void Run()
    {
        var subscriptions = new List<DeviceSessionMuteSubscription>();
        try
        {
            CreateSessionSubscriptions(subscriptions);
            ApplyMutedState(DesiredMuted);
        }
        catch (Exception ex)
        {
            _startupException = ex;
        }
        finally
        {
            _ready.Set();
        }

        if (_startupException is null)
        {
            while (!_stop.IsSet)
            {
                if (_requests.TryTake(
                    out var request,
                    PeriodicReassertIntervalMilliseconds))
                {
                    ApplyPendingRequests(request);
                    continue;
                }

                TryReassertDesiredState();
            }
        }

        CancelPendingRequests();
        try
        {
            ApplyMutedState(muted: true);
        }
        catch
        {
            // The process is already closing; page-level gating remains disabled.
        }

        for (var index = subscriptions.Count - 1; index >= 0; index--)
        {
            try
            {
                subscriptions[index].Dispose();
            }
            catch
            {
                // A failed endpoint cleanup must not terminate the process.
            }
        }
    }

    private void CreateSessionSubscriptions(List<DeviceSessionMuteSubscription> subscriptions)
    {
        using var deviceEnumerator = new MMDeviceEnumerator();
        var devices = deviceEnumerator.EnumerateAudioEndPoints(
            DataFlow.Capture,
            DeviceState.Active);
        foreach (var device in devices)
        {
            try
            {
                subscriptions.Add(new DeviceSessionMuteSubscription(
                    device,
                    _rootProcessId,
                    () => DesiredMuted));
            }
            catch
            {
                device.Dispose();
            }
        }
    }

    private void ApplyPendingRequests(MuteRequest firstRequest)
    {
        var requests = new List<MuteRequest> { firstRequest };
        while (_requests.TryTake(out var request))
        {
            requests.Add(request);
        }

        try
        {
            ApplyMutedState(DesiredMuted);
            foreach (var pending in requests)
            {
                pending.Completion?.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            foreach (var pending in requests)
            {
                pending.Completion?.TrySetException(ex);
            }
        }
    }

    private void TryReassertDesiredState()
    {
        try
        {
            ApplyMutedState(DesiredMuted);
        }
        catch
        {
            // A later request or periodic pass retries transient endpoint failures.
        }
    }

    private void CancelPendingRequests()
    {
        while (_requests.TryTake(out var request))
        {
            request.Completion?.TrySetCanceled();
        }
    }

    private void ApplyMutedState(bool muted)
    {
        var targetProcessIds = ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId);
        using var deviceEnumerator = new MMDeviceEnumerator();
        var devices = deviceEnumerator.EnumerateAudioEndPoints(
            DataFlow.Capture,
            DeviceState.Active);

        foreach (var device in devices)
        {
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    using var session = sessions[index];
                    SetMutedIfTargetSession(session, targetProcessIds, muted);
                }
            }
        }
    }

    private static void SetMutedIfTargetSession(
        AudioSessionControl session,
        HashSet<int> targetProcessIds,
        bool muted)
    {
        if (!targetProcessIds.Contains((int)session.GetProcessID))
        {
            return;
        }

        using var volume = session.SimpleAudioVolume;
        volume.Mute = muted;
    }

    private sealed class DeviceSessionMuteSubscription : IDisposable
    {
        private readonly MMDevice _device;
        private readonly AudioSessionManager _sessionManager;
        private readonly int _rootProcessId;
        private readonly Func<bool> _desiredMuted;
        private int _disposed;

        public DeviceSessionMuteSubscription(
            MMDevice device,
            int rootProcessId,
            Func<bool> desiredMuted)
        {
            _device = device;
            _rootProcessId = rootProcessId;
            _desiredMuted = desiredMuted;
            _sessionManager = device.AudioSessionManager;
            _sessionManager.OnSessionCreated += HandleSessionCreated;
            ApplyToExistingSessions();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _sessionManager.OnSessionCreated -= HandleSessionCreated;
            _device.Dispose();
        }

        private void ApplyToExistingSessions()
        {
            var targetProcessIds = ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId);
            var sessions = _sessionManager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                SetMutedIfTargetSession(session, targetProcessIds, _desiredMuted());
            }
        }

        private void HandleSessionCreated(object sender, IAudioSessionControl newSession)
        {
            try
            {
                using var session = new AudioSessionControl(newSession);
                var targetProcessIds = ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId);
                SetMutedIfTargetSession(session, targetProcessIds, _desiredMuted());
            }
            catch
            {
                // Core Audio callbacks must not escape into the COM notification thread.
            }
        }
    }

    private sealed record MuteRequest(TaskCompletionSource? Completion);
}

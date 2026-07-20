using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicToggle;

internal sealed class WebViewAudioVolumeController
{
    private static readonly TimeSpan MuteScopeStartupTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MuteScopeShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly int _rootProcessId;

    public WebViewAudioVolumeController(int rootProcessId)
    {
        _rootProcessId = rootProcessId;
    }

    public int ApplyVolume(int volumePercent)
    {
        var scalarVolume = Math.Clamp(volumePercent, 0, 100) / 100F;
        var targetProcessIds = ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId);
        var updatedSessionCount = 0;

        using var deviceEnumerator = new MMDeviceEnumerator();
        var devices = deviceEnumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
            DeviceState.Active);

        foreach (var device in devices)
        {
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    using var session = sessions[index];
                    if (!targetProcessIds.Contains((int)session.GetProcessID))
                    {
                        continue;
                    }

                    using var volume = session.SimpleAudioVolume;
                    volume.Volume = scalarVolume;
                    updatedSessionCount++;
                }
            }
        }

        return updatedSessionCount;
    }

    public IDisposable BeginMuteNewSessions()
    {
        return new NewSessionMuteScope(_rootProcessId);
    }

    private sealed class NewSessionMuteScope : IDisposable
    {
        private readonly int _rootProcessId;
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly ManualResetEventSlim _stop = new(false);
        private readonly Thread _worker;
        private Exception? _startupException;
        private int _disposed;

        public NewSessionMuteScope(int rootProcessId)
        {
            _rootProcessId = rootProcessId;
            _worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "MicToggle audio-session mute scope",
            };
            _worker.SetApartmentState(ApartmentState.MTA);
            _worker.Start();

            if (!_ready.Wait(MuteScopeStartupTimeout))
            {
                Dispose();
                throw new TimeoutException("Audio-session mute scope did not initialize in time.");
            }

            if (_startupException is not null)
            {
                Dispose();
                throw new InvalidOperationException(
                    "Audio-session mute scope could not initialize.",
                    _startupException);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stop.Set();
            if (Thread.CurrentThread != _worker)
            {
                _worker.Join(MuteScopeShutdownTimeout);
            }

            if (!_worker.IsAlive)
            {
                _ready.Dispose();
                _stop.Dispose();
            }
        }

        private void Run()
        {
            var subscriptions = new List<DeviceSessionMuteSubscription>();
            try
            {
                using var deviceEnumerator = new MMDeviceEnumerator();
                var devices = deviceEnumerator.EnumerateAudioEndPoints(
                    DataFlow.Render,
                    DeviceState.Active);
                foreach (var device in devices)
                {
                    try
                    {
                        subscriptions.Add(new DeviceSessionMuteSubscription(
                            device,
                            _rootProcessId));
                    }
                    catch
                    {
                        device.Dispose();
                    }
                }
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
                _stop.Wait();
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
    }

    private sealed class DeviceSessionMuteSubscription : IDisposable
    {
        private readonly MMDevice _device;
        private readonly AudioSessionManager _sessionManager;
        private readonly int _rootProcessId;
        private int _disposed;

        public DeviceSessionMuteSubscription(MMDevice device, int rootProcessId)
        {
            _device = device;
            _rootProcessId = rootProcessId;
            _sessionManager = device.AudioSessionManager;
            _sessionManager.OnSessionCreated += HandleSessionCreated;
            MuteExistingSessions();
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

        private void MuteExistingSessions()
        {
            var sessions = _sessionManager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                MuteIfTargetSession(session);
            }
        }

        private void HandleSessionCreated(object sender, IAudioSessionControl newSession)
        {
            try
            {
                using var session = new AudioSessionControl(newSession);
                MuteIfTargetSession(session);
            }
            catch
            {
                // Core Audio callbacks must not escape into the COM notification thread.
            }
        }

        private void MuteIfTargetSession(AudioSessionControl session)
        {
            var targetProcessIds = ProcessTreeSnapshot.GetDescendantProcessIds(_rootProcessId);
            if (!targetProcessIds.Contains((int)session.GetProcessID))
            {
                return;
            }

            using var volume = session.SimpleAudioVolume;
            volume.Volume = 0F;
        }
    }
}

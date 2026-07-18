using NAudio.CoreAudioApi;

namespace MicToggle;

internal sealed class WebViewAudioVolumeController
{
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
}

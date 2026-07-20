namespace MicToggle;

internal sealed class MicrophoneActivityEventArgs(
    bool enabled,
    int trackCount,
    double level) : EventArgs
{
    public bool Enabled { get; } = enabled;

    public int TrackCount { get; } = trackCount;

    public double Level { get; } = Math.Clamp(level, 0, 1);
}

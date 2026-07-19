namespace MicToggle;

internal enum ChatGptVoiceWatchdogAction
{
    None,
    Recover,
}

internal sealed class ChatGptVoiceWatchdog
{
    private readonly TimeSpan _loadingRecoveryThreshold;
    private DateTimeOffset? _loadingSince;

    public ChatGptVoiceWatchdog(TimeSpan loadingRecoveryThreshold)
    {
        if (loadingRecoveryThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(loadingRecoveryThreshold));
        }

        _loadingRecoveryThreshold = loadingRecoveryThreshold;
    }

    public ChatGptVoiceWatchdogAction Observe(
        ChatGptVoiceModeState state,
        DateTimeOffset observedAt)
    {
        if (state == ChatGptVoiceModeState.Inactive)
        {
            _loadingSince = null;
            return ChatGptVoiceWatchdogAction.Recover;
        }

        if (state != ChatGptVoiceModeState.Loading)
        {
            _loadingSince = null;
            return ChatGptVoiceWatchdogAction.None;
        }

        _loadingSince ??= observedAt;
        if (observedAt - _loadingSince.Value < _loadingRecoveryThreshold)
        {
            return ChatGptVoiceWatchdogAction.None;
        }

        _loadingSince = observedAt;
        return ChatGptVoiceWatchdogAction.Recover;
    }

    public void Reset()
    {
        _loadingSince = null;
    }
}

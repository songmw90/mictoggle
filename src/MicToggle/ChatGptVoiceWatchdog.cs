namespace MicToggle;

internal enum ChatGptVoiceWatchdogAction
{
    None,
    Recover,
    Refresh,
}

internal sealed class ChatGptVoiceWatchdog
{
    private readonly TimeSpan _loadingRecoveryThreshold;
    private readonly TimeSpan _idleRestartInterval;
    private DateTimeOffset? _loadingSince;
    private DateTimeOffset? _refreshDueAt;

    public ChatGptVoiceWatchdog(
        TimeSpan loadingRecoveryThreshold,
        TimeSpan idleRestartInterval)
    {
        if (loadingRecoveryThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(loadingRecoveryThreshold));
        }

        if (idleRestartInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleRestartInterval));
        }

        _loadingRecoveryThreshold = loadingRecoveryThreshold;
        _idleRestartInterval = idleRestartInterval;
    }

    public ChatGptVoiceWatchdogAction Observe(
        ChatGptVoiceModeState state,
        DateTimeOffset observedAt)
    {
        _refreshDueAt ??= observedAt + _idleRestartInterval;
        if (state == ChatGptVoiceModeState.Inactive)
        {
            _loadingSince = null;
            return ChatGptVoiceWatchdogAction.Recover;
        }

        if (observedAt >= _refreshDueAt.Value)
        {
            _loadingSince = null;
            return ChatGptVoiceWatchdogAction.Refresh;
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

    public void RecordActivity(DateTimeOffset observedAt)
    {
        _refreshDueAt = observedAt + _idleRestartInterval;
    }

    public void Reset()
    {
        _loadingSince = null;
        _refreshDueAt = null;
    }
}

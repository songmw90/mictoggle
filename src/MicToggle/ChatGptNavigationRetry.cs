namespace MicToggle;

internal sealed class ChatGptNavigationRetry
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maximumDelay;
    private TimeSpan _nextDelay;
    private DateTimeOffset? _retryDueAt;

    public ChatGptNavigationRetry(TimeSpan initialDelay, TimeSpan maximumDelay)
    {
        if (initialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay));
        }

        if (maximumDelay < initialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelay));
        }

        _initialDelay = initialDelay;
        _maximumDelay = maximumDelay;
        _nextDelay = initialDelay;
    }

    public void RecordFailure(DateTimeOffset observedAt)
    {
        _retryDueAt ??= observedAt + _nextDelay;
    }

    public bool ShouldRetry(DateTimeOffset observedAt)
    {
        if (_retryDueAt is null || observedAt < _retryDueAt.Value)
        {
            return false;
        }

        _nextDelay = TimeSpan.FromTicks(Math.Min(
            _nextDelay.Ticks * 2,
            _maximumDelay.Ticks));
        _retryDueAt = observedAt + _nextDelay;
        return true;
    }

    public void Reset()
    {
        _nextDelay = _initialDelay;
        _retryDueAt = null;
    }
}

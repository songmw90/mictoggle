namespace MicToggle;

internal sealed class ChatGptVoiceSessionHealth
{
    private readonly object _sync = new();
    private readonly long _disconnectedGraceMilliseconds;
    private readonly long _heartbeatStaleMilliseconds;
    private readonly long _recoveryCooldownMilliseconds;
    private readonly Dictionary<string, Observation> _observations = [];

    private long? _disconnectedSinceMilliseconds;
    private long _nextRecoveryAllowedMilliseconds;
    private bool _hasSeenActiveTrack;
    private bool _recoveryInProgress;

    public ChatGptVoiceSessionHealth(
        long disconnectedGraceMilliseconds,
        long heartbeatStaleMilliseconds,
        long recoveryCooldownMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(disconnectedGraceMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heartbeatStaleMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recoveryCooldownMilliseconds);

        _disconnectedGraceMilliseconds = disconnectedGraceMilliseconds;
        _heartbeatStaleMilliseconds = heartbeatStaleMilliseconds;
        _recoveryCooldownMilliseconds = recoveryCooldownMilliseconds;
    }

    public bool RecoveryInProgress
    {
        get
        {
            lock (_sync)
            {
                return _recoveryInProgress;
            }
        }
    }

    public void Reset(long nowMilliseconds)
    {
        lock (_sync)
        {
            _observations.Clear();
            _disconnectedSinceMilliseconds = nowMilliseconds;
            _nextRecoveryAllowedMilliseconds = nowMilliseconds;
            _hasSeenActiveTrack = false;
            _recoveryInProgress = false;
        }
    }

    public int Observe(
        string bridgeId,
        int trackCount,
        long nowMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeId);

        lock (_sync)
        {
            _observations[bridgeId] = new Observation(
                Math.Max(0, trackCount),
                nowMilliseconds);
            PruneStaleObservations(nowMilliseconds);

            if (HasFreshActiveTrack())
            {
                _hasSeenActiveTrack = true;
                _disconnectedSinceMilliseconds = null;
            }
            else if (_observations.Count > 0)
            {
                _disconnectedSinceMilliseconds ??= nowMilliseconds;
            }

            return _observations.Values.Sum(observation => observation.TrackCount);
        }
    }

    public bool TryBeginScheduledRecovery(long nowMilliseconds)
    {
        lock (_sync)
        {
            PruneStaleObservations(nowMilliseconds);
            if (!CanBeginRecovery(nowMilliseconds) || _observations.Count == 0)
            {
                return false;
            }

            if (HasFreshActiveTrack())
            {
                _hasSeenActiveTrack = true;
                _disconnectedSinceMilliseconds = null;
                return false;
            }

            _disconnectedSinceMilliseconds ??= nowMilliseconds;
            if (nowMilliseconds - _disconnectedSinceMilliseconds.Value
                < _disconnectedGraceMilliseconds)
            {
                return false;
            }

            BeginRecovery(nowMilliseconds);
            return true;
        }
    }

    public bool TryBeginPushRecovery(long nowMilliseconds)
    {
        lock (_sync)
        {
            PruneStaleObservations(nowMilliseconds);
            if (!CanBeginRecovery(nowMilliseconds) || HasFreshActiveTrack())
            {
                return false;
            }

            if (!_hasSeenActiveTrack)
            {
                return false;
            }

            BeginRecovery(nowMilliseconds);
            return true;
        }
    }

    public void CompleteRecovery(long nowMilliseconds)
    {
        lock (_sync)
        {
            if (!_recoveryInProgress)
            {
                return;
            }

            _recoveryInProgress = false;
            _observations.Clear();
            _disconnectedSinceMilliseconds = nowMilliseconds;
            _nextRecoveryAllowedMilliseconds = Math.Max(
                _nextRecoveryAllowedMilliseconds,
                nowMilliseconds + _recoveryCooldownMilliseconds);
        }
    }

    private bool CanBeginRecovery(long nowMilliseconds) =>
        !_recoveryInProgress && nowMilliseconds >= _nextRecoveryAllowedMilliseconds;

    private bool HasFreshActiveTrack() =>
        _observations.Values.Any(observation => observation.TrackCount > 0);

    private void BeginRecovery(long nowMilliseconds)
    {
        _recoveryInProgress = true;
        _nextRecoveryAllowedMilliseconds = nowMilliseconds + _recoveryCooldownMilliseconds;
    }

    private void PruneStaleObservations(long nowMilliseconds)
    {
        foreach (var bridgeId in _observations
            .Where(pair => nowMilliseconds - pair.Value.ObservedAtMilliseconds
                > _heartbeatStaleMilliseconds)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _observations.Remove(bridgeId);
        }
    }

    private readonly record struct Observation(
        int TrackCount,
        long ObservedAtMilliseconds);
}

namespace MicToggle;

internal sealed class ChatGptTopLevelHostGate
{
    private readonly Dictionary<ulong, string?> _pendingOrigins = [];
    private string? _currentOrigin;

    public bool CurrentOriginAllowed => _currentOrigin is not null;

    public void ObserveNavigationStarting(ulong navigationId, string uri)
    {
        _pendingOrigins[navigationId] = ChatGptFrameRegistry.GetAllowedOrigin(uri);
    }

    public bool ObserveContentLoading(ulong navigationId, bool isErrorPage)
    {
        _currentOrigin = !isErrorPage
            && _pendingOrigins.TryGetValue(navigationId, out var targetOrigin)
                ? targetOrigin
                : null;
        return CurrentOriginAllowed;
    }

    public void CompleteNavigation(ulong navigationId, bool isSuccess)
    {
        _pendingOrigins.Remove(navigationId);
    }

    public IReadOnlyList<ulong> GetPendingNavigationIds() =>
        _pendingOrigins.Keys.OrderBy(navigationId => navigationId).ToArray();

    public void Clear()
    {
        _pendingOrigins.Clear();
        _currentOrigin = null;
    }
}

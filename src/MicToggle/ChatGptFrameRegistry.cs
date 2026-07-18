namespace MicToggle;

internal sealed class ChatGptFrameRegistry
{
    private static readonly string[] BootstrapOrigins =
    [
        "https://chatgpt.com",
        "https://voice.chatgpt.com",
    ];

    private readonly object _sync = new();
    private readonly Dictionary<uint, FrameState> _frames = [];

    public void Register(uint frameId)
    {
        lock (_sync)
        {
            _frames.TryAdd(frameId, new FrameState { UsesBootstrapOrigins = true });
        }
    }

    public string? SetNavigationUri(uint frameId, ulong navigationId, string uri)
    {
        lock (_sync)
        {
            if (!_frames.TryGetValue(frameId, out var frame))
            {
                return null;
            }

            frame.UsesBootstrapOrigins = false;
            var origin = GetAllowedOrigin(uri);
            if (frame.PendingNavigations.TryGetValue(navigationId, out var navigation))
            {
                navigation.UpdateTarget(origin);
            }
            else
            {
                frame.PendingNavigations.Add(navigationId, new NavigationState(origin));
            }

            return origin;
        }
    }

    public void ObserveContentLoading(
        uint frameId,
        ulong navigationId,
        bool isErrorPage)
    {
        lock (_sync)
        {
            if (!_frames.TryGetValue(frameId, out var frame))
            {
                return;
            }

            frame.UsesBootstrapOrigins = false;
            if (!frame.PendingNavigations.TryGetValue(navigationId, out var navigation))
            {
                frame.CurrentOrigin = null;
                return;
            }

            navigation.ObserveContentLoading(isErrorPage);
            frame.CurrentOrigin = isErrorPage ? null : navigation.TargetOrigin;
        }
    }

    public void CompleteNavigation(uint frameId, ulong navigationId, bool isSuccess)
    {
        lock (_sync)
        {
            if (!_frames.TryGetValue(frameId, out var frame))
            {
                return;
            }

            frame.PendingNavigations.Remove(navigationId);
        }
    }

    public bool Remove(uint frameId)
    {
        lock (_sync)
        {
            return _frames.Remove(frameId);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _frames.Clear();
        }
    }

    public IReadOnlyList<uint> GetBroadcastTargets()
    {
        lock (_sync)
        {
            return _frames
                .Where(pair => pair.Value.CurrentOrigin is not null)
                .Select(pair => pair.Key)
                .OrderBy(frameId => frameId)
                .ToArray();
        }
    }

    public IReadOnlyList<string> GetHostOrigins(uint frameId)
    {
        lock (_sync)
        {
            if (!_frames.TryGetValue(frameId, out var frame))
            {
                return [];
            }

            if (frame.UsesBootstrapOrigins)
            {
                return BootstrapOrigins.ToArray();
            }

            return new[] { frame.CurrentOrigin }
                .Concat(frame.PendingNavigations.Values.Select(navigation => navigation.HostOrigin))
                .Where(origin => origin is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static string? GetAllowedOrigin(string uriText)
    {
        if (!ChatGptOriginPolicy.AllowsMicrophone(uriText)
            || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private sealed class FrameState
    {
        public string? CurrentOrigin { get; set; }

        public Dictionary<ulong, NavigationState> PendingNavigations { get; } = [];

        public bool UsesBootstrapOrigins { get; set; }
    }

    private sealed class NavigationState(string? targetOrigin)
    {
        public string? TargetOrigin { get; private set; } = targetOrigin;

        public bool ContentLoadingObserved { get; private set; }

        public bool IsErrorPage { get; private set; }

        public string? HostOrigin => ContentLoadingObserved && IsErrorPage
            ? null
            : TargetOrigin;

        public void UpdateTarget(string? targetOrigin)
        {
            TargetOrigin = targetOrigin;
            ContentLoadingObserved = false;
            IsErrorPage = false;
        }

        public void ObserveContentLoading(bool isErrorPage)
        {
            ContentLoadingObserved = true;
            IsErrorPage = isErrorPage;
        }
    }
}

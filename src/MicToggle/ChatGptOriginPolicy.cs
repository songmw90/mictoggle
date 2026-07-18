namespace MicToggle;

internal static class ChatGptOriginPolicy
{
    public static bool AllowsMicrophone(string uriText)
    {
        return Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase));
    }
}

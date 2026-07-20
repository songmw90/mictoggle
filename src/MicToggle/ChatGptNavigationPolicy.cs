namespace MicToggle;

internal static class ChatGptNavigationPolicy
{
    private const string GatewayPrefix = "chat.gateway.";
    private const string GatewaySuffix = ".api.openai.com";

    public static bool IsRecoverableGatewayUri(string? uriText)
    {
        return Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.StartsWith(GatewayPrefix, StringComparison.OrdinalIgnoreCase)
            && uri.Host.EndsWith(GatewaySuffix, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Length > GatewayPrefix.Length + GatewaySuffix.Length;
    }
}

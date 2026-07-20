using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptNavigationPolicyTests
{
    private static readonly Type? PolicyType =
        Type.GetType("MicToggle.ChatGptNavigationPolicy, MicToggle", throwOnError: false);

    [Theory]
    [InlineData("https://chat.gateway.unified-94.api.openai.com/", true)]
    [InlineData("https://chat.gateway.eu.api.openai.com/chat/frontend/", true)]
    [InlineData("http://chat.gateway.unified-94.api.openai.com/", false)]
    [InlineData("https://api.openai.com/", false)]
    [InlineData("https://chatgpt.com/", false)]
    [InlineData("https://chat.gateway.unified-94.api.openai.com.example.com/", false)]
    [InlineData("not a URI", false)]
    public void Only_https_chat_gateway_hosts_are_recoverable(
        string uri,
        bool expected)
    {
        Assert.NotNull(PolicyType);
        var method = PolicyType!.GetMethod(
            "IsRecoverableGatewayUri",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [uri]));
    }
}

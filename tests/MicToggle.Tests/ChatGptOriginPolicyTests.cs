using System;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptOriginPolicyTests
{
    [Fact]
    public void ChatGptOriginPolicy_should_allow_only_chatgpt_origin()
    {
        var policy = Type.GetType("MicToggle.ChatGptOriginPolicy, MicToggle", true)!;
        var allows = policy.GetMethod("AllowsMicrophone")!;

        Assert.True((bool)allows.Invoke(null, ["https://chatgpt.com/"])!);
        Assert.True((bool)allows.Invoke(null, ["https://voice.chatgpt.com/"])!);
        Assert.False((bool)allows.Invoke(null, ["http://chatgpt.com/"])!);
        Assert.False((bool)allows.Invoke(null, ["https://example.com/"])!);
        Assert.False((bool)allows.Invoke(null, ["https://chatgpt.com.example.com/"])!);
        Assert.False((bool)allows.Invoke(null, ["https://evilchatgpt.com/"])!);
    }
}
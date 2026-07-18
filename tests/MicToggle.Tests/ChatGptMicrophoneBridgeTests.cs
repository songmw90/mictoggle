using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MicToggle.Tests;

public sealed class ChatGptMicrophoneBridgeTests
{
    [Fact]
    public void Bridge_uses_host_polling_and_messages_in_independent_ChatGPT_frames()
    {
        var bridge = Type.GetType("MicToggle.ChatGptMicrophoneBridge, MicToggle", true)!;
        var initializationScript = (string)bridge.GetProperty("InitializationScript")!.GetValue(null)!;
        var buildSetEnabledScript = bridge.GetMethod("BuildSetEnabledScript")!;
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            initializationScript,
            enabledScript = (string)buildSetEnabledScript.Invoke(null, [true])!,
            disabledScript = (string)buildSetEnabledScript.Invoke(null, [false])!
        })));

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "-e", Harness, payload }
        })!;

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Node bridge harness failed.{Environment.NewLine}{output}{error}");
    }

    private const string Harness = """
        const assert = require('node:assert/strict');
        const vm = require('node:vm');
        const payload = JSON.parse(Buffer.from(process.argv[1], 'base64').toString('utf8'));

        function createTrack() {
            const endedListeners = [];
            return {
                readyState: 'live',
                enabled: true,
                addEventListener(type, listener) {
                    if (type === 'ended') {
                        endedListeners.push(listener);
                    }
                },
                emitEnded() {
                    this.readyState = 'ended';
                    endedListeners.splice(0).forEach(listener => listener());
                },
                endedListenerCount() {
                    return endedListeners.length;
                },
                stop() {
                    this.readyState = 'ended';
                }
            };
        }

        function createRuntime(origin, hostState) {
            const messages = [];
            const streams = [];
            const intervals = [];
            const windowListeners = new Map();
            const webMessageListeners = [];
            let originalCalls = 0;
            let originalThisMatches = false;

            const mediaDevices = {
                getUserMedia() {
                    originalCalls += 1;
                    originalThisMatches = this === mediaDevices;
                    return Promise.resolve(streams.shift());
                }
            };
            const originalGetUserMedia = mediaDevices.getUserMedia;
            const location = new URL(origin);
            const webview = {
                hostObjects: { sync: { micToggleState: hostState } },
                postMessage(message) {
                    messages.push(message);
                },
                addEventListener(type, listener) {
                    if (type === 'message') {
                        webMessageListeners.push(listener);
                    }
                }
            };
            const window = {
                chrome: { webview },
                location,
                addEventListener(type, listener) {
                    const listeners = windowListeners.get(type) ?? [];
                    listeners.push(listener);
                    windowListeners.set(type, listeners);
                },
                setInterval(callback, delay) {
                    intervals.push({ callback, delay });
                    return intervals.length;
                }
            };
            window.window = window;
            const context = vm.createContext({
                window,
                location,
                navigator: { mediaDevices },
                console
            });

            vm.runInContext(payload.initializationScript, context);

            return {
                context,
                intervals,
                messages,
                streams,
                mediaDevices,
                originalGetUserMedia,
                get originalCalls() { return originalCalls; },
                get originalThisMatches() { return originalThisMatches; },
                evaluate(script) { return vm.runInContext(script, context); },
                tick() { intervals.filter(interval => interval.delay === 50).forEach(interval => interval.callback()); },
                tickDelay(delay) {
                    intervals.filter(interval => interval.delay === delay).forEach(interval => interval.callback());
                },
                sendState(enabled) {
                    webMessageListeners.forEach(listener => listener({
                        data: { type: 'microphone-state', enabled }
                    }));
                },
                removeHost() {
                    delete webview.hostObjects.sync.micToggleState;
                },
                setHost(value) {
                    webview.hostObjects.sync.micToggleState = value;
                },
                dispatch(type) {
                    (windowListeners.get(type) ?? []).forEach(listener => listener());
                }
            };
        }

        (async () => {
            const sharedHostState = { Enabled: false };
            const topLevel = createRuntime('https://chatgpt.com/c/top', sharedHostState);
            const childFrame = createRuntime('https://voice.chatgpt.com/call', sharedHostState);

            assert.ok(topLevel.context.window.__micToggle, 'top-level ChatGPT bridge must install');
            assert.ok(childFrame.context.window.__micToggle, 'ChatGPT child-frame bridge must install');
            assert.deepEqual(topLevel.intervals.map(value => value.delay), [50, 1000]);
            assert.deepEqual(childFrame.intervals.map(value => value.delay), [50, 1000]);
            const initialBridgeId = topLevel.messages.at(-1).bridgeId;
            assert.equal(typeof initialBridgeId, 'string');
            assert.ok(initialBridgeId.length > 0, 'status must identify its document bridge');
            const initialMessageCount = topLevel.messages.length;
            topLevel.tickDelay(1000);
            assert.equal(topLevel.messages.length, initialMessageCount + 1);
            assert.equal(topLevel.messages.at(-1).bridgeId, initialBridgeId);

            const wrappedGetUserMedia = topLevel.mediaDevices.getUserMedia;
            vm.runInContext(payload.initializationScript, topLevel.context);
            assert.equal(
                topLevel.mediaDevices.getUserMedia,
                wrappedGetUserMedia,
                'repeat installation must not double-wrap');
            assert.equal(topLevel.intervals.length, 2, 'repeat installation must not add another poller');

            const topTrack = createTrack();
            const frameTrack = createTrack();
            topLevel.streams.push({ getAudioTracks: () => [topTrack] });
            childFrame.streams.push({ getAudioTracks: () => [frameTrack] });
            await topLevel.mediaDevices.getUserMedia({ audio: true });
            await childFrame.mediaDevices.getUserMedia({ audio: true });
            assert.equal(topTrack.enabled, false, 'new top-level track must default disabled');
            assert.equal(frameTrack.enabled, false, 'new child-frame track must default disabled');
            assert.equal(topLevel.originalCalls, 1);
            assert.equal(topLevel.originalThisMatches, true, 'original binding must be preserved');

            sharedHostState.Enabled = true;
            topLevel.tick();
            childFrame.tick();
            assert.equal(topTrack.enabled, true, 'top-level poller must consume native host state');
            assert.equal(frameTrack.enabled, true, 'frame poller must consume the same native host state');

            frameTrack.enabled = false;
            childFrame.tick();
            assert.equal(
                frameTrack.enabled,
                true,
                'polling must reassert native enable after page code disables a track');

            sharedHostState.Enabled = false;
            topLevel.tick();
            childFrame.tick();
            assert.equal(topTrack.enabled, false, 'top-level poller must release without script execution');
            assert.equal(frameTrack.enabled, false, 'frame poller must release without script execution');

            topLevel.removeHost();
            sharedHostState.Enabled = true;
            topLevel.tick();
            assert.equal(
                topTrack.enabled,
                true,
                'an outgoing ChatGPT document must retain its cached native host reference');
            sharedHostState.Enabled = false;
            topLevel.tick();

            const lateHostState = { Enabled: false };
            const lateHost = createRuntime('https://chatgpt.com/c/late-host', null);
            const lateTrack = createTrack();
            lateHost.streams.push({ getAudioTracks: () => [lateTrack] });
            await lateHost.mediaDevices.getUserMedia({ audio: true });
            lateHost.setHost(lateHostState);
            lateHost.tick();
            assert.equal(lateTrack.enabled, false);
            lateHostState.Enabled = true;
            lateHost.tick();
            assert.equal(
                lateTrack.enabled,
                true,
                'an allowed document must acquire a host added after navigation completion');

            frameTrack.enabled = true;
            childFrame.tick();
            assert.equal(
                frameTrack.enabled,
                false,
                'polling must reassert native release after page code re-enables a track');

            topLevel.sendState(true);
            childFrame.sendState(true);
            assert.equal(topTrack.enabled, true, 'native message must update top-level immediately');
            assert.equal(frameTrack.enabled, true, 'native message must update child frame immediately');

            assert.equal(topLevel.evaluate(payload.disabledScript).enabled, false);
            assert.equal(topTrack.enabled, false, 'direct command remains an immediate compatibility path');
            assert.equal(topLevel.evaluate(payload.enabledScript).enabled, true);

            const secondTrack = createTrack();
            topLevel.streams.push({ getAudioTracks: () => [secondTrack] });
            await topLevel.mediaDevices.getUserMedia({ audio: true });
            assert.equal(secondTrack.enabled, true, 'latest state must apply before returning a new track');
            const wrappedStop = secondTrack.stop;
            assert.equal(secondTrack.endedListenerCount(), 1);

            topLevel.streams.push({ getAudioTracks: () => [secondTrack] });
            await topLevel.mediaDevices.getUserMedia({ audio: true });
            assert.equal(topLevel.context.window.__micToggle.status().trackCount, 2);
            assert.equal(secondTrack.stop, wrappedStop, 'duplicate track registration must not re-wrap stop');
            assert.equal(secondTrack.endedListenerCount(), 1);

            topTrack.emitEnded();
            assert.equal(topLevel.context.window.__micToggle.status().trackCount, 1);
            secondTrack.stop();
            assert.equal(topLevel.context.window.__micToggle.status().trackCount, 0);
            assert.equal(topLevel.messages.at(-1).trackCount, 0);

            sharedHostState.Enabled = true;
            childFrame.tick();
            assert.equal(frameTrack.enabled, true);
            childFrame.dispatch('pagehide');
            childFrame.tick();
            assert.equal(frameTrack.enabled, false, 'pagehide must latch tracks disabled during teardown');
            childFrame.dispatch('pageshow');
            childFrame.tick();
            assert.equal(frameTrack.enabled, true, 'BFCache restoration must resume native host polling');

            const unloadFrame = createRuntime('https://files.chatgpt.com/voice', sharedHostState);
            const unloadTrack = createTrack();
            unloadFrame.streams.push({ getAudioTracks: () => [unloadTrack] });
            await unloadFrame.mediaDevices.getUserMedia({ audio: true });
            assert.equal(unloadTrack.enabled, true);
            unloadFrame.dispatch('beforeunload');
            assert.equal(unloadTrack.enabled, false, 'beforeunload must mute tracks immediately');
            unloadFrame.tick();
            assert.equal(unloadTrack.enabled, true, 'a canceled beforeunload must resume native host state');

            for (const origin of [
                'http://chatgpt.com/',
                'https://evilchatgpt.com/',
                'https://chatgpt.com.example.com/'
            ]) {
                const blocked = createRuntime(origin, sharedHostState);
                assert.equal(blocked.context.window.__micToggle, undefined, `${origin} must not install bridge`);
                assert.equal(blocked.mediaDevices.getUserMedia, blocked.originalGetUserMedia);
                assert.equal(blocked.intervals.length, 0, `${origin} must not poll native state`);
            }

            delete topLevel.context.window.__micToggle;
            assert.equal(topLevel.evaluate(payload.enabledScript), null, 'direct command must remain null-safe');
        })().catch(error => {
            console.error(error);
            process.exitCode = 1;
        });
        """;
}

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

        function createTrack(initialEnabled = true) {
            const endedListeners = [];
            return {
                kind: 'audio',
                readyState: 'live',
                enabled: initialEnabled,
                addEventListener(type, listener) {
                    if (type === 'ended') {
                        endedListeners.push(listener);
                    }
                },
                emitEnded() {
                    this.readyState = 'ended';
                    endedListeners.splice(0).forEach(listener => listener());
                },
                endSilently() {
                    this.readyState = 'ended';
                },
                endedListenerCount() {
                    return endedListeners.length;
                },
                clone() {
                    return createTrack(this.enabled);
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
            let inputAmplitude = 0;
            let audioContextCount = 0;
            const audioContexts = [];

            class FakeAnalyser {
                constructor() {
                    this.fftSize = 2048;
                    this.smoothingTimeConstant = 0;
                }
                getByteTimeDomainData(samples) {
                    for (let index = 0; index < samples.length; index += 1) {
                        const direction = index % 2 === 0 ? 1 : -1;
                        samples[index] = 128 + (inputAmplitude * direction);
                    }
                }
                disconnect() {}
            }
            class FakeAudioContext {
                constructor() {
                    audioContextCount += 1;
                    this.state = 'suspended';
                    audioContexts.push(this);
                }
                createAnalyser() {
                    return new FakeAnalyser();
                }
                createMediaStreamSource(stream) {
                    assert.equal(stream.tracks.length, 1);
                    return {
                        connect() {},
                        disconnect() {}
                    };
                }
                resume() {
                    this.state = 'running';
                    return Promise.resolve();
                }
                suspend() {
                    this.state = 'suspended';
                    return Promise.resolve();
                }
                close() {
                    this.state = 'closed';
                    return Promise.resolve();
                }
            }
            class FakeMediaStream {
                constructor(tracks) {
                    this.tracks = tracks;
                }
                getAudioTracks() {
                    return this.tracks.filter(track => track.kind === 'audio');
                }
                clone() {
                    return new FakeMediaStream(this.tracks.map(track => track.clone()));
                }
            }

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
                    intervals.push({ callback, delay, active: true });
                    return intervals.length;
                },
                clearInterval(id) {
                    if (intervals[id - 1]) {
                        intervals[id - 1].active = false;
                    }
                }
            };
            window.AudioContext = FakeAudioContext;
            window.MediaStream = FakeMediaStream;
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
                createStream(tracks) { return new FakeMediaStream(tracks); },
                intervals,
                messages,
                streams,
                mediaDevices,
                originalGetUserMedia,
                get originalCalls() { return originalCalls; },
                get originalThisMatches() { return originalThisMatches; },
                evaluate(script) { return vm.runInContext(script, context); },
                tick(delay) {
                    intervals
                        .filter(interval => interval.active && (delay === undefined || interval.delay === delay))
                        .forEach(interval => interval.callback());
                },
                setInputAmplitude(value) {
                    inputAmplitude = value;
                },
                get audioContextCount() { return audioContextCount; },
                get closedAudioContextCount() {
                    return audioContexts.filter(context => context.state === 'closed').length;
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
            assert.deepEqual(topLevel.intervals.map(value => value.delay), [50]);
            assert.deepEqual(childFrame.intervals.map(value => value.delay), [50]);

            const wrappedGetUserMedia = topLevel.mediaDevices.getUserMedia;
            vm.runInContext(payload.initializationScript, topLevel.context);
            assert.equal(
                topLevel.mediaDevices.getUserMedia,
                wrappedGetUserMedia,
                'repeat installation must not double-wrap');
            assert.equal(topLevel.intervals.length, 1, 'repeat installation must not add another poller');

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

            topLevel.setInputAmplitude(32);
            topLevel.tick(100);
            const activity = topLevel.messages
                .filter(message => message.type === 'microphone-activity')
                .at(-1);
            assert.ok(activity, 'enabled live tracks must report actual input activity');
            assert.equal(activity.enabled, true);
            assert.equal(activity.trackCount, 1);
            assert.ok(activity.level >= 0.5 && activity.level <= 1);
            assert.equal(topLevel.audioContextCount, 1);

            topLevel.tick(50);
            assert.equal(
                topLevel.intervals.filter(value => value.delay === 100 && value.active).length,
                1,
                'host polling must not create duplicate level meters');

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
            assert.equal(
                topLevel.intervals.filter(value => value.delay === 100 && value.active).length,
                0,
                'releasing push-to-talk must stop level sampling');

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

            const pruneFrame = createRuntime('https://chatgpt.com/c/prune', { Enabled: true });
            const pruneTrack = createTrack();
            pruneFrame.streams.push({ getAudioTracks: () => [pruneTrack] });
            await pruneFrame.mediaDevices.getUserMedia({ audio: true });
            pruneFrame.tick(50);
            assert.equal(pruneFrame.audioContextCount, 1);
            pruneTrack.endSilently();
            assert.equal(pruneFrame.context.window.__micToggle.status().trackCount, 0);
            assert.equal(
                pruneFrame.closedAudioContextCount,
                1,
                'pruning an ended track must close its audio meter');

            const cloneHostState = { Enabled: true };
            const cloneFrame = createRuntime('https://chatgpt.com/c/clone', cloneHostState);
            const cloneSourceTrack = createTrack();
            cloneFrame.streams.push(cloneFrame.createStream([cloneSourceTrack]));
            const cloneSourceStream = await cloneFrame.mediaDevices.getUserMedia({ audio: true });
            const directClone = cloneSourceTrack.clone();
            const streamClone = cloneSourceStream.clone();
            const streamCloneTrack = streamClone.getAudioTracks()[0];
            assert.equal(directClone.enabled, true);
            assert.equal(streamCloneTrack.enabled, true);

            cloneHostState.Enabled = false;
            cloneFrame.tick(50);
            assert.equal(
                directClone.enabled,
                false,
                'push-to-talk release must mute a directly cloned microphone track');
            assert.equal(
                streamCloneTrack.enabled,
                false,
                'push-to-talk release must mute tracks created by cloning the microphone stream');

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

namespace MicToggle;

internal static class ChatGptMicrophoneBridge
{
    public const string HostObjectName = "micToggleState";

    public static string InitializationScript => """
        (() => {
            const hostname = location.hostname.toLowerCase();
            const allowedOrigin = location.protocol === "https:"
                && (hostname === "chatgpt.com" || hostname.endsWith(".chatgpt.com"));
            if (!allowedOrigin || window.__micToggle) {
                return;
            }

            const tracks = new Set();
            const streams = new WeakSet();
            const meters = new Map();
            let enabled = false;
            let tearingDown = false;
            let nativeState = null;

            const pauseMeter = track => {
                const meter = meters.get(track);
                if (!meter) {
                    return;
                }

                if (meter.timerId !== null) {
                    window.clearInterval(meter.timerId);
                    meter.timerId = null;
                }

                if (meter.context.state === "running") {
                    meter.context.suspend().catch(() => {});
                }
            };
            const disposeMeter = track => {
                const meter = meters.get(track);
                if (!meter) {
                    return;
                }

                pauseMeter(track);
                meter.source.disconnect();
                meter.analyser.disconnect();
                meter.context.close().catch(() => {});
                meters.delete(track);
            };

            const pruneTracks = () => {
                tracks.forEach(track => {
                    if (track.readyState !== "live") {
                        disposeMeter(track);
                        tracks.delete(track);
                    }
                });
            };
            const status = () => {
                pruneTracks();
                return { enabled, trackCount: tracks.size };
            };
            const postStatus = () => window.chrome?.webview?.postMessage({
                type: "microphone-status",
                ...status()
            });
            const removeTrack = track => {
                disposeMeter(track);
                if (tracks.delete(track)) {
                    postStatus();
                }
            };
            const startMeter = track => {
                if (!enabled || track.readyState !== "live") {
                    return;
                }

                let meter = meters.get(track);
                if (!meter) {
                    const AudioContext = window.AudioContext || window.webkitAudioContext;
                    if (typeof AudioContext !== "function" || typeof window.MediaStream !== "function") {
                        return;
                    }

                    try {
                        const context = new AudioContext();
                        const analyser = context.createAnalyser();
                        analyser.fftSize = 256;
                        analyser.smoothingTimeConstant = 0.35;
                        const source = context.createMediaStreamSource(new window.MediaStream([track]));
                        source.connect(analyser);
                        meter = {
                            analyser,
                            context,
                            lastLevel: -1,
                            lastPostedAt: 0,
                            samples: new Uint8Array(analyser.fftSize),
                            source,
                            timerId: null
                        };
                        meters.set(track, meter);
                    } catch {
                        return;
                    }
                }

                if (meter.timerId !== null) {
                    return;
                }

                meter.context.resume().catch(() => {});
                meter.timerId = window.setInterval(() => {
                    if (!enabled || track.readyState !== "live") {
                        pauseMeter(track);
                        return;
                    }

                    meter.analyser.getByteTimeDomainData(meter.samples);
                    let squareTotal = 0;
                    for (const sample of meter.samples) {
                        const normalized = (sample - 128) / 128;
                        squareTotal += normalized * normalized;
                    }

                    const rms = Math.sqrt(squareTotal / meter.samples.length);
                    const level = Math.round(Math.min(1, rms * 4) * 1000) / 1000;
                    const now = Date.now();
                    if (Math.abs(level - meter.lastLevel) < 0.03
                        && now - meter.lastPostedAt < 500) {
                        return;
                    }

                    meter.lastLevel = level;
                    meter.lastPostedAt = now;
                    window.chrome?.webview?.postMessage({
                        type: "microphone-activity",
                        enabled,
                        trackCount: status().trackCount,
                        level
                    });
                }, 100);
            };
            const syncMeters = () => {
                tracks.forEach(track => {
                    if (enabled) {
                        startMeter(track);
                    } else {
                        pauseMeter(track);
                    }
                });
            };
            const applyEnabledState = track => {
                if (track.readyState === "live") {
                    track.enabled = enabled;
                }
            };
            const setEnabled = value => {
                const nextEnabled = tearingDown ? false : Boolean(value);
                const stateChanged = enabled !== nextEnabled;
                enabled = nextEnabled;
                pruneTracks();
                tracks.forEach(applyEnabledState);
                syncMeters();
                if (stateChanged) {
                    postStatus();
                }

                return status();
            };
            const registerTrack = track => {
                if (track.readyState !== "live" || tracks.has(track)) {
                    return;
                }

                tracks.add(track);
                const originalStop = track.stop.bind(track);
                track.stop = (...args) => {
                    const result = originalStop(...args);
                    removeTrack(track);
                    return result;
                };
                applyEnabledState(track);
                if (enabled) {
                    startMeter(track);
                }
                track.addEventListener("ended", () => {
                    removeTrack(track);
                }, { once: true });

                if (typeof track.clone === "function") {
                    const originalClone = track.clone.bind(track);
                    track.clone = (...args) => {
                        const clonedTrack = originalClone(...args);
                        registerTrack(clonedTrack);
                        postStatus();
                        return clonedTrack;
                    };
                }
            };
            const registerStream = stream => {
                if ((typeof stream !== "object" && typeof stream !== "function")
                    || stream === null
                    || streams.has(stream)) {
                    return stream;
                }

                streams.add(stream);
                stream.getAudioTracks?.().forEach(registerTrack);
                if (typeof stream.clone === "function") {
                    const originalClone = stream.clone.bind(stream);
                    stream.clone = (...args) => registerStream(originalClone(...args));
                }

                return stream;
            };
            const mediaDevices = navigator.mediaDevices;
            if (typeof mediaDevices?.getUserMedia === "function") {
                const originalGetUserMedia = mediaDevices.getUserMedia.bind(mediaDevices);
                mediaDevices.getUserMedia = async constraints => {
                    const stream = await originalGetUserMedia(constraints);
                    registerStream(stream);
                    postStatus();
                    return stream;
                };
            }

            const getNativeState = () => {
                if (nativeState) {
                    return nativeState;
                }

                try {
                    const candidate = window.chrome?.webview?.hostObjects?.sync?.micToggleState;
                    if (candidate) {
                        nativeState = candidate;
                    }
                } catch {
                    // The host is intentionally absent while a document is loading.
                }

                return nativeState;
            };
            const pollHostState = () => {
                if (tearingDown) {
                    return;
                }

                try {
                    const hostEnabled = getNativeState()?.Enabled;
                    if (typeof hostEnabled === "boolean") {
                        setEnabled(hostEnabled);
                    }
                } catch {
                    // Host access can be temporarily unavailable while a frame is navigating.
                }
            };
            const muteTracks = () => {
                enabled = false;
                pruneTracks();
                tracks.forEach(applyEnabledState);
                syncMeters();
                postStatus();
            };
            const muteForTeardown = () => {
                tearingDown = true;
                muteTracks();
            };
            const resumeAfterPageShow = () => {
                tearingDown = false;
                pollHostState();
            };

            window.__micToggle = { setEnabled, status };
            window.chrome?.webview?.addEventListener("message", event => {
                getNativeState();
                const message = event.data;
                if (message?.type === "microphone-state"
                    && typeof message.enabled === "boolean") {
                    setEnabled(message.enabled);
                }
            });
            window.addEventListener("pagehide", muteForTeardown);
            window.addEventListener("beforeunload", muteTracks);
            window.addEventListener("pageshow", resumeAfterPageShow);
            window.setInterval(pollHostState, 50);
            postStatus();
            pollHostState();
        })();
        """;

    public static string BuildSetEnabledScript(bool enabled) =>
        $"window.__micToggle?.setEnabled({enabled.ToString().ToLowerInvariant()}) ?? null";

    public static string BuildStateMessageJson(bool enabled) =>
        enabled
            ? "{\"type\":\"microphone-state\",\"enabled\":true}"
            : "{\"type\":\"microphone-state\",\"enabled\":false}";
}

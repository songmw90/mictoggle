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
            const bridgeId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
            let enabled = false;
            let tearingDown = false;
            let nativeState = null;

            const pruneTracks = () => {
                tracks.forEach(track => {
                    if (track.readyState !== "live") {
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
                bridgeId,
                ...status()
            });
            const removeTrack = track => {
                if (tracks.delete(track)) {
                    postStatus();
                }
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
                track.addEventListener("ended", () => {
                    removeTrack(track);
                }, { once: true });
            };
            const mediaDevices = navigator.mediaDevices;
            if (typeof mediaDevices?.getUserMedia === "function") {
                const originalGetUserMedia = mediaDevices.getUserMedia.bind(mediaDevices);
                mediaDevices.getUserMedia = async constraints => {
                    const stream = await originalGetUserMedia(constraints);
                    stream.getAudioTracks().forEach(registerTrack);
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
            window.setInterval(postStatus, 1000);
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

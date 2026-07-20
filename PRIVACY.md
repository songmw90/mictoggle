# Privacy

MicToggle does not operate a server and does not include telemetry, analytics,
advertising, crash upload, or a custom account system.

## Network traffic

The embedded Microsoft WebView2 browser connects directly to ChatGPT and to any
services used by the ChatGPT page. Microsoft may update the Evergreen WebView2
Runtime separately. Those services are governed by their own terms and privacy
policies. MicToggle does not proxy that traffic through a project-operated
service.

## Local data

WebView2 stores cookies, login state, cache, and other browser data under:

```text
%LOCALAPPDATA%\MicToggle\WebView2
```

MicToggle stores its output-volume preference under:

```text
%LOCALAPPDATA%\MicToggle\output-volume.json
```

To remove local MicToggle data, exit the app and delete
`%LOCALAPPDATA%\MicToggle`. This signs the embedded browser out and resets local
settings.

## Keyboard hook

MicToggle installs a Windows low-level keyboard hook while it is running. The
hook recognizes `Left Ctrl + Alt`, retains only the current pressed-key state in
memory, and immediately forwards each event to the next Windows hook. It does
not record, persist, suppress, or transmit keystrokes.

## Microphone control

MicToggle automatically grants WebView2 microphone permission only to HTTPS
pages on `chatgpt.com` and its subdomains. Its local page bridge tracks the audio
tracks created by those pages and keeps them disabled unless the push-to-talk
chord is held. Windows privacy settings and ChatGPT behavior still apply.

## Page integration

MicToggle injects a local script into allowed ChatGPT pages to control live
microphone tracks and to attempt to start voice mode. It receives only bridge
status messages such as whether a track is enabled, how many live tracks exist,
and a normalized input-level value used for the on-screen activity frame. The
level meter runs only while push-to-talk is held and does not retain or transfer
audio samples. MicToggle does not extract prompts, responses, conversation
history, or account credentials.

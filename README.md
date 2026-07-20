# MicToggle

![MicToggle icon](src/MicToggle/Assets/MicToggle.png)

MicToggle is a small Windows push-to-talk wrapper for ChatGPT voice mode. It
keeps ChatGPT in a dedicated WebView2 window, enables its microphone only while
you hold `Left Ctrl + Alt`, and leaves the rest of the system microphone alone.

MicToggle is independent and unofficial. It is not affiliated with, endorsed
by, or sponsored by OpenAI. ChatGPT is a trademark of OpenAI. Use of ChatGPT is
subject to the [OpenAI Terms of Use](https://openai.com/policies/terms-of-use/).

## Features

- Hold `Left Ctrl + Alt` to talk; release either key to mute ChatGPT again.
- Shows a thin mint frame around every connected monitor while push-to-talk is
  held; the frame brightens when the ChatGPT microphone receives actual input.
- Starts ChatGPT voice mode automatically when the page exposes the voice button.
- Checks voice mode every second, immediately restarts it when inactive, and
  recovers a loading screen only after it remains stuck for 45 seconds.
- Retries ChatGPT automatically when a transient OpenAI gateway redirect fails.
- Silently reconnects voice mode after five minutes without push-to-talk input,
  including sessions whose UI still looks active but no longer responds.
- Uses a dedicated WebView2 profile, so it does not control or depend on Chrome.
- Controls only MicToggle's WebView audio volume, not the Windows master volume.
- Runs in the notification area; double-click the tray icon to show the window.
- Supports `--startup` to initialize directly in the notification area without showing the window.
- Closing the window hides it. Use the tray menu's `Exit` command to quit.
- Does not suppress the hotkey or block normal keyboard input.

## Requirements

- Windows 10 or Windows 11, x64. Other Windows architectures are not yet tested.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).
- A ChatGPT account with access to voice mode.

## Install

1. Download `MicToggle-win-x64.zip` and its `.sha256` file from the
   [latest GitHub release](https://github.com/songmw90/mictoggle/releases/latest).
2. Extract the archive to a normal local folder.
3. Run `MicToggle.exe` and sign in to ChatGPT in the MicToggle window.
4. Hold `Left Ctrl + Alt` while speaking.

Project release archives are currently unsigned, so Windows SmartScreen may
show a warning. Verify that the archive came from the expected repository and
check it against the published `.sha256` file before running it.

MicToggle does not install itself or create a startup entry. To start it with
Windows directly in the notification area, place a shortcut to
`MicToggle.exe --startup` in `shell:startup`.

## Privacy and security

MicToggle has no analytics, custom account system, or project-operated backend.
ChatGPT traffic goes directly through the embedded WebView2 browser. The app
stores its separate browser profile under `%LOCALAPPDATA%\MicToggle\WebView2`
and its output-volume setting under `%LOCALAPPDATA%\MicToggle`.

The app checks the current `Left Ctrl + Alt` state on a short background wait
loop. It does not install a keyboard hook, suppress input, or log or transmit
keystrokes. See [PRIVACY.md](PRIVACY.md) for the complete behavior disclosure.

## Project status

MicToggle embraces the beauty of incompleteness: it ships the original fixed
hotkey, original icon, and a deliberately small settings surface. ChatGPT can
change its page structure at any time, which may break voice-mode auto-start.
Focused pull requests for configurable shortcuts, accessibility, icons, and
compatibility are welcome.

## Build and test

```powershell
dotnet restore MicToggle.sln --locked-mode
dotnet test MicToggle.sln -c Release --no-restore
```

Create the framework-dependent Windows release archive:

```powershell
.\scripts\publish.ps1
```

The script writes `artifacts\MicToggle-win-x64.zip` and its matching
`.sha256` file. The archive contains the project license, privacy notice, and
exact third-party license files.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md). Small, focused changes are preferred.
Do not submit OpenAI logos, copied product artwork, credentials, cookies, or
other assets that you do not have the right to license.

## License

MicToggle source code and original icon assets are licensed under the
[MIT License](LICENSE). Third-party components remain under their respective
licenses listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

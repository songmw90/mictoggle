# Contributing

Small, focused pull requests are welcome. Useful areas include configurable
shortcuts, accessibility, Windows architecture support, icon alternatives, and
compatibility fixes after ChatGPT page changes.

## Development

Install the .NET 8 SDK, then run:

```powershell
dotnet restore MicToggle.sln --locked-mode
dotnet test MicToggle.sln -c Release --no-restore
```

Use `.\scripts\publish.ps1` to verify the public release archive. Do not commit
`bin`, `obj`, `artifacts`, WebView2 profile data, credentials, or cookies.

## Pull requests

- Keep each pull request centered on one behavior or maintenance concern.
- Add or update tests for behavior changes.
- Preserve key pass-through behavior; MicToggle must not consume the hotkey.
- Keep microphone permission restricted to HTTPS `chatgpt.com` origins.
- Explain user-visible changes and manual verification in the pull request.
- Update third-party notices when a runtime dependency changes.

By submitting a contribution, you agree to license it under this repository's
MIT License. You must have the right to submit all code and assets in the
contribution. Do not contribute OpenAI logos or artwork, third-party icons,
credentials, session data, or copied code with incompatible terms.

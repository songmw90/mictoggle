# Changelog

All notable changes to MicToggle will be documented in this file.

## 0.1.2 - 2026-07-19

- Replaced voice-track heartbeat recovery with a simple inactivity timer.
- Restarts ChatGPT voice mode after 10 minutes without push-to-talk activity.
- Resets the 10-minute timer on every push-to-talk press or release.

## 0.1.1 - 2026-07-19

- Added per-document voice-session heartbeats.
- Restores voice mode after the microphone track remains disconnected.
- Lets a push-to-talk press recover a stale voice session immediately.
- Avoids cycling healthy voice sessions and rate-limits recovery attempts.

## 0.1.0 - 2026-07-19

- Initial public source layout.
- Push-to-talk microphone control for ChatGPT voice mode.
- Dedicated WebView2 profile and automatic voice-mode start.
- Per-application output-volume control.
- Notification-area lifecycle and double-click window restore.

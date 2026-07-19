# Changelog

All notable changes to MicToggle will be documented in this file.

## 0.1.5 - 2026-07-19

- Recovers stalled Voice sessions whose live action changes to `Cancel loading` or `로딩 취소`.
- Waits for the real Voice start control after ending a session instead of mistaking the outgoing session for a successful restart.

## 0.1.4 - 2026-07-19

- Reduced the idle Voice refresh interval from 10 minutes to 5 minutes.

## 0.1.3 - 2026-07-19

- Mutes MicToggle output during the 10-minute idle Voice refresh and restores the current volume two seconds afterward.
- Added `--startup` mode, which initializes ChatGPT invisibly and remains in the notification area.

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

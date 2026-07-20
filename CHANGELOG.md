# Changelog

All notable changes to MicToggle will be documented in this file.

## 0.1.13 - 2026-07-21

- Dispatches push-to-talk state changes directly from the polling thread to the UI queue.
- Removes the extra thread-pool hop that could delay modifier-state delivery under load.

## 0.1.12 - 2026-07-21

- Pre-creates the activity-frame window handles during startup.
- Uses lightweight native visibility and opacity updates to remove push-to-talk overlay lag.
- Replaces the low-level keyboard hook with non-blocking modifier-state polling so other input is not delayed.

## 0.1.11 - 2026-07-21

- Shows the microphone activity frame on every connected monitor while push-to-talk is held.
- Removes stale edge windows when the connected monitor layout shrinks.

## 0.1.10 - 2026-07-21

- Keeps Voice reconnect audio muted for two seconds after the page reports an active session.
- Suppresses delayed activation and deactivation earcons before restoring the configured output volume.

## 0.1.9 - 2026-07-21

- Shows a thin mint activity frame on the current monitor while push-to-talk is held.
- Brightens the frame from locally measured microphone input without retaining audio data.
- Stops input-level sampling immediately when push-to-talk is released.

## 0.1.8 - 2026-07-21

- Restores Voice mode while MicToggle is hidden in the notification area.
- Prevents hidden WebView controls from causing a repeated silent-refresh loop.

## 0.1.7 - 2026-07-21

- Recycles Voice mode after five idle minutes even when a stale session still looks active in the page.
- Mutes both WebView2 and newly created Windows audio sessions during automatic Voice reconnection.
- Waits for Voice initialization before completing hidden startup.

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

# Windows + Android Cloud Release and Cross-Device Playback

Accepted coordinated implementation plan: `C:\MyRepositories\Harmony-Music\plans\2026-07-22_1200_windows_android_cloud_cross_device_playback.md`.

## Harmony Cloud implementation contract

- Keep the existing Auth0-protected, PostgreSQL-backed account sync and audio-backup API as the source of truth.
- Extend account devices with automatic platform/name metadata, active presence, FCM token registration, and last-seen state; never store raw Auth0 subjects, bearer tokens, local paths, stream URLs, or playback-media URLs.
- Add authenticated SignalR real-time routing plus short-lived idempotent command persistence, opaque-FCM command wake-ups, acknowledgement, and playback-state updates.
- Expose account-scoped device listing, presence, FCM registration, command submission/fetch/acknowledgement, and account deletion cleanup.
- Reject cross-account device access; expire or fail unacknowledged commands rather than queueing offline playback.
- Verify authorization, account isolation, expiry/idempotency, token lifecycle, FCM redaction, and deletion using the unit/integration suite before deployment.

# Fix cloud handoff: command delivery, bidirectional control, role safety

## Context

The id-only / WebSocket rework is deployed (Resolver + Cloud pushed; app built locally) and a
real two-device handoff was run: phone → Windows, both apps freshly launched.

The handoff itself worked. Reading the two diagnostic dumps, the device whose
`playerController` disagrees with its own `audioHandler` is the one mirroring — that is the phone
(UI shows *Intercontinental*, its handler holds *Pati Pati* at index 6 of 63). Windows agrees with
itself and holds a local cache path, so Windows was correctly the audio target: it resolved the
song, loaded it, and **seeked to 80590 ms — exactly the phone's position at handoff**.

It simply never played, and the phone's UI was dead. Four defects, in order of severity:

1. **Pending commands are no longer delivered.** The old 1 s poll called
   `GET /playback/commands`; the rewrite deleted it, and `SendSnapshotAsync` on socket connect
   replays only the *session snapshot*, never queued commands. A `handoff` command created while
   the target's socket is down is pushed into the void and lost — permanently on Windows, which
   has no FCM to wake it. Windows therefore only ever saw the snapshot.
2. **The snapshot path never plays.** `_applySession` defaults `playing: false` for socket
   snapshots, and the `sessionSnapshot` frame carries no progress fields at all, so the only
   `playing` signal is whatever `wasPlaying` was on the phone — which is `false` whenever the
   source button state happened to be `loading` rather than `playing`.
3. **Nothing publishes durable state anymore.** `POST /playback/session/state` has no caller since
   the rewrite. When the *target* changes song locally the session's `index` never moves and the
   controller never learns — this is what blocks "both devices can change songs, like Spotify".
4. **The receiver can forward its own playback back out.** `_applyCommand`'s `handoff` case
   (`cloud_playback_receiver.dart:323`) calls `_startPlayback` without leaving controller mode, and
   `PlaybackCommandService.updateQueue` / `playByIndex` branch on `_remoteTargetDeviceId`
   (`playback_command_service.dart:161`, `:183`). A device that is a controller and then becomes
   the target sends its own playback commands to the device it used to control. It did not fire in
   this run, but it is live the moment a second handoff happens.

**Outcome:** a handoff always starts playing on the target, either device can drive the queue, and
the controller's UI tracks it.

---

## 1. Restore command delivery (Harmony-Music, Harmony-Cloud)

**Drain on connect.** `CloudPlaybackReceiver.start()` and every reconnect must fetch
`GET /playback/commands?deviceId=` and apply + ack each one before trusting the snapshot.
`HarmonyCloudClient.pendingPlaybackCommands` / `acknowledgePlaybackCommand` still exist and are
already wired through `CloudSyncCoordinator` — nothing new is needed on the client API surface.

**Replay on connect, server side.** In `PlaybackSocketEndpoint.SendSnapshotAsync`, after the
snapshot, push a `CommandFrame` for every unacknowledged, unexpired command targeting this device.
This closes the race without reintroducing polling: REST drain covers a cold start, socket replay
covers a reconnect.

**Raise the command expiry** from 60 s (`Program.cs`, four `AddMinutes(1)` sites). A minute made
sense when a poll ran every second; with FCM-woken devices it is too tight. Five minutes, as a
named constant.

## 2. Make the handoff actually play (Harmony-Music, Harmony-Cloud)

- Add the persisted progress fields (`currentSongId`, `positionMs`, `durationMs`, `playing`) to
  `SessionSnapshotFrame` so the socket snapshot carries what `GET /playback/session` already
  returns via `PlaybackSessionResponse`.
- `_applySession` must take those from the frame instead of defaulting `positionMs: 0,
  playing: false`.
- Treat `PlayButtonState.loading` as playing when computing `wasPlaying` in
  `cloud_devices_sheet._handoff` — a handoff started mid-load must still resume.
- A `handoff` is an explicit user action: default `shouldPlay` to true unless the session state
  says `playing == false` *and* the source was genuinely paused.

## 3. Bidirectional control (Harmony-Music)

The target must publish durable state whenever its own queue or index changes, not just progress:

- Call `CloudSyncCoordinator.updatePlaybackSessionState` (currently uncalled) from the receiver
  whenever this device is the target and its `queueRevision` inputs change — song change, queue
  edit, shuffle/repeat toggle. Debounce it; it is a Postgres write, and the per-second write
  amplification is exactly what the split was meant to remove.
- Drive it off `_audioHandler.queue` / `mediaItem` while acting as target, reusing
  `PlaybackCommandService.sessionState(queue:index:)`.
- The controller already follows `currentSongId` in `applyRemoteProgress`; verify it re-points
  `currentSongIndex` when the target skips, and that `mergeResolvedQueueItems` does not fight it.

## 4. Role safety and UI state (Harmony-Music)

- **Structural fix for §4 of Context:** give `CloudPlaybackReceiver` a local-only path to the audio
  handler so a command arriving from the cloud is applied locally *by construction* and can never
  re-enter the remote-forwarding branch. Either a `localOnly` façade over `PlaybackCommandService`
  or explicit `applyLocal*` methods used exclusively by the receiver. Additionally call
  `stopRemoteControl()` on the become-target transition in the command path.
- `setCurrentSongResolving(false)` currently leaves `buttonState` stuck on `loading`
  (`player_controller.dart`, it only ever *sets* loading) — restore the real state from the handler
  or the last remote sample. This is why the phone's dump reads `buttonState: "loading"`.
- Add a staleness guard to the extrapolation ticker: stop projecting when no progress sample has
  arrived for ~3 sampling intervals, instead of sweeping to 100 % and pinning there (the phone's
  `progressCurrentMs == progressTotalMs == 206921`).

## 5. Diagnostics (Harmony-Music)

The dumps could not answer "what role did each device think it had". Extend the existing
diagnostics payload with a `cloudPlayback` block: `isMirroringRemotePlayback`,
`_remoteTargetDeviceId`, `cloudSocketStatus`, `_appliedTargetSessionId`, `_appliedQueueRevision`,
last frame type sent/received with timestamps, and the last applied command id. Cheap, and it makes
the next repro decisive instead of inferential.

---

## Verification

**Cloud** — `dotnet test`; extend `PlaybackSocketTests` with: a command created while the target is
disconnected is delivered when it connects; the snapshot frame carries progress fields.
**Music** — `flutter analyze --no-pub` and `flutter test` via the `harmony-flutter-dart` MCP server
with `timeout_ms: 600000`. Add a receiver test asserting that a `handoff` arriving while in
controller mode drives the local handler and sends nothing outward.

**Manual, the failing case first** (Jan runs the apps):
1. Phone → Windows handoff: Windows **starts playing** at the phone's position.
2. Kill Windows' network, hand off from the phone, restore network: Windows picks the command up on
   reconnect rather than losing it.
3. Skip a song **on Windows**: the phone's queue and now-playing follow.
4. Skip a song **on the phone**: Windows follows. Both directions work.
5. Hand off a second time, phone → Windows → phone: no forwarding loop, no stuck spinner.
6. Leave it paused a while: the controller's bar holds position instead of creeping to 100 %.

## Risks

- Command replay on connect plus REST drain can deliver the same command twice; acking is already
  idempotent server-side, but `_applyCommand` must tolerate a repeat (the `handoff` case restarts
  playback, so guard it with the applied-command id).
- Publishing durable state on every target-side change reintroduces write pressure if the debounce
  is wrong — keep it change-triggered, never timer-triggered.

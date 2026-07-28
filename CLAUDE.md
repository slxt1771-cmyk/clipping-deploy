# CLAUDE.md

Guidance for Claude Code (or any agent) working in this repository.

## What this is

A Windows desktop app (WPF, .NET 8) that wraps OBS Studio to give a game-clip-recording
workflow similar to NVIDIA ShadowPlay / Medal: always-on replay buffer, a global save-clip
hotkey, automatic per-game OBS profile switching, and a local clip library.

Single-machine, personal-use tool — not distributed, no installer, no telemetry. Some
defaults in code (OBS paths, websocket password, repo-absolute fallback paths) are
intentionally hardcoded for this one dev machine. Don't "harden" those into config/env
lookups unless asked; that's deliberate, not an oversight.

## Solution layout

```
ClippingSoftware.sln
src/
  ClippingSoftware.App/      WPF UI (MVVM, CommunityToolkit.Mvvm), tray icon, app entry point
  ClippingSoftware.Core/     Business logic: OBS control, game detection, profile switching,
                             recording ingest pipeline, global hotkeys
  ClippingSoftware.Data/     SQLite persistence (Microsoft.Data.Sqlite + Dapper): settings,
                             game profiles, clip metadata
  ClippingSoftware.Shared/   Win32 interop (RegisterHotKey, window helpers) — kept dependency-free
                             of WPF/OBS so it can be reused/tested in isolation
assets/GameDatabase/         known-games.json: curated executable -> display-name map
tools/ffmpeg/                Bundled static ffmpeg.exe/ffprobe.exe (see FfmpegTools)
```

Project reference graph: `App -> Core, Data, Shared` · `Core -> Data, Shared` · `Data -> Shared`.
`Shared` has no project references. Respect this direction — don't add a back-reference
(e.g. Data depending on Core).

## Architecture, in one pass

- **`Core/Obs/ObsController`** is the *sole* chokepoint for obs-websocket calls
  (OBSWebsocketDotNet). Nothing else should talk to `OBSWebsocket` directly — route new OBS
  functionality through methods on this class.
- **`Core/GameDetection/GameDetectionService`** watches the foreground window
  (`ForegroundWatcher`), applies `FullscreenHeuristic`, looks the exe up in `GameDatabase`
  (from `known-games.json`), and resolves a `GameProfile` from `GameProfileRepository`
  (falling back to the built-in Default profile). Raises `DetectedGameChanged`.
- **`Core/ProfileManager/ProfileApplier`** subscribes to that event and diffs the incoming
  profile against the currently-applied one:
  - **light switch** (audio mute state only) — no output interruption.
  - **heavy switch** (resolution / fps / OBS profile / encoder differ) — obs-websocket
    rejects `SetVideoSettings`/`SetCurrentProfile` while an output is running, so this stops
    the replay buffer first, applies the change, then restarts it (~1-2s gap, accepted
    tradeoff).
- **`Core/Recording/RecordingIngestCoordinator`** listens for `RecordingStopped` /
  `ReplayBufferSaved` from `ObsController`, waits for the file to finish flushing
  (`ClipIngestWatcher`), probes it (`ClipMetadataExtractor`, via bundled ffprobe), generates a
  thumbnail (`ThumbnailGenerator`, via bundled ffmpeg), and inserts a `ClipMetadata` row via
  `ClipRepository`.
- **`Core/HotkeyManager/GlobalHotkeyService`** wraps `RegisterHotKey`/`WM_HOTKEY`. Must be
  constructed after the owning window's `SourceInitialized` (needs a real HWND with a live
  message pump) — see the class doc comment for the exact pattern used in `MainWindow`.
- **Data layer**: `Database` owns the SQLite connection string/schema
  (`%LocalAppData%\ClippingSoftware\app.db`) and creates tables idempotently
  (`CREATE TABLE IF NOT EXISTS`) on construction — there is no separate migrations mechanism.
  If you change a table shape, update the `CREATE TABLE` in `Database.Initialize()` directly;
  existing installs are single-dev-machine, so no migration path is needed.
  Repositories (`SettingsRepository`, `GameProfileRepository`, `ClipRepository`) use Dapper
  with a private `*Row` class per table to hex the JSON-text columns
  (`ExecutableMatches`, `EncoderJson`) into/out of the domain model.
- **UI**: `MainWindow` is a `TabControl` (`Recording` tab, `Clips` tab hosting
  `ClipBrowserView`, `Profiles` tab hosting `GameProfilesView` for `GameProfile` CRUD).
  `MainViewModel` owns all the Core services and wires their events onto the WPF dispatcher
  thread. `App.xaml.cs` builds the tray icon (`H.NotifyIcon.Wpf` via `TrayIconController`)
  around the same `MainViewModel` commands.

## Conventions to follow

- MVVM via CommunityToolkit.Mvvm source generators: `[ObservableProperty]` fields
  (`_camelCase`), `[RelayCommand]` methods, `partial void On<Prop>Changed(...)` hooks. Don't
  hand-roll `INotifyPropertyChanged` or `ICommand`.
- Constructor DI is manual (`new()` in `MainViewModel`'s field initializers) — there is no DI
  container. Keep it that way; don't introduce one for a handful of singletons.
- Every public class/member that isn't self-explanatory from its name has an XML doc comment
  explaining *why*, not what — follow that pattern for new code, and keep comments accurate:
  update or delete a comment when the code it describes changes (see PROJECT.md's "stale
  docs" note — don't repeat that mistake).
  - Comment blocks state facts about the current codebase; don't reference milestone names
    ("M4 handoff", "added for M5") as if they were separate documents — the milestone happened
    within *this* codebase and the doc comment should just describe current behavior. Fold
    any non-obvious rationale directly into the comment.
- Failure handling: detection-path and best-effort paths (game detection callback, audio
  mute-on-profile-switch, ingest) swallow exceptions deliberately with a comment saying why —
  they must not crash a long-lived watcher/background flow. Don't add broad catches elsewhere
  "for safety"; only where a background loop's survival matters more than surfacing the error.
- `ObsController` methods are thin synchronous wrappers — no retry/backoff logic lives there.
  Retry/timing logic (e.g. the connect-attempt loop in `MainViewModel.InitializeAsync`)
  belongs in the caller, not the controller.

## Build / run

No test project exists yet. Build via the solution:

```
dotnet build "ClippingSoftware.sln"
dotnet run --project src/ClippingSoftware.App
```

Requires OBS Studio installed with obs-websocket enabled (bundled in OBS 28+), matching the
host/port/password in Settings (defaults target `localhost:4455`). The app launches OBS
itself (`ObsProcessManager`) if it isn't already running.

## Where to look for current status / roadmap

See `PROJECT.md` for milestone status and the active backlog — check it before assuming a
feature is finished or missing.

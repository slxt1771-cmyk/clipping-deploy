# PROJECT.md

Status and roadmap for Clipping Software. This is the "where are we" doc — read it before
assuming a feature is done, missing, or in progress. Update it whenever a milestone's status
changes; treat it as living, not historical.

## Milestone status

| Milestone | Scope | Status |
|---|---|---|
| M1 | OBS process management + websocket control (connect, start/stop record, replay buffer) | Done |
| M2 | Global hotkeys (save replay buffer, start/stop recording) | Done |
| M3 | Recording ingest pipeline (wait-for-flush, ffprobe metadata, thumbnail, DB insert) | Done |
| M4 | Clip library browser (grid, open in Explorer, delete, live-append on ingest) | Done — hosted in `MainWindow`'s "Clips" tab. |
| M5 | Game detection + per-game OBS profile switching (light/heavy switch logic) | Done |
| M6 | Trim Editor (open a clip, pick in/out points, export a trimmed copy) | Done — `MainViewModel` subscribes to `ClipBrowserViewModel.TrimEditorRequested` and opens `TrimEditorView`/`TrimEditorViewModel`. Trimming (`Core/Recording/ClipTrimmer`) uses ffmpeg stream copy (`-c copy`), not a re-encode, so the actual cut snaps to the nearest keyframe at/before the requested in-point rather than being frame-accurate — accepted tradeoff for near-instant exports; see the doc comment on `ClipTrimmer` for the reasoning. Exports land in `AppSettings.ExportStorageFolder` and get ingested through the existing `RecordingIngestCoordinator.IngestAsync` pipeline (now with `isTrimmedCopy`/`sourceClipId` parameters), so they show up in the clip browser live, same as a fresh recording. |
| — | Game profile management UI (add/edit/delete `GameProfile`s, hosted in `MainWindow`'s "Profiles" tab) | Done — `GameProfilesViewModel`/`GameProfilesView`. Makes M5's per-game switching usable without hand-editing the DB. |
| — | Hotkey rebinding UI (Settings-tab controls for the replay-save and start/stop-recording hotkeys) | Done — `HotkeyCaptureBox` control + `MainViewModel.HotkeySettingsChanged` event, handled by `MainWindow` to live-unregister/re-register via `GlobalHotkeyService`. |
| M7 | Capture source picker: switch the live scene between Display Capture / Window Capture and target a specific monitor or window | Done — `ObsController.SetCaptureMode`/`SetMonitorCaptureTarget`/`SetWindowCaptureTarget`/`GetMonitorOptions`/`GetWindowOptions`. Two places use it: the Capture tab's own "CAPTURE SOURCE" group (`MainViewModel.IsWindowCaptureMode`/`SelectedMonitorOption`/`SelectedWindowOption`, applied immediately like Connect/Start Recording) as the default/fallback target, and a per-`GameProfile` override (`GameProfile.CaptureTargetMonitorId`/`CaptureTargetWindow`, applied by `ProfileApplier.ApplyCaptureTarget` on every light/heavy switch) that takes over whenever that game is detected. |
| M8 | Per-app audio isolation: route individual running apps into their own OBS output track alongside Desktop Audio/Mic | Done — `Core/Recording/AudioSourceManager` creates `wasapi_process_output_capture` inputs, assigns each a free track (3-6; the two built-ins fixed-own tracks 1-2), and keeps the `AdvOut`/`RecTracks` profile bitmask in sync so OBS actually records the track. Persisted via `AudioSourceRepository`/`AudioSources` table and re-provisioned on every OBS reconnect (`RestoreProvisionedSources`). Surfaced in the Capture tab's "AUDIO SOURCES" group: per-row mute checkbox, remove button (built-ins can't be removed), and an add-source picker sharing the same window list M7's window-capture picker uses. Capped at 6 tracks total (4 app sources max). |
| M9 | Trim Editor per-track audio selection: choose which of a clip's recorded audio tracks make it into an export | Done — `ClipTrimmer.TrimAsync`'s `enabledAudioStreamIndexes` parameter maps/mixes down the selected ffprobe audio streams (`-map` for one, `amix` for 2+, since sharing platforms only play a file's first audio track anyway); either path forces `-c:a aac` except the single-stream/non-frame-accurate case, which can still stream-copy. `TrimEditorViewModel.AudioTrackOptions` (checkboxes in the view, one per `ClipMetadata.AudioTracks` entry) drives the selection; clips from before `AudioTracksJson` existed have an empty list, which is treated as "no track info" and falls back to the legacy keep-everything behavior rather than exporting silence. |
| M10 | Replay buffer status indicator: show buffer-running state independently of the recording indicator | Done — `MainViewModel.IsReplayBufferActive` (mirrors `ObsController.GetReplayBufferActive()`, set on connect/start/stop) backs a second BUFFER lamp+label in `MainWindow`'s tally strip, next to the existing REC/LIVE lamp — recording and the replay buffer are two independent OBS outputs, so they get two independent lamps rather than being folded into one. |
| M11 | Per-track audio: live volume sliders on every source, and separately extracted per-track files so a clip's tracks can each be re-leveled/muted after the fact and mixed down on export | Done. Live: `ObsController.SetInputVolume`/`GetInputVolume` (linear multiplier, OBS's own convention) back a slider on each Capture-tab AUDIO SOURCES row (`AudioSourceRowViewModel.VolumePercent`), same live-apply pattern as the existing mute checkbox. Retroactive: `Core/Recording/AudioTrackExtractor` splits every audio stream in a freshly ingested clip into its own AAC file (`RecordingIngestCoordinator`, one ffmpeg invocation, multiple `-map` outputs) under `%LocalAppData%\ClippingSoftware\AudioTracks`, and `AudioTrackInfo.FilePath` points at it. The Trim Editor loads one checkbox+slider per track (`TrimEditorViewModel.AudioTrackOptions`) and `ClipTrimmer.TrimAsync`'s `audioTracks` parameter (`AudioTrackExportSelection` list) maps each enabled track from its own extracted file (or straight from the source container for older clips with no extracted file) through a `volume` filter, mixed down with `amix` when more than one is enabled - a single track at 100% still stream-copies. Deleting a clip (`ClipBrowserViewModel.Delete`) also deletes its extracted track files. |
| M12 | Game-linked audio source + default app presets + quick game-only/all-except-game toggle | Done. `AudioSourceManager` now reserves track 3 unconditionally for a "Game Audio" source (`GameInputName`), auto-managed rather than hand-picked: `GameDetectionService.DetectedGameChanged` grew a third argument (the raw foreground process name, e.g. "eldenring.exe") so `MainViewModel` can call `AudioSourceManager.SetLinkedGameTargetForProcess` whenever a *recognized* game (not the Default/unmatched case) is in the foreground - it resolves that process to an OBS window value and re-points the Game source's `wasapi_process_output_capture` input via `SetWindowCaptureTarget`, creating it lazily on first use. `GameDetectionService.LastDetectedGameDisplayName`/`LastDetectedProcessName` let `MainViewModel` re-announce the current game right after an OBS reconnect instead of waiting for the next foreground change. `AudioSourceManager.EnsurePresetSources` (called after `RestoreProvisionedSources` on every connect) auto-adds Discord/Spotify/Brave into free slots for whichever are currently running and not already added - with Game reserving track 3, the remaining 3 app slots (tracks 4-6) exactly fit those three presets, though any can still be removed/replaced via the existing manual add/remove flow. Two quick-action buttons in the Capture tab's AUDIO SOURCES group (`SetGameOnlyAudioCommand`/`SetAllExceptGameAudioCommand`) flip every row's mute state in one click - "GAME ONLY" mutes everything but Game, "ALL EXCEPT GAME" mutes only Game. |
| M13 | Sound cues: toggleable audio feedback for "clip saved" and "recording started" (Medal-style), with a shared volume slider | Done. `Core/Notifications/SoundCuePlayer` wraps two `System.Windows.Media.MediaPlayer` instances (Core already sets `UseWPF` for `GlobalHotkeyService`'s HwndSource interop, so this needed no new package) - `MediaPlayer.Volume` gives the 0.0-1.0 volume control for free, which is why this wasn't built on `System.Media.SoundPlayer`/`SystemSounds` (no per-instance volume there). `ObsController` grew a `RecordingStarted` event (previously only `RecordingStopped` existed) so recording-start has something real to key off; the clip-saved cue keys off the existing `ReplayBufferSaved` event. **The two bundled WAV files (`assets/Sounds/clip-saved.wav`, `recording-started.wav`) are procedurally synthesized original tones - short sine-wave chimes generated once by a throwaway script and checked in, not sourced from any copyrighted/third-party asset.** Replace either file in place (same name/path) to swap in a different sound; nothing in code needs to change to pick it up, since `SoundCuePlayer` re-resolves the path via the same walk-up-from-`AppContext.BaseDirectory` convention `GameDatabase`/`FfmpegTools` already use. `AppSettings.PlayClipSavedSound`/`PlayRecordingStartedSound`/`SoundCueVolumePercent` persist the two toggles + volume through the existing Settings-tab save flow; a new "SOUND CUES" group on the Capture tab exposes them (volume applies live via `MainViewModel.OnSoundCueVolumePercentChanged`, no save needed to preview it). |
| M14 | Visual polish pass: custom control templates for the four controls still rendering with stock OS chrome | Done. `Theme.xaml` gained full `ControlTemplate`s for `CheckBox` (flat 16x16 box, tertiary checkmark), `RadioButton` (matching circular indicator), `ComboBox` (flat hairline border, hand-drawn chevron, dark popup, styled `ComboBoxItem`), and `ScrollBar` (thin track + pill thumb, no arrow buttons - restyling the type is enough since `ScrollViewer`'s own stock template just hosts `ScrollBar` internally, so every `ScrollViewer` in the app picked this up for free). `Slider`'s thumb was resized/rounded since it's now shared between the wide Trim Editor scrub bar and the compact per-row volume sliders added in M11/M13. Added hover states that didn't exist before: clip tiles in `ClipBrowserView` brighten their border on hover, `DataGridRow` gets a subtle background lift. While wiring this up, found and fixed 5 places (`MainWindow.xaml` x3, `GameProfilesView.xaml` x2) where an inline `ComboBox.Style`/`RadioButton.Style` block used for visibility/IsChecked toggling had no `BasedOn`, which silently discards the implicit type style entirely in WPF - those controls would have kept rendering with stock OS chrome despite the new theme. **Deliberately not done**: a custom window titlebar (replacing the native Windows chrome) - the other big lever for this look, but WPF's `WindowChrome` has a well-known content-bleeds-past-screen-edge bug specifically when combined with `WindowState="Maximized"` (which this window defaults to), and getting the fix wrong risks breaking resize/maximize/close in ways that can't be verified without eyes on the running app. Flagged as a follow-up rather than attempted blind. |
| M15 | Nav moved to a left icon rail with Clips as the default tab, runtime Primary/Secondary accent-color customization, and the custom titlebar M14 deliberately deferred | Done - see below for detail on each. |
| M16 | Tab restructure (Clips/Capture/Settings, in that order), Capture-vs-everything-else split, embedded per-clip editor with autosaved drafts, a simple multi-clip Sequence editor, and a layout/centering pass on Capture+Settings | Done - see below for detail on each. |
| M17 | Page-by-page polish pass: Clips header reflow + a Filters popup, and folding Capture back into Settings | Done. **Clips**: the title row (`ClipBrowserView.xaml`) now right-docks SEARCH/FILTERS/REFRESH in line with "CLIP-SYS // CL-001" instead of a separate row below it, and the "MODULE_ID: CLIP_LIBRARY" subtitle is gone (redundant with the tab's own context). LIBRARY/EDIT/TAGS became one coherent pill group (`Theme.xaml`'s new `SubNavButtonStyle`): dim/transparent by default, white label + tertiary bottom-accent when selected - same visual grammar the left icon rail already used for its own selected state, just applied here too. The always-visible tag-chip row moved into a new FILTERS popup (`ClipBrowserViewModel.IsFilterPanelOpen`/`ToggleFilterPanelCommand`), which also gained a Clips-vs-Recordings segmented filter (`TypeFilter`, checked against `ClipMetadata.IsTrimmedCopy` in `FilterClip`) - lets a trimmed export be told apart from a raw replay-buffer/manual recording. **Capture folded into Settings**: the separate Capture tab is gone - nav is just Clips/Settings now. Its CAPTURE SOURCE and AUDIO SOURCES groups joined Settings' existing WrapPanel (still 340px/uniform-width, now six groups in a row instead of two-plus-four split across tabs), and Game Profiles moved down with them, unchanged in its own bounded (non-scrolled) row below the WrapPanel's ScrollViewer for the same reason it always needed one - a DataGrid's "*" row collapses to zero inside an infinite-height ScrollViewer. CONNECT (previously floating in its own row next to Start/Stop Recording) is now a centered action button inside the CONNECTION group, next to the host/port/password fields it actually connects. Start/Stop Recording and the global StatusMessage moved to the title bar (`MainWindow.xaml`'s row 0) - reachable from either tab now, same reasoning as the LIVE/BUFFER lamps already living there. **Every milestone row above that says "Capture tab"** (M7, M8, M12, M13, M14) is describing where things lived at the time they were built - that page doesn't exist anymore, its content is on Settings now; left as historical record rather than rewritten, per this doc's own convention of layering later changes as notes (see M15/M16) instead of editing old rows in place. |
| M18 | First real-usage bug pass: per-game auto-detect resolution (fixes black-bar clips from a stretched-resolution game), click-to-seek on the Trim Editor scrub bar, click-to-pause on the video, a stray font-size fix, and app-audio source staleness/mute-scope clarity | Done - see below for detail on each. |

Multi-track audio recording itself (a clip's container carrying more than the old fixed
desktop+mic pair) is the schema change underneath M8/M9: `ClipMetadata.HasMicTrack`/
`HasDesktopAudioTrack` were replaced by `AudioTracks: List<AudioTrackInfo>` (`StreamIndex` +
`Label`), populated at ingest time by matching `ClipMetadataExtractor`'s ffprobe audio-stream
count positionally against `AudioSourceManager`'s current track layout (see
`RecordingIngestCoordinator.BuildAudioTracks`). `Database` migrates existing rows in place
(`AddColumnIfMissing`/`BackfillAudioTracksJsonFromLegacyColumns`) rather than losing the old
mic/desktop flags on installs that predate this - the now-redundant `HasMicTrack`/
`HasDesktopAudioTrack` columns themselves were dropped in M15 (`Database.DropColumnIfExists`),
once backfill had already copied their data into `AudioTracksJson`; see M15's bug-fix note below.

**M15 in detail:**

- **Left icon rail, Clips-first.** `TabControl`/`TabItem` in `Theme.xaml` rebuilt for `TabStripPlacement="Left"` (VS Code activity-bar pattern) instead of the old cramped bottom strip - left-edge accent bar on the selected item instead of the old top-edge one. `MainWindow.xaml`'s `TabControl` now sets `SelectedIndex="1"` (Clips) so that's what greets you on launch; declaration order (Capture/Clips/Profiles) was left alone rather than physically reordering the XAML, since only the *default selection* was actually requested.
- **Accent color customization.** `App/Theming/ColorUtils` (hex parsing, tint/shade-via-blend) and `ThemeManager` (`ApplyAccentColors`/`ResetToDefaults`) mutate the existing `TertiaryBrush`/`TertiaryTint`/`TertiaryShade`/`TallyRedBrush` `SolidColorBrush` resource objects' `Color` property in place rather than converting the app to `DynamicResource` - every `StaticResource` reference across ~20 XAML files already points at those same shared brush instances, so mutating `.Color` re-themes the whole app instantly with zero XAML changes elsewhere. "Primary"/"Secondary" (the user-facing names) map onto Tertiary (signal/live/focus) and Alert (recording/destructive) respectively - Primary(white)/Secondary(black)/Neutral scaffold and the mono font/flat line style stay fixed, per the brief. New "APPEARANCE" group on the Capture tab: two hex fields with live swatch preview (`Converters/HexToBrushConverter`), APPLY (preview without persisting) and RESET buttons, persisted through the existing Settings save flow (`AppSettings.PrimaryAccentHex`/`SecondaryAccentHex`, additive DB migration).
- **Custom titlebar, done this time.** M14 explicitly deferred this as too risky to attempt blind; built now since the user asked for it directly. `WindowChrome` (`CaptionHeight="40"`, `UseAeroCaptionButtons="False"`) replaces the native Windows caption bar with a themed strip that folds in what used to be a separate tally strip (LIVE/BUFFER/GAME status) plus custom minimize/maximize/close buttons (`TitleBarButtonStyle`/`TitleBarCloseButtonStyle` in `Theme.xaml`, the close button turning Alert-red on hover per the one Windows convention worth keeping). The known WindowChrome-plus-`WindowState="Maximized"` bug (maximized window overhangs the work area by `SystemParameters.WindowResizeBorderThickness`, measured at exactly 8px here) needed **two** fixes together: a `WM_GETMINMAXINFO` hook (`Win32Window.cs` extended with `MINMAXINFO`/`POINT`, hooked via `HwndSource.AddHook` in `MainWindow.xaml.cs`, mirroring `GlobalHotkeyService`'s existing HWND-interop pattern) *and*, since that hook alone didn't fully resolve it, a companion `Margin="8"` on the root `Grid` triggered only when `WindowState == Maximized`. Also switched from `WindowState="Maximized"` in XAML to setting it in code *after* the hook is attached (`SourceInitialized`), since starting already-maximized sends the critical `WM_GETMINMAXINFO` before a XAML-declared state gives the hook a chance to attach. **This was verified visually** (screenshots via an automated PowerShell capture + `Read`, both maximized and restored window states, plus a `GetWindowRect`-vs-`SystemParameters.WorkArea` bounds check) - a real check, not just built-and-hoped.
- **Bug found and fixed along the way, unrelated to this session's UI work:** a real clip-saving failure (`SQLite Error 19: NOT NULL constraint failed: Clips.HasMicTrack`) surfaced live while testing. Cause: M11's `AudioTracksJson` migration was purely additive - `ClipRepository`'s INSERT stopped supplying `HasMicTrack`/`HasDesktopAudioTrack`, but the migration never dropped those still-NOT-NULL legacy columns from this machine's pre-M11 database, so every real clip ingest was failing until this was caught and fixed with `Database.DropColumnIfExists` (mirrors `AddColumnIfMissing`; requires SQLite 3.35+, which `Microsoft.Data.Sqlite`'s bundled version comfortably exceeds).

**M16 in detail:**

- **Nav order, for real this time.** M15 only changed the default *selection*, leaving the XAML
  declaration order alone. `MainWindow.xaml`'s `TabControl` items are now physically reordered
  Clips/Capture/Settings (`SelectedIndex="0"`) - Clips is what you actually check most; Capture/
  Settings are set-once-and-forget config, so rail position now matches priority, not just default
  selection.
- **Capture vs. Settings split.** The old single "SETTINGS" tab (really: capture mechanics +
  connection + storage/hotkeys + sound cues + appearance + game profiles, all in one place) is now
  two tabs. Capture keeps only what's actually about *capturing*: CAPTURE SOURCE and AUDIO SOURCES.
  Everything else - CONNECTION, STORAGE & HOTKEYS, SOUND CUES, APPEARANCE, and the full Game
  Profiles CRUD grid (formerly its own "Profiles" tab) - moved into a renamed "Settings" tab.
  Both tabs' groupboxes were also reorganized from a horizontal-scroll strip of mismatched widths
  into a uniform-width (340px) `WrapPanel` grid that actually lines up and wraps instead of running
  off-screen, and form labels across `MainWindow.xaml`/`GameProfilesView.xaml` were centered
  (`HorizontalAlignment="Center"`) along with `GroupBox` headers and `DataGrid` cell/column-header
  content (`Theme.xaml`), per the "make sure all the text is centered" ask.
- **Embedded per-clip editor with autosaved drafts.** The Trim Editor stopped being a separate
  popup `Window` and is now a `UserControl` (`TrimEditorView`/`TrimEditorViewModel` unchanged
  internally) embedded directly in the Clips tab's own "EDIT" sub-view, with a clip-switcher
  `ComboBox` so you can hop between clips without leaving the editor. Because the same
  `MediaElement` instance now lives across multiple clips instead of one per popup session, it
  needs `Player.Stop(); Player.Close();` on every `DataContextChanged` (switching clips, or
  going back to the library) to release the previous file's lock before opening the next one.
  Progress (in/out points, frame-accurate toggle, per-track audio selection) autosaves on every
  change via `ClipEditDraftRepository` (write-through, same pattern as `AudioSourceManager` - no
  debounce, no explicit save button) so an app restart mid-edit doesn't lose anything; a draft is
  deleted once its clip is actually exported.
- **Simple multi-clip Sequence editor.** A genuinely basic "DaVinci-lite" alternative editing spot,
  explicitly *not* meant to compete with the per-clip editor: pick clips from the library (the "+"
  tile action, `AddToSequenceCommand`), reorder/trim each with plain in/out-second fields
  (`SequenceEditorViewModel`/`SequenceClipRowViewModel`, write-through persisted via
  `SequenceRepository` - one persistent sequence, not a list of saved projects, matching the "just
  an alternative spot" framing), then export the whole thing as one combined file
  (`Core/Recording/SequenceExporter`: trims each segment frame-accurate via the existing
  `ClipTrimmer`, concatenates via ffmpeg's concat demuxer, cleans up its temp files in a `finally`).
- **A real WPF bug found and fixed along the way.** `ContentPresenter ContentSource="SelectedContent"`
  inside the M15 custom `TabControl` `ControlTemplate` silently produced *zero* rendered content for
  the selected tab - no exception anywhere (checked via a `DispatcherUnhandledException` hook and an
  `AppDomain.FirstChanceException` hook, neither fired), no binding-error trace, just nothing. Root
  cause never fully explained even after extensive bisection (a hand-rolled `System.Windows.Automation`
  tree dump falsely reported the same empty-content symptom for content that a `PrintWindow`-based
  screenshot proved *was* actually rendering correctly - the automation tooling itself was unreliable
  in this environment, most likely because the window wasn't focused/foreground during the automated
  checks, which sent the investigation in circles for a while). Fixed regardless by swapping the
  `ContentSource="SelectedContent"` reflection-convention binding for an explicit
  `Content="{TemplateBinding SelectedContent}" ContentTemplate="{TemplateBinding SelectedContentTemplate}"`
  pair, which is more robust and is what the fix landed on even though the original may not have
  actually been broken. If tab content ever silently goes blank again, verify with a `PrintWindow`
  capture before trusting `System.Windows.Automation` output in this environment.

All of M1-M17 plus the two originally-unnumbered gaps (game profile UI, hotkey rebinding UI)
are now done. What's left is smaller polish items — see below.

**M18 in detail:**

- **Auto-detect resolution, per game profile.** Root cause of the black-bar clips: `GameProfile.OutputWidth`/
  `OutputHeight` was always a fixed value (2560x1440 for Default), applied as both OBS's base *and* output
  canvas size on every heavy switch (`ProfileApplier.ApplyHeavySwitch`) - a game actually rendered at a
  different ("stretched", e.g. via Nvidia Control Panel's resolution override) resolution gets captured at
  that smaller/differently-shaped size but pasted onto the larger fixed canvas, leaving the unfilled canvas
  area black. `GameProfile.AutoDetectResolution` (new bool, additive DB column) lets a profile opt out of the
  fixed value: when set, `ProfileApplier.ResolveResolution` queries `Win32Window.GetSystemMetrics(SM_CXSCREEN/
  SM_CYSCREEN)` (Shared - new P/Invoke) right before applying, which reflects whatever the OS currently
  reports as the primary display's resolution *including* a Nvidia Control Panel-style override (that changes
  the actual reported display mode, not just how the panel scales the final pixels), so the canvas always
  matches what's actually being rendered. `RequiresHeavySwitch`'s diff was rewired through the same resolver
  so a profile with auto-detect on still diffs correctly against one that doesn't. Manual entry (the
  pre-existing Output Width/Height fields) is unchanged and still used when auto-detect is off - exposed as an
  "Auto-Detect Resolution" checkbox above them in `GameProfilesView` that disables the two fields while
  checked (`InverseBooleanConverter`, new). No multi-monitor mapping involved (deliberately - primary-display
  `GetSystemMetrics` only), matching this app's single-machine/personal-use scope.
- **Trim Editor scrub bar: click-to-seek.** `Slider`'s `IsMoveToPointEnabled` defaults to `false`, so a plain
  click on the track only nudged the value by `LargeChange` via the track's repeat-buttons instead of jumping
  to the clicked point - looked like "click seeks to the wrong spot, drag still works." Set on the app-wide
  `Slider` style in `Theme.xaml` (not just the scrub bar) so every slider, including the per-track volume
  sliders, gets the same click-to-jump behavior consistently.
- **Trim Editor: click-to-pause on the video.** The `Border` hosting the `MediaElement` now has a
  `MouseLeftButtonDown` handler (`TrimEditorView.Player_Click`) that toggles play/pause, tracked via a new
  `_isPlaying` field since `MediaElement` doesn't expose one - same toggle the PLAY/PAUSE buttons already
  used, now reachable by clicking the video itself.
- **Stray font-size fix.** The titlebar's detected-game-name text (`MainWindow.xaml`) was `FontSize="12"`
  while every other label/value in that same row (`EyebrowTextStyle`, the status-message text) is `11` - a
  from-code audit of every hardcoded `FontSize`/`FontFamily`/`Foreground` in the app turned up this one
  outlier; everything else was already consistent (no stray hardcoded colors/fonts found bypassing the theme
  resources).
- **App-audio source staleness fix, reported the same session.** A per-app audio source's OBS window target
  (`AudioSourceRecord.WindowTarget`, a `"title:class:exe"` string) was only ever set once, at add time -
  nothing re-pointed it afterward. Spotify's window title includes the current song, so its target went
  stale within minutes even without restarting the app, let alone across the close/reopen cycles a
  background app like Spotify goes through constantly; `AudioSourceManager.RefreshAppSourceTargets`
  (new) re-resolves every existing app source's target from the executable name embedded at the end of its
  stored value against OBS's live window list, updating both the OBS input (`SetWindowCaptureTarget`) and
  the persisted record (`AudioSourceRepository.UpdateWindowTarget`, new) whenever it's drifted. `MainViewModel`
  runs it (plus `EnsurePresetSources`, so an app that wasn't running yet at connect time still gets
  auto-added once it appears) on a 30s `DispatcherTimer` started/stopped alongside OBS connect/disconnect -
  polling rather than event-driven, since a backgrounded app's window can change without ever firing a
  foreground-change event. **Separately, and likely the bigger cause of the actual reported symptom** ("I
  muted Spotify but I could still hear it in the clip"): muting an app's own isolated track
  (`AudioSourceManager.SetMuted`) never removes that app from Desktop Audio - Desktop Audio is a *separate,
  parallel* whole-system capture, so an unmuted Desktop Audio track (the default, and virtually always the
  first/only track a normal video player opens) still carries every app's sound regardless of any per-app
  track's mute state. That's an OBS/Windows audio-architecture reality, not a bug in `SetInputMute` wiring -
  fixed here only by adding a `ToolTip` on the per-app MUTE checkbox explaining it and pointing at the actual
  way to exclude an app from an export (deselect its track in the Trim Editor's AUDIO TRACKS row, per M9/M11).
  True live isolation (so Desktop Audio itself never picks up a given app) would need routing that app's
  output to a non-default Windows playback device - an OS-level setup step for the user, not something this
  app's code can do on its own.

- **Visual identity pass.** The app moved from an early tally-lamp red/green/amber palette to a
  strict 4-family system (Primary white / Secondary black / Tertiary pale-lavender signal color /
  Neutral near-black ramp, plus Alert red for recording/destructive-only). Covers the whole app:
  `Theme.xaml` (palette, button variants — Primary/Outlined/Danger/Icon —, restyled TabControl/
  GroupBox/Slider/DataGrid), icon-based bottom-nav tab headers in `MainWindow`, and a Clip Library
  pass in `ClipBrowserView`/`ClipBrowserViewModel`/`ClipItemViewModel` (live search-filter via
  `ClipsView`/`SearchText`, resolution+source tag chip, icon-only card actions). An earlier draft
  had placeholder "cybersigilism" line-art (`SigilTileBrush`/`SigilWatermarkBrush`) standing in for
  tribal/antler background artwork — removed by request; the app is intentionally blank/flat
  behind the chrome now (mono font + hairline borders carry the visual identity, no background
  texture). Don't re-add a background texture/watermark without being asked.
- **Frame-accurate trim option.** Done — `ClipTrimmer.TrimAsync` takes a `frameAccurate` bool: off
  (default) is the original stream-copy/keyframe-snapped path; on re-encodes (`-c:v libx264 -crf
  18 -c:a aac`) with `-ss` after `-i` for an exact in-point, at the cost of export time and a small
  bitrate hit. Exposed as a "FRAME-ACCURATE (slower, re-encodes)" checkbox in `TrimEditorView`,
  off by default.

## Known gaps / follow-ups

- Stale-comment cleanup: check `ClipBrowserView`/`ClipBrowserViewModel` and related M4/M6 doc
  comments for anything still describing a since-finished feature as unwired (M6's merge
  already fixed the ones that existed as of that work; watch for new ones as things evolve).
- Clip Library cards don't surface `ClipMetadata.AudioTracks` (M8/M9/M11's per-track info) anywhere -
  the resolution+source tag chip added in the visual identity pass predates it. A nice-to-have
  ("2 TRACKS" chip, or listing labels in the tooltip) if it turns out to matter in practice; not
  done yet, deliberately deferred rather than missed.
- Extracted per-track audio files (M11, `%LocalAppData%\ClippingSoftware\AudioTracks`) are cleaned
  up on clip delete but have no standalone orphan sweep - a clip row deleted directly from the DB
  (not through `ClipBrowserViewModel.Delete`) would leave its track files behind, same class of gap
  as thumbnails already had. Not handled, since it was already an accepted gap for thumbnails.
- Trimmed exports get their own extracted per-track files, but `RecordingIngestCoordinator.BuildAudioTracks`
  labels them from whatever `AudioSourceManager.Sources` currently holds (the *live* OBS source list),
  not from what actually went into that export's mixdown - a pre-existing positional-labeling
  simplification (see the class's own doc comment) that M11 inherits rather than fixes.
- Both the Game-link (M12) and preset auto-add (M12) resolve a process to an OBS capture target via
  `GetWindowOptions`, which enumerates *windows*, not raw WASAPI audio sessions - a game/Discord/Spotify/
  Brave instance with no open/visible window (fully minimized to tray, still launching, etc.) won't be
  found and that slot is just left as-is until a later retry finds it. True "every audio session on the
  machine" enumeration (not window-based) was explicitly deferred, same constraint the pre-M12 manual
  add flow already had - not a regression, just not yet closed.

## Architectural decisions worth knowing (so you don't re-litigate them)

- **No DI container, no test project.** Both are intentional for a single-dev-machine
  personal tool at this size — don't introduce either without a concrete reason tied to a
  task at hand.
- **No migrations mechanism.** `Database.Initialize()` uses `CREATE TABLE IF NOT EXISTS`
  only. Schema changes are made by editing that SQL directly; there is exactly one real
  install to worry about (this machine), so destructive/lossy schema edits are acceptable if
  called out, but prefer additive changes where easy.
- **Heavy-switch profile changes cost ~1-2s of replay-buffer downtime** by design (OBS
  rejects video-settings/profile changes while an output is running). This was an accepted
  tradeoff during M5, not a bug to "fix" by finding a workaround.
- **Hardcoded absolute paths** (`D:\claude stuff\clipping software\...` fallbacks in
  `FfmpegTools`, `GameDatabase`, and the default `ObsExecutablePath`/`ClipStorageFolder` in
  `AppSettings`) are deliberate for this single-machine setup, not leftover debug code.
- **Trim exports default to stream-copy, not frame-accurate** (see `ClipTrimmer`'s doc comment) —
  a deliberate speed/simplicity default; frame-accurate re-encode is now available as an opt-in
  checkbox, not the default, for the same export-time/quality reasons.

## Suggested next work (in rough priority order)

1. General polish/refinement pass now that M1-M17 + the profile/hotkey UIs + visual identity are
   all in: UX rough edges, error-message clarity, XAML layout tightening, etc. — nothing specific
   queued yet; revisit this doc once real usage surfaces friction.

~~2. Verify a Reset-to-default with an actively-invalid or edge-case hex...~~ **Done (M17 bug-hunt
pass).** `#000`-style 3-digit shorthand and `#AARRGGBB` 8-digit alpha-prefixed hex both parse fine
via `ColorUtils.TryParseHex` (it wraps WPF's own `ColorConverter`, which already handles both
forms) and RESET always cleanly restores defaults afterward - no stuck/broken state. One real bug
found and fixed along the way: `ThemeManager.ApplyOne` was applying the parsed color's alpha
channel as-is, so an alpha-prefixed hex could produce broken translucency in the app's
`AllowsTransparency="True"` popups (the FILTERS popup, ComboBox dropdowns) even though this is a
fully-opaque design system with no transparent surfaces by intent. Now forces `color.A = 255` after
parsing, before applying - `ColorUtils.TryParseHex` itself is untouched (it's also used by
`HexToBrushConverter`'s live swatch preview, where showing the real alpha is fine).

## Non-goals (don't add unless explicitly asked)

- Multi-machine/config-file-based settings, installers, auto-update.
- Cloud upload/sharing of clips.
- Support for capture backends other than OBS.
- Automated tests as a blanket requirement — add them for genuinely tricky logic (e.g.
  light/heavy switch diffing) if asked, but this isn't a "needs full coverage" codebase.

# Installing Clipping Software

## For users: download and run

1. Go to the [Releases page](../../releases) and download `ClippingSoftware-Setup-<version>.exe`.
2. Run it. Windows SmartScreen will warn that the publisher is unknown — the installer isn't code-signed
   (that needs a paid certificate). Click **More info → Run anyway**.
3. Follow the prompts: choose an install folder, optionally tick the desktop shortcut and
   "start automatically when I sign in", then click Install.
4. The app launches and walks you through a four-step first-run setup:
   - **Welcome** — confirms the install is complete.
   - **Find OBS Studio** — usually detected automatically; Browse if not.
   - **Connect to OBS** — paste the WebSocket password (see below). There's a Test Connection button.
   - **Where clips are saved** — pick folders and how many seconds a clip covers, then Finish.

Nothing else needs configuring. Press **Ctrl+Alt+F10** in a game to save the last minute.

### The one thing you have to fetch yourself: the OBS WebSocket password

OBS generates a unique password per machine and won't accept connections without it, so it can't be
bundled. In OBS: **Tools → WebSocket Server Settings → Show Connect Info**, copy the password, paste it
into setup step 3.

### Prerequisites

- Windows 10/11, 64-bit.
- **OBS Studio 28 or newer** — install it first from [obsproject.com](https://obsproject.com). The
  installer warns (but doesn't stop) if it can't find OBS.
- No .NET install needed. The app ships its own runtime.

### What gets installed where

| What | Where | Removed on uninstall? |
|---|---|---|
| The app, `assets/`, bundled `ffmpeg` | `C:\Program Files\Clipping Software` | Yes |
| Settings, clip database, thumbnails, extracted audio | `%LocalAppData%\ClippingSoftware` | **No** |
| Your recordings and exports | `Videos\ClippingSoftware` (or wherever you chose) | **No** |

Uninstalling never deletes your clips or settings. Remove those folders by hand if you want them gone.

## For developers: building the installer

CI does this on every push (`.github/workflows/release.yml`) and the resulting `Setup.exe` is attached
as a build artifact. Pushing a `v*` tag also publishes it to a GitHub release.

To build one locally you need Windows, the .NET 8 SDK, and
[Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
# ffmpeg is not in the repo (too large, gitignored) - fetch it into the layout the build expects
mkdir tools\ffmpeg
# copy ffmpeg.exe and ffprobe.exe into tools\ffmpeg\

dotnet publish src/ClippingSoftware.App/ClippingSoftware.App.csproj `
  -c Release -r win-x64 --self-contained true -o publish

& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" `
  "/DSourceDir=$(Resolve-Path publish)" `
  "/DAppVersion=1.0.0" `
  "/DOutputDir=$(Join-Path (Get-Location) 'dist')" `
  installer\ClippingSoftware.iss
```

`dist\ClippingSoftware-Setup-1.0.0.exe` is the result.

### Why the app must be published, not just built

`assets/` and `tools/ffmpeg/` are copied into the build output by the App csproj, and the app resolves
them beside its own exe at runtime (`Core/BundledResources.cs`). Running from a `bin/` folder also
works because the resolver falls back to walking up to the repo root — but an installed copy has no
repo above it, so anything that ships must come through the published output. The workflow's
"Verify published layout" step fails the build if those files go missing.

### Cutting a release

1. Bump `<Version>` in `src/ClippingSoftware.App/ClippingSoftware.App.csproj`.
2. Commit, then tag: `git tag v1.0.1 && git push origin v1.0.1`.

The tag build overrides the csproj version with the tag name and publishes the release. The installer's
`AppId` GUID is fixed, so a new version upgrades an existing install in place rather than installing
alongside it.

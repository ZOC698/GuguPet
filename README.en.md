# GuguPet for Codex

[简体中文](README.md) | **English** | [日本語](README.ja.md)

`GuguPet for Codex` is a native Windows WPF desktop companion powered by the existing Codex Pet v2 Gugu spritesheet. It does not modify Codex or the original Pet files.

![Gugu avatar](Assets/gugu-icon.png)

## Demo

[![GuguPet for Codex action demo](docs/media/gugupet-for-codex-preview.png)](docs/media/gugupet-for-codex-demo.mp4)

Click the image to play a 16-second recording captured directly from the isolated Gugu window. It contains only Gugu on a solid background: no Codex session data, desktop, control panel, or other window is recorded.

> This is an unofficial fan-made project and is not affiliated with or endorsed by OpenAI, Codex, Bilibili, or the original character creators.

## Download and run

Download `GuguPet-Windows-x64-*.zip` from the repository's **Releases** page, extract it, and run `GuguPet.exe`. The current portable package is a self-contained Windows x64 build, so users do not need to install .NET separately.

The current release is not yet Authenticode-signed. Windows SmartScreen may display a warning on first launch. Download only from this repository and verify the SHA-256 published with the Release.

## Built-in behavior

- Idle animations
- Hover-triggered jump
- While dragged with the left mouse button, Gugu is lifted by the penguin hood and leans with the movement; every drag randomly uses either the normal face or a `><` squeezed-eye pose with both flipper sleeves waving, while autonomous roaming keeps the original walking animation
- First-launch wave
- A roughly five-second peephole entrance when GuguPet starts: waiting, pushing a box, climbing onto a suitcase, approaching the lens, and blinking. It can be disabled or previewed from the control panel, and skipped with Esc
- By default, startup shows only Gugu without opening the control panel. The panel-on-start option can be restored under the entrance-animation settings
- 16-direction mouse gaze
- Window-local coordinates and per-monitor DPI conversion for accurate gaze across displays with different scaling
- Codex state animations including `running`, `waiting`, `failed`, and `review`
- Mouse gaze is a low-priority idle reaction and returns to idle after an adjustable delay
- Read-only monitoring of `%USERPROFILE%\.codex\sessions` by default, reacting automatically to Codex start, completion, and error events
- Automatic connection to DSH's official local event interface (preferring `127.0.0.1:5556`, with `3080` compatibility). Once connected, WebSocket events drive updates without per-second process scans or compressed-log reads
- Safe progress summaries extracted from public `agent_message` events and shown in temporary bubbles beside Gugu
- The control panel combines the status, title, and public progress of up to eight Codex / DSH tasks without reading or displaying internal reasoning
- Codex and DSH have separate progress bubbles that can be visible simultaneously; clicking either bubble returns to its matching application
- Clicking a live progress bubble restores and activates the Codex window; if Codex is not running, GuguPet attempts to launch it from the Start menu
- Right-click to open the instant control panel
- Adjustable size, opacity, movement speed, always-on-top mode, and reduced motion
- Extra actions include playing guitar, listening with headphones, playing a compact drum kit, eating a cookie, three sleeping poses, input-needed, drinking water, stretching, sitting and thinking, plus dedicated head-pat and belly-guard reactions
- Preview extra actions instantly and configure their random idle interval from the control panel
- Music sync is disabled by default. When enabled, it uses Windows media-session events to read playback state plus the title, artist, and source exposed by the player. During playback Gugu alternates between headphones and drums; previous, play/pause, and next controls are available in the panel
- Music sync does not record or analyse audio and does not poll players every second. Codex / DSH activity, direct interaction, and manual actions take priority over music animation
- Random walking targets within the desktop work area, with an enable switch and adjustable speed
- While working, Gugu switches randomly between chin-resting (50%), spiral eyes (30%), and starry eyes (20%), with stable frame size and position
- When input is required, Gugu randomly waves or performs the raise-flipper, lean-in, tap, and wait sequence; on completion, Gugu randomly shows starry eyes, jumps, or eats a cookie
- During long-running work, Gugu briefly rests its chin, drinks water, or stretches before continuing to think; new progress makes Gugu look up before resuming
- Click the head for a pat and double-click the belly for a belly-guard reaction; releasing a drag applies decaying inertia
- Drag a cookie from the control panel onto Gugu; when the pointer approaches Gugu's feet, Gugu looks down to inspect it
- After prolonged inactivity, Gugu randomly sleeps on its side, lies prone, or sleeps on its back. Fast mouse chasing is disabled by default and can be enabled separately
- Optional screen-edge routine: walk to an edge, peek, choose side/prone/back sleep with equal probability, then climb back into the work area
- Dropping a file onto Gugu copies local file-handoff data and opens Codex; a bubble prompts the user to paste it into the input box
- Status bubbles provide input-needed and failure-handling buttons, completion summaries, priority ordering for up to eight tasks, and left/right navigation
- Gugu's context menu keeps the common “New Codex task,” “Open DSH,” “Feed cookie,” and “Open control panel” actions

## State bridge

“Auto sync” is enabled by default in the control panel. When Codex or DSH begins a task, Gugu switches to `running`; when input or approval is requested, to `waiting`; after completion, briefly to `review`; and on an explicit error, to `failed`. Codex integration reads session logs only. DSH integration connects only to `session.list` and two downlink event streams on the local loopback interface; it does not submit prompts, answer requests, read DSH credentials, or decode compressed logs. Neither integration reads or displays internal reasoning.

“Start Gugu with Codex (no Hook)” under System Integration is disabled by default. When enabled by the user, GuguPet registers a standard per-user Windows startup entry and waits for `ChatGPT.exe` in a hidden watcher mode. Gugu appears when the Codex desktop app starts. Disabling the option removes the startup entry and stops the watcher; it does not require editing `hooks.json` or trusting a command in Codex CLI.

The same section includes “Automatically download updates (confirm before installing),” which is disabled by default. When enabled, GuguPet checks this repository's latest stable GitHub Release every six hours. It downloads only the Windows x64 package, requires a matching SHA-256, and asks before installation. After confirmation, the separate updater keeps the previous program folder as a sibling rollback backup, swaps in the new package, and restarts GuguPet. Settings under `%LOCALAPPDATA%\GuguPet` are not overwritten. “Check for updates now” remains available without enabling automatic checks.

## Language packs

On first launch, the portable build reads the Windows UI language through `.NET CurrentUICulture`, then matches the full locale code followed by the language code. Built-in packs:

- `Locales/zh-CN.json`: Simplified Chinese
- `Locales/en-US.json`: English
- `Locales/ja-JP.json`: Japanese

Unmatched languages fall back to English. Users can override automatic selection under **System Integration → Language**; restart GuguPet to apply the change.

Adding a language requires no code changes: copy any JSON pack, edit `culture`, `displayName`, and `strings`, and use a standard locale code for the filename, such as `ko-KR.json`. Missing keys in non-Chinese packs fall back to English and then to the original Chinese string. Each pack is limited to 2 MB, and malformed files are ignored safely.

Other local programs can control GuguPet by editing `%LOCALAPPDATA%\GuguPet\bridge-state.json`:

```json
{
  "state": "running",
  "message": "Working on the task"
}
```

Supported states: `idle`, `running-right`, `running-left`, `waving`, `jumping`, `failed`, `waiting`, `running`, `review`, and `interrupted`.

Changes are hot-reloaded after the file is saved. Any local script can control the desktop companion by writing this file.

## Development

```powershell
dotnet run --project .\GuguPet.csproj
```

Maintainers can launch `--demo-capture` for an isolated, Gugu-only recording mode. It uses a separate instance and solid background while disabling session monitoring, the state bridge, random movement, and cursor gaze; it is not part of the normal user startup flow.

## Publishing

```powershell
.\scripts\build-release.ps1 -OutputDirectory artifacts
```

When updating the maintainer's local development install, use the full-package updater instead of copying only `GuguPet.dll`:

```powershell
.\scripts\update-local-install.ps1
```

It rebuilds and synchronizes the main app, launch watcher, standalone updater, locale packs, and self-contained runtime as one package. The install is atomically replaced and verified file by file; the desktop shortcut and an enabled watcher registration are refreshed, while the previous directory remains as a rollback backup.

## Privacy, security, and licensing

- [Privacy notice](PRIVACY.md)
- [Security policy](SECURITY.md)
- [Uninstall instructions](UNINSTALL.md)
- [Code signing policy](CODE_SIGNING_POLICY.md)
- Source code and the AI-generated project artwork are available under the [MIT License](LICENSE)
- See the [Asset notice](ASSET_NOTICE.md) for provenance and third-party-rights boundaries

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

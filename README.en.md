# GuguPet Desktop Companion

[简体中文](README.md) | **English** | [日本語](README.ja.md)

GuguPet is a native Windows WPF desktop companion powered by the existing Codex Pet v2 Gugu spritesheet. It does not modify Codex or the original Pet files.

![Gugu avatar](Assets/gugu-icon.png)

> This is an unofficial fan-made project and is not affiliated with or endorsed by OpenAI, Codex, Bilibili, or the original character creators.

## Download and run

Download `GuguPet-Windows-x64-*.zip` from the repository's **Releases** page, extract it, and run `GuguPet.exe`. The current portable package is a self-contained Windows x64 build, so users do not need to install .NET separately.

The current release is not yet Authenticode-signed. Windows SmartScreen may display a warning on first launch. Download only from this repository and verify the SHA-256 published with the Release.

## Built-in behavior

- Idle animations
- Hover-triggered jump
- Running left or right while dragged with the left mouse button
- First-launch wave
- A roughly five-second peephole entrance when GuguPet starts: waiting, pushing a box, climbing onto a suitcase, approaching the lens, and blinking. It can be disabled or previewed from the control panel, and skipped with Esc
- By default, startup shows only Gugu without opening the control panel. The panel-on-start option can be restored under the entrance-animation settings
- 16-direction mouse gaze
- Window-local coordinates and per-monitor DPI conversion for accurate gaze across displays with different scaling
- Codex state animations including `running`, `waiting`, `failed`, and `review`
- Mouse gaze is a low-priority idle reaction and returns to idle after an adjustable delay
- Read-only monitoring of `%USERPROFILE%\.codex\sessions` by default, reacting automatically to Codex start, completion, and error events
- Safe progress summaries extracted from public `agent_message` events and shown in temporary bubbles beside Gugu
- The control panel displays the status, title, and public progress of the eight most recent Codex tasks without reading or displaying internal reasoning
- Clicking a live progress bubble restores and activates the Codex window; if Codex is not running, GuguPet attempts to launch it from the Start menu
- Right-click to open the instant control panel
- Adjustable size, opacity, movement speed, always-on-top mode, and reduced motion
- Extra actions include playing guitar, eating a cookie, three sleeping poses, input-needed, drinking water, stretching, sitting and thinking, plus dedicated head-pat and belly-guard reactions
- Preview extra actions instantly and configure their random idle interval from the control panel
- Random walking targets within the desktop work area, with an enable switch and adjustable speed
- While working, Gugu switches randomly between chin-resting (50%), spiral eyes (30%), and starry eyes (20%), with stable frame size and position
- When input is required, Gugu randomly waves or performs the raise-flipper, lean-in, tap, and wait sequence; on completion, Gugu randomly shows starry eyes, jumps, or eats a cookie
- During long-running work, Gugu briefly rests its chin, drinks water, or stretches before continuing to think; new progress makes Gugu look up before resuming
- Click the head for a pat and double-click the belly for a belly-guard reaction; releasing a drag applies decaying inertia
- Drag a cookie from the control panel onto Gugu; when the pointer approaches Gugu's feet, Gugu looks down to inspect it
- After prolonged inactivity, Gugu randomly sleeps on its side, lies prone, or sleeps on its back. Fast mouse chasing is disabled by default and can be enabled separately
- Optional screen-edge routine: walk to an edge, peek, settle down, then climb back into the work area
- Dropping a file onto Gugu copies local file-handoff data and opens Codex; a bubble prompts the user to paste it into the input box
- Status bubbles provide input-needed and failure-handling buttons, completion summaries, priority ordering for up to eight tasks, and left/right navigation
- Below “New Codex task,” Gugu's context menu lists the three most recent primary tasks and their states. Clicking returns to Codex; the current public interface cannot deep-link to an exact task

## State bridge

“Automatically follow Codex work status” is enabled by default in the control panel. When Codex begins a task, Gugu switches to `running`; when input is requested, to `waiting`; after completion, briefly to `review`; and on an explicit error, to `failed`. This feature reads session logs only. It does not modify Codex data or read or display internal reasoning.

“Start Gugu with Codex (no Hook)” under System Integration is disabled by default. When enabled by the user, GuguPet registers a standard per-user Windows startup entry and waits for `ChatGPT.exe` in a hidden watcher mode. Gugu appears when the Codex desktop app starts. Disabling the option removes the startup entry and stops the watcher; it does not require editing `hooks.json` or trusting a command in Codex CLI.

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

Supported states: `idle`, `running-right`, `running-left`, `waving`, `jumping`, `failed`, `waiting`, `running`, and `review`.

Changes are hot-reloaded after the file is saved. Any local script can control the desktop companion by writing this file.

## Development

```powershell
dotnet run --project .\GuguPet.csproj
```

## Publishing

```powershell
dotnet publish .\GuguPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Privacy, security, and licensing

- [Privacy notice](PRIVACY.md)
- [Security policy](SECURITY.md)
- [Uninstall instructions](UNINSTALL.md)
- [Code signing policy](CODE_SIGNING_POLICY.md)
- Source code and the AI-generated project artwork are available under the [MIT License](LICENSE)
- See the [Asset notice](ASSET_NOTICE.md) for provenance and third-party-rights boundaries

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

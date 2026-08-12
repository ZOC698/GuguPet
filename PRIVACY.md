# Privacy

GuguPet runs locally on Windows and does not include an analytics service,
advertising SDK, account system, or telemetry upload endpoint.

When Codex integration is enabled, GuguPet reads local Codex session metadata
under `%USERPROFILE%\.codex\sessions` to derive task state, titles, and public
progress messages. It does not intentionally display internal reasoning. The
desktop pet can also read `%LOCALAPPDATA%\GuguPet\bridge-state.json` when a
local program uses the bridge interface.

Optional features can register a per-user Windows startup entry, observe when
the Codex desktop process starts, copy a file dropped on the pet into local
handoff data, or open the Codex desktop application. These features are local,
user-controlled, and disabled where stated in the interface.

Users should review the source and settings before enabling integrations. Bug
reports should not include session logs, credentials, private prompts, or other
sensitive local files.

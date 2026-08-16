# Privacy

GuguPet runs locally on Windows and does not include an analytics service,
advertising SDK, account system, or telemetry upload endpoint.

When Codex integration is enabled, GuguPet reads local Codex session metadata
under `%USERPROFILE%\.codex\sessions` to derive task state, titles, and public
progress messages. It does not intentionally display internal reasoning. The
desktop pet can also read `%LOCALAPPDATA%\GuguPet\bridge-state.json` when a
local program uses the bridge interface.

When a local DeepSeek Harness Web service is available, GuguPet connects only
to a loopback address on `127.0.0.1`. Because Gugu DSH may choose a free port
at launch, GuguPet checks the command line of matching local DSH backend
processes only to discover that port; `5556` and `3080` remain compatibility
fallbacks. It uses DSH's
read-only `session.list`, host-event, and mux-event surfaces to derive top-level
task state, title, input/approval waits, errors, and public assistant text. It
does not store or upload process command lines, read DSH credential files,
decode the compressed session store, submit
prompts, answer requests, or send these values to an external server. Codex and
DSH progress bubbles are kept separate and open only their matching local app.

Optional features can register a per-user Windows startup entry, observe when
the Codex desktop process starts, copy a file dropped on the pet into local
handoff data, or open the Codex desktop application. These features are local,
user-controlled, and disabled where stated in the interface.

Users should review the source and settings before enabling integrations. Bug
reports should not include session logs, credentials, private prompts, or other
sensitive local files.

# Uninstalling GuguPet

GuguPet is portable and does not install a system service.

1. Open the control panel and turn off **Start GuguPet with Windows** and
   **Start GuguPet with Codex** if either option is enabled.
2. Exit GuguPet from its tray icon or context menu.
3. Delete the extracted GuguPet folder.
4. Optional: delete `%LOCALAPPDATA%\GuguPet` to remove settings, the fixed
   Codex launch watcher, local bridge state, update downloads, and the update
   log.
5. Optional: after verifying the new version works, delete the sibling
   `GuguPet-rollback` folder that contains the single `previous` rollback copy.

If the program folder was deleted before the startup options were disabled,
remove the per-user values `GuguPet` and `GuguPet.CodexWatcher` from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No administrator access
is required.

# Uninstalling GuguPet

GuguPet is portable and does not install a system service.

1. Open the control panel and turn off **Start GuguPet with Windows** and
   **Start GuguPet with Codex** if either option is enabled.
2. Exit GuguPet from its tray icon or context menu.
3. Delete the extracted GuguPet folder.
4. Optional: delete `%LOCALAPPDATA%\GuguPet` to remove settings and local bridge
   state.

If the program folder was deleted before the startup options were disabled,
remove the per-user values `GuguPet` and `GuguPet.CodexWatcher` from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No administrator access
is required.

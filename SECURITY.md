# Security policy

Please report security issues privately through GitHub's private vulnerability
reporting feature when it is available for this repository. Do not include
tokens, private Codex sessions, passwords, or other personal data in a public
issue.

The portable Windows release is currently distributed without an Authenticode
signature. Verify the SHA-256 checksum shown in the GitHub Release before
running it. Code-signing automation is planned, but no unsigned build should be
presented as digitally signed.

The optional updater is disabled by default. It accepts only a newer stable
release from the official GitHub Releases endpoint, downloads the Windows x64
archive, and requires the archive's SHA-256 digest or the matching published
checksum before offering installation. It asks for confirmation, keeps the old
program directory as a rollback backup, and does not overwrite settings in
`%LOCALAPPDATA%\GuguPet`. A checksum confirms package integrity but does not
replace Authenticode publisher verification.

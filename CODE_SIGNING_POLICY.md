# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate
by [SignPath Foundation](https://signpath.org/).

## Scope

Official Windows releases are built from the public
[`ZOC698/GuguPet`](https://github.com/ZOC698/GuguPet) repository. After the
SignPath Foundation application is approved, release artifacts produced by the
repository's GitHub Actions workflow will be submitted to SignPath. Local or
unverifiable binaries are not eligible for official signing.

The signed project-owned portable-executable files are:

- `GuguPet.exe`
- `GuguPet.dll`
- `GuguPet.LaunchWatcher.exe`

Third-party and Microsoft runtime files are not re-signed as GuguPet files.
Every signed release must be timestamped and verified before publication.

## Team roles

- Committer and reviewer: [ZOC698](https://github.com/ZOC698)
- Approver: [ZOC698](https://github.com/ZOC698)

Changes submitted by other contributors require review by the maintainer.
Every signing request requires manual approval by the approver.

## Privacy

See the project [privacy notice](PRIVACY.md). GuguPet itself does not transfer
information to networked systems unless the user explicitly requests an action
that opens or hands data to another application. Codex and other applications
have their own privacy terms and are not operated by this project.

## Verification

On Windows, inspect a downloaded executable in **Properties → Digital
Signatures**, or run:

```powershell
Get-AuthenticodeSignature .\GuguPet.exe | Format-List Status,StatusMessage,SignerCertificate,TimeStamperCertificate
```

The signature must report `Valid`, identify SignPath Foundation as the signing
publisher, and contain a trusted timestamp. Release SHA-256 checksums are
published separately so the complete archive can also be verified.

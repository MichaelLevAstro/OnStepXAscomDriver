# com0com Bundled Binaries

This directory ships the **com0com** virtual null-modem driver alongside the
OnStepX installer so the NINA TPPA OAPA bridge works out-of-the-box without
the user manually installing com0com.

The binaries are **not** committed to the repository — they're a third-party
redistributable. Download a signed build and drop the contents here before
running `build-installer.cmd` for a production release.

## What to drop here

A signed com0com 3.x distribution. Required files at minimum:

- `setupc.exe` — CLI driver setup tool the OnStepX installer + Hub UI invoke
- `setup.exe` — GUI variant (optional, but ships in the upstream package)
- `com0com.inf` — driver INF
- `com0com.cat` — driver catalog (signature)
- `i386\com0com.sys` — 32-bit kernel driver
- `amd64\com0com.sys` — 64-bit kernel driver
- `ReadMe.txt`, `disable_pnp.cmd`, `enable_pnp.cmd` — keep upstream support files
- `com0com-LICENSE.txt` — GPL license text (rename from upstream `COPYING` if needed)

## Sources

Use a build that's signed for current Windows 11 driver-signing policy.
The classic Pete Batard signed packages are commonly used; verify the
signature catalog is still accepted on a clean Win11 machine before
shipping.

- Project home: <https://com0com.sourceforge.net/>
- Release source: <https://files.com0com.com/>

## How the installer uses these files

`installer\OnStepX.AscomDriver.iss` ships everything in this directory to
`<install>\com0com\` (preserving subdirectories) at install time, then
calls `setupc.exe install PortName=COM<N> PortName=COM<N+1>` to create
the default Hub-managed pair. The Hub later invokes `setupc.exe list /
install / remove` from this same directory in response to PA Advanced
popup actions.

If `setupc.exe` is missing here when ISCC runs, the installer compiles
*without* the bundled driver (compile-time `#if FileExists` guard) and
the Hub's pair management UI degrades gracefully ("com0com not installed").

## License

com0com is GPL-licensed. Bundling the upstream signed binaries
unmodified is permitted under GPL §3 redistribution terms — keep
`com0com-LICENSE.txt` here so it ships to end users.

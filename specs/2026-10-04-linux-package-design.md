# Linux support, step 2: a Linux package in release 1.5.0.0

Date: 2026-10-04. Status: approved design, amended after the final review (udev rule with final assignments, install.sh
checks, service hardening, invariant globalization, release token scope and dry run).

## Context

- Step 1 is merged (PR #7): the `net10.0` target of Canon.API runs on Linux x64 from the sources, verified with an EOS
  R100. See `specs/2026-10-04-linux-edsdk-design.md`.
- Release 1.5.0.0 must ship a Linux build next to the Windows package.
- Today `.github/workflows/build.yml` runs one Windows job on a `v*` tag: tag check, version sync, build, tests,
  `CanonWebAPI.zip` (win-x64), GitHub release, then `docs/autoupdate.xml`, which the photo booths read to update
  themselves. The Windows package name, content and feed must not change.
- Decisions: x64 only; a `.tar.gz` usable by hand plus an optional `install.sh` (systemd service, USB permissions);
  manual updates on Linux; an atomic release pipeline.
- Verified on the development machine (Ubuntu 26.04): `dotnet publish -f net10.0 -r linux-x64 --self-contained true
  -p:PublishSingleFile=true` produces an executable `Canon.API` that needs no .NET native library next to it, only
  `libEDSDK.so`. Without a camera, `/status` answers `"error": "No Canon camera detected: Device not found"`. SIGTERM
  stops it cleanly in 2 s (the camera session is closed). The single file requires glibc 2.27 or later; `libEDSDK.so`
  requires glibc 2.14 and libstdc++ (GLIBCXX 3.4.21), plus `libusb-1.0` and `libudev`.

## Goals

- Every release has a second asset, `CanonWebAPI-linux-x64.tar.gz`, built and checked in the same workflow as the
  Windows package.
- On Linux the API runs by hand from the extracted folder, or as a systemd service installed by `install.sh`.
- A release is published only when both packages built, passed the tests, and started and loaded the EDSDK.
- The pull request CI starts both packages too (the published Windows executable was never run in CI).
- Release 1.5.0.0 is prepared (version, upgrade note, changelog); the tag is pushed only with the user's go-ahead.

## Non-goals

ARM (arm64, arm32), automatic update on Linux, `.deb` or other distribution packages, a Linux camera test script
(`tools/Test-Camera.ps1` needs PowerShell), Docker images for Linux.

## Design

### 1. The Linux package

- Asset: `CanonWebAPI-linux-x64.tar.gz`, containing one folder `CanonWebAPI/`:
  - `Canon.API` (self-contained single-file executable), `libEDSDK.so`, `appsettings.json`,
    `appsettings.Development.json`, the `.pdb` files and `Canon.API.staticwebassets.endpoints.json` (as in the
    Windows package);
  - `install.sh`, `canonwebapi.service`, `60-canonwebapi.rules` (section 2).
- Built with `dotnet publish Canon.API/Canon.API.csproj -c Release -f net10.0 -r linux-x64 --self-contained true
  -p:PublishSingleFile=true`. `EnableCompressionInSingleFile` applies to Linux as to Windows, and the `net10.0` target
  uses `InvariantGlobalization`: the API only formats with the invariant culture, so no ICU library is needed. No
  `IncludeNativeLibrariesForSelfExtract`: nothing would be extracted (verified above), and a service user has no home
  directory to extract to.
- The package content is defined in `Canon.API.csproj`, like the Windows DLLs: for the `net10.0` target, the files of
  `packaging/linux/` are copied to the publish folder only (not to the build output). `install.sh` keeps its
  executable bit through the tar archive, which is created on Linux.
- Run by hand: `./Canon.API` listens on `http://localhost:5000`, like the Windows executable; `--urls` or
  `ASPNETCORE_URLS` change it. Needs `libusb-1.0`, read/write access to the camera USB device (desktop session, or the
  udev rule of `install.sh`), and no other program holding the camera.
- The Windows package is unchanged.

### 2. `install.sh`

Run as root from the extracted folder: `sudo ./install.sh` installs or updates, `sudo ./install.sh --uninstall`
removes. Files in `packaging/linux/`: `install.sh`, `canonwebapi.service`, `60-canonwebapi.rules`.

Install or update:

1. Checks, before anything changes: root, systemd running as init (`/run/systemd/system`), `x86_64`,
   `libusb-1.0.so.0` known to `ldconfig`, and the package files next to the script (`Canon.API`, `libEDSDK.so`,
   `canonwebapi.service`, `60-canonwebapi.rules`; the installed copy in `/opt/canonwebapi` only uninstalls). When
   libusb is missing, prints `sudo apt install libusb-1.0-0` and stops (no package installed by the script).
2. Creates the system user `canonwebapi` when missing: no login shell, no home directory.
3. Stops the service when it runs (update).
4. Copies the program files to `/opt/canonwebapi`, owned by root. `logs/` is owned by `canonwebapi`. An existing
   `appsettings.Local.json` is never overwritten or removed; none is created.
5. Installs `/etc/udev/rules.d/60-canonwebapi.rules`, reloads udev, replays an `add` event for the Canon USB devices
   and waits for udev (`udevadm settle`). The rule gives Canon PTP cameras (interface `06/01/01`) `MODE:="0660"`,
   `GROUP:="canonwebapi"`: final assignments, because `60-libgphoto2*.rules` sorts after it and sets `MODE="0664",
   GROUP="plugdev"` on `add` and `bind` (it skips `change` events, so a `change` trigger would hide the problem until
   the next replug or reboot). Canon printers and scanners are untouched. Desktop users keep their own access (the
   `uaccess` ACL is separate from the group).
6. Installs `/etc/systemd/system/canonwebapi.service`, reloads systemd, enables and (re)starts the service, waits 3 s
   and fails, pointing to `journalctl -u canonwebapi`, when it is not active; then prints the URL and its status.

Service unit:

```ini
[Unit]
Description=CanonWebAPI (Canon camera web API)
After=network.target

[Service]
User=canonwebapi
Group=canonwebapi
WorkingDirectory=/opt/canonwebapi
ExecStart=/opt/canonwebapi/Canon.API
Restart=on-failure
RestartSec=5
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=full
ProtectHome=yes

[Install]
WantedBy=multi-user.target
```

No `PrivateDevices` (USB camera), `MemoryDenyWriteExecute` (JIT) or `RestrictAddressFamilies` (hot plug uses netlink).
`ProtectSystem=full` leaves `/opt` writable for the logs. A fatal startup error (port in use, invalid
`appsettings.Local.json`) exits with code 1, so `Restart=on-failure` applies.

systemd stops it with SIGTERM: ASP.NET Core shuts down cleanly and closes the camera session. Logs: the API files in
`/opt/canonwebapi/logs/` and `journalctl -u canonwebapi` (console output).

Uninstall: stops and disables the service, removes the unit and the udev rule (then replays the `add` event, so a
plugged camera gets its default ownership back), `/opt/canonwebapi` (settings and logs included) and the
`canonwebapi` user.

Update: extract the new archive, run its `install.sh`.

### 3. Release workflow and CI

A PowerShell script, `tools/Test-Package.ps1`, checks a published package: it starts the executable with
`--urls http://localhost:5000 --AutoUpdate:Enabled=false` from a temporary copy of its folder (its logs stay out of
the package), polls `GET /status` for up to 60 s, and passes when the answer is HTTP 200 with `connected: true` or an
`error` containing `No Canon camera detected` (the runners have no camera; a missing or unloadable `EDSDK.dll` /
`libEDSDK.so` gives another error). It stops the executable and prints its output on failure.
PowerShell 7 is installed on the GitHub Windows and Ubuntu runners.

`.github/workflows/build.yml` (tag `v*`), jobs:

1. `prepare` (ubuntu-latest): the current tag check and version sync steps (PowerShell, `shell: pwsh`); outputs the
   commit to build and the version.
2. `windows` (windows-latest) and `linux` (ubuntu-latest), in parallel, both on the prepared commit: restore, build,
   test, publish their runtime, `Test-Package.ps1`, then archive (`CanonWebAPI.zip` with `Compress-Archive` as today,
   `CanonWebAPI-linux-x64.tar.gz` with `tar`, files owned by root in the archive) and upload it as a workflow
   artifact. Both time out after 30 minutes.
3. `release` (ubuntu-latest), when both succeeded: downloads the two archives, creates the GitHub release with both
   (generated notes, as today), then writes and commits `docs/autoupdate.xml` (Windows zip URL and UTF-8 BOM, as
   today).

Token scope: `contents: read` for the workflow; only `prepare` and `release` get `contents: write`. The build jobs run
NuGet build logic and the tests, so their checkout does not persist credentials.

When the Windows or Linux job fails, nothing is released and the feed is not updated. When the `release` job fails
after creating the release, the feed is not updated and the booths stay on the previous version: rerun the failed job.
The workflow can also be run by hand from the Actions tab (`workflow_dispatch`): a dry run of `prepare` (no tag check,
no version commit), `windows`, `linux` and `release` (downloads both packages and generates the XML) that creates no
release and pushes nothing, to check the pipeline on `main` before tagging.

`.github/workflows/ci.yml` (pull requests and `main`): the Windows job also publishes win-x64 and runs
`Test-Package.ps1`; a new Linux job (ubuntu-latest) restores, builds, tests, publishes linux-x64 and runs
`Test-Package.ps1`.

### 4. Documentation

- README:
  - an "Installing on Linux" section: download and extract, prerequisites (`libusb-1.0`, glibc 2.27+, systemd for the
    service), run by hand, `install.sh` (what it installs), update, uninstall, settings in
    `/opt/canonwebapi/appsettings.Local.json`, logs, `systemctl status|restart canonwebapi`, `journalctl -u
    canonwebapi`, and the desktop automount note (gvfs can hold the camera);
  - "Running on Linux" stays the from-source section;
  - System Requirements and the release steps mention both packages.
- CLAUDE.md: the two packages, `packaging/linux/`, `tools/Test-Package.ps1`, the release pipeline jobs.
- CHANGELOG (Unreleased): Linux package and `install.sh`; release pipeline and CI check both packages.

### 5. Release 1.5.0.0

Last step, after the implementation:

- Version 1.5.0.0 in `Canon.API/Canon.API.csproj`, the README ("Current version") and `docs/openapi.json`.
- README "Upgrading from 1.4.x" note: the flash setting is no longer written while the live view runs (a value set with
  `POST /flash` stays until the live view stops); `dotnet run` and `dotnet publish` need `-f`; Linux package.
- CHANGELOG: `[Unreleased]` becomes `[1.5.0.0] - <date of the release commit>`, with a new empty `[Unreleased]` and updated links.
- The tag `v1.5.0.0` is pushed only with the user's go-ahead: it updates every photo booth.

## Testing

- Pull request CI: both packages start and load the EDSDK (`Test-Package.ps1`), on Windows and Linux runners.
- Development machine: build the Linux package with the release commands and extract it; run `./Canon.API` by hand;
  run `install.sh` (sudo, by the user), check the service, the udev rule and the logs; camera test with the R100
  against the service (captures with the live view, flash change, power cycle); `install.sh --uninstall`.

## Risks

- The GitHub Ubuntu runner may not let the EDSDK initialize (no USB). `/status` would then report another error and
  `Test-Package.ps1` would fail on the pull request, before any release: the expected message is adjusted there, still
  rejecting library load failures.
- On a desktop, gvfs can mount the camera and hold it: documented (unmount it or disable the automount).
- `install.sh` changes the system (user, `/opt`, udev rule, service): it is tested on the development machine with
  the user, then uninstalled.

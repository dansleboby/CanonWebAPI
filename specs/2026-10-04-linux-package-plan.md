# Linux Package (Release 1.5.0.0) - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `CanonWebAPI-linux-x64.tar.gz` (with an optional systemd installer) next to the Windows package in every release, both built and checked by an atomic release pipeline, then prepare release 1.5.0.0.

**Architecture:** The package content is defined in `Canon.API.csproj` for the `net10.0` target (publish-only files from `packaging/linux/`). `tools/Test-Package.ps1` starts a published package and checks that it loads the EDSDK; the CI and release workflows run it on Windows and Linux runners. The release workflow becomes prepare -> windows + linux (parallel) -> release.

**Tech Stack:** .NET 10 (SDK 10.0.112 here), GitHub Actions (windows-latest, ubuntu-latest), PowerShell 7 (`pwsh`, on both runners), bash, systemd, udev.

**Spec:** `specs/2026-10-04-linux-package-design.md`

## Global Constraints

- Branch `feature/linux-package`. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- The Windows package does not change: asset `CanonWebAPI.zip`, same content, same `docs/autoupdate.xml` content and URL.
- Linux asset: `CanonWebAPI-linux-x64.tar.gz`, one top folder `CanonWebAPI/`. x64 only.
- Linux publish command: `dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true`.
- Install locations: `/opt/canonwebapi`, user and group `canonwebapi`, `/etc/systemd/system/canonwebapi.service`, `/etc/udev/rules.d/60-canonwebapi.rules`. API URL `http://localhost:5000`.
- Package check: `/status` answers with `connected: true` or an `error` containing `No Canon camera detected`.
- Code comments in English, senior altitude (only what the code cannot say). ASCII punctuation in code, comments, scripts and docs.
- Every change is recorded in the `Unreleased` section of `CHANGELOG.md`.
- PowerShell 7 for local checks: `/tmp/claude-1000/-home-gilbert-Documents-Project-CanonWebAPI/055083d5-ab07-4905-b425-34b4a1057fc9/scratchpad/pwsh/pwsh` (not installed system wide).
- Verified before this plan: with the Task 1 project settings, `dotnet build CanonSDK.sln` has 0 warnings, the `packaging/linux` files are not copied to the build output, and the linux-x64 publish folder contains `Canon.API` (executable, 53 MB compressed), `libEDSDK.so`, the three packaging files (`install.sh` keeps its executable bit), `appsettings*.json`, `*.pdb`, `Canon.API.staticwebassets.endpoints.json`.

## File Structure

| File | Change |
|------|--------|
| `Canon.API/Canon.API.csproj` | Linux publish settings and packaging items for `net10.0` |
| `packaging/linux/install.sh` | New: install, update, uninstall (executable) |
| `packaging/linux/canonwebapi.service` | New: systemd unit |
| `packaging/linux/60-canonwebapi.rules` | New: udev rule |
| `tools/Test-Package.ps1` | New: starts a published package and checks `/status` |
| `.github/workflows/ci.yml` | Windows job checks its package; new Linux job |
| `.github/workflows/build.yml` | prepare / windows / linux / release jobs; manual dry run |
| `README.md`, `CLAUDE.md`, `CHANGELOG.md` | Linux installation, CI, release pipeline; release 1.5.0.0 |
| `docs/openapi.json` | Version 1.5.0 (Task 6) |

---

### Task 1: Linux package content and installer

**Files:**
- Create: `packaging/linux/install.sh`, `packaging/linux/canonwebapi.service`, `packaging/linux/60-canonwebapi.rules`
- Modify: `Canon.API/Canon.API.csproj`, `README.md`, `CHANGELOG.md`

**Interfaces:**
- Produces: a linux-x64 publish folder containing `Canon.API`, `libEDSDK.so`, `install.sh` (executable), `canonwebapi.service`, `60-canonwebapi.rules` at its root. `install.sh` usage: `sudo ./install.sh` (install or update), `sudo ./install.sh --uninstall`.

- [ ] **Step 1: Check that the packaging files are not published yet**

```bash
O=$(mktemp -d)
dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true --output "$O"
ls "$O"
```

Expected: no `install.sh`, `canonwebapi.service` or `60-canonwebapi.rules`; `Canon.API` is about 100 MB (not compressed).

- [ ] **Step 2: The systemd unit**

Create `packaging/linux/canonwebapi.service`:

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

[Install]
WantedBy=multi-user.target
```

- [ ] **Step 3: The udev rule**

Create `packaging/linux/60-canonwebapi.rules`:

```
# CanonWebAPI: Canon cameras (USB vendor 04a9) can be opened by the canonwebapi service user.
SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_device", ATTR{idVendor}=="04a9", MODE="0660", GROUP="canonwebapi"
```

- [ ] **Step 4: The installer**

Create `packaging/linux/install.sh`, then `chmod 755 packaging/linux/install.sh` (git records the executable bit):

```bash
#!/usr/bin/env bash
# Installs or updates CanonWebAPI as a systemd service from an extracted Linux package, or removes it.
#   sudo ./install.sh               install or update (appsettings.Local.json and the logs are kept)
#   sudo ./install.sh --uninstall   remove the service, the udev rule, /opt/canonwebapi and the canonwebapi user
set -euo pipefail

readonly app_dir=/opt/canonwebapi
readonly service=canonwebapi
readonly user=canonwebapi
readonly unit_file=/etc/systemd/system/canonwebapi.service
readonly rule_file=/etc/udev/rules.d/60-canonwebapi.rules
package_dir="$(cd "$(dirname "$0")" && pwd)"
readonly package_dir

fail() {
    echo "Error: $*" >&2
    exit 1
}

uninstall() {
    systemctl disable --now "$service" 2>/dev/null || true
    rm -f "$unit_file" "$rule_file"
    systemctl daemon-reload
    udevadm control --reload
    rm -rf "$app_dir"
    if id "$user" >/dev/null 2>&1; then
        userdel "$user"
    fi
    echo "CanonWebAPI removed."
}

install_package() {
    [ "$(uname -m)" = x86_64 ] || fail "this package is for x86_64, not $(uname -m)."
    # No grep -q: with pipefail, grep exiting early would fail the pipeline through SIGPIPE.
    ldconfig -p | grep 'libusb-1.0.so.0' >/dev/null || fail "libusb-1.0 is missing: sudo apt install libusb-1.0-0"
    [ -f "$package_dir/Canon.API" ] || fail "Canon.API not found next to the script: run it from the extracted package."
    [ "$package_dir" != "$app_dir" ] || fail "run the install.sh of the extracted package, not the installed one."

    if ! id "$user" >/dev/null 2>&1; then
        useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin "$user"
    fi

    systemctl stop "$service" 2>/dev/null || true

    install -d -o root -g root -m 755 "$app_dir"
    install -d -o "$user" -g "$user" -m 755 "$app_dir/logs"

    # Program files only: appsettings.Local.json and the logs of the installation are kept.
    for file in "$package_dir"/*; do
        name="$(basename "$file")"
        case "$name" in
            appsettings.Local.json | canonwebapi.service | 60-canonwebapi.rules) continue ;;
        esac
        [ -f "$file" ] || continue
        install -o root -g root -m 644 "$file" "$app_dir/$name"
    done
    chmod 755 "$app_dir/Canon.API" "$app_dir/install.sh"

    install -o root -g root -m 644 "$package_dir/60-canonwebapi.rules" "$rule_file"
    udevadm control --reload
    # Applies the rule to a camera that is already plugged in.
    udevadm trigger --subsystem-match=usb --attr-match=idVendor=04a9

    install -o root -g root -m 644 "$package_dir/canonwebapi.service" "$unit_file"
    systemctl daemon-reload
    systemctl enable "$service"
    systemctl restart "$service"

    echo "CanonWebAPI is installed in $app_dir and runs as the $service service: http://localhost:5000/status"
    echo "Settings: $app_dir/appsettings.Local.json. Logs: $app_dir/logs and journalctl -u $service"
    systemctl --no-pager status "$service" || true
}

[ "$(id -u)" -eq 0 ] || fail "run it as root: sudo $0 $*"
command -v systemctl >/dev/null || fail "systemd is required."

case "${1:-}" in
    "") install_package ;;
    --uninstall) uninstall ;;
    *) fail "unknown option $1. Usage: sudo $0 [--uninstall]" ;;
esac
```

- [ ] **Step 5: Publish settings and packaging items**

In `Canon.API/Canon.API.csproj`, after the `PropertyGroup` conditioned on `net10.0-windows` (Self-Contained Single-File Deployment), add:

```xml
  <!-- Linux package (self-contained single file for linux-x64). No native library self-extraction: none is needed,
       and a service user has no home directory to extract to. -->
  <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
```

(XML comments cannot contain `--`: do not put command line options in them.)

After the `ItemGroup` that copies `..\EDSDK\linux-x64\libEDSDK.so` for `net10.0`, add:

```xml
  <!-- Linux package: installer, systemd service and udev rule next to the executable (publish only). -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <None Include="..\packaging\linux\*" Link="%(Filename)%(Extension)" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
  </ItemGroup>
```

- [ ] **Step 6: Check the package content**

```bash
dotnet build CanonSDK.sln
ls Canon.API/bin/Debug/net10.0/ | grep -c -E 'install.sh|canonwebapi'   # 0: publish only
O=$(mktemp -d)
dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true --output "$O"
ls -l "$O"
bash -n packaging/linux/install.sh && echo "syntax OK"
dotnet test CanonSDK.sln
```

Expected: build 0 warnings, 0 errors; the grep count is 0; the publish folder lists `Canon.API` (executable, about 53 MB), `libEDSDK.so`, `install.sh` (executable), `canonwebapi.service`, `60-canonwebapi.rules`, `appsettings.json`, `appsettings.Development.json`, the `.pdb` files and `Canon.API.staticwebassets.endpoints.json`; `syntax OK`; tests `Failed: 0`. If `shellcheck` can be installed without root (e.g. `pip install --user shellcheck-py`), run `shellcheck packaging/linux/install.sh` and fix its warnings; otherwise say so in the report.

- [ ] **Step 7: Documentation**

`README.md`: after the "## Automatic Updates" section (and its last line "Linux has no automatic update yet..."), add:

````markdown
## Installing on Linux

Each release also provides `CanonWebAPI-linux-x64.tar.gz`: a self-contained executable for Linux x64 (no .NET to
install) with the Linux version of the EDSDK. Requirements: glibc 2.27 or later (e.g. Ubuntu 18.04, Debian 10 or
later), `libusb-1.0` (`sudo apt install libusb-1.0-0`), and systemd for the service. There is no automatic update on
Linux.

```bash
tar -xzf CanonWebAPI-linux-x64.tar.gz
cd CanonWebAPI
./Canon.API          # by hand: http://localhost:5000
sudo ./install.sh    # or as a service started at boot
```

`install.sh` copies the files to `/opt/canonwebapi`, creates the `canonwebapi` system user, adds a udev rule that lets
it open Canon cameras (USB vendor `04a9`), then installs, enables and starts the `canonwebapi` systemd service.

*   Settings: `/opt/canonwebapi/appsettings.Local.json` (kept by updates), then `sudo systemctl restart canonwebapi`.
*   Logs: `/opt/canonwebapi/logs/` and `journalctl -u canonwebapi`; state: `systemctl status canonwebapi`.
*   Update: extract the new archive and run its `install.sh`.
*   Uninstall: `sudo /opt/canonwebapi/install.sh --uninstall` removes the service, the udev rule, `/opt/canonwebapi`
    (settings and logs included) and the `canonwebapi` user.
*   Run by hand, the API needs read/write access to the camera USB device: desktop sessions get it, otherwise install
    the service. A desktop can mount the camera (GNOME: gvfs) and hold it: unmount it (`gio mount -l`, then
    `gio mount -u <location>`) or disable the automount.
````

In "### Running on Linux", replace "There is no automatic update and no release package for Linux yet: run it from the sources." with "To run a release, see [Installing on Linux](#installing-on-linux); this section runs it from the sources."

In "### System Requirements", replace the OS line with:

```markdown
*   **OS**: Windows 10/11 (x64): `CanonWebAPI.zip`; Linux x64 (glibc 2.27+, `libusb-1.0`): `CanonWebAPI-linux-x64.tar.gz` (see [Installing on Linux](#installing-on-linux))
```

`CHANGELOG.md`, `## [Unreleased]` / `### Added`, first bullet:

```markdown
- Linux package in every release: `CanonWebAPI-linux-x64.tar.gz`, a self-contained executable for Linux x64 with the
  Linux EDSDK, and `install.sh`, which installs it as a systemd service with the USB access it needs
  (`/opt/canonwebapi`, `canonwebapi` user, udev rule); `install.sh --uninstall` removes it.
```

- [ ] **Step 8: Commit**

```bash
git add packaging/linux Canon.API/Canon.API.csproj README.md CHANGELOG.md
git commit -m "Add the Linux package content and its systemd installer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git ls-files -s packaging/linux/install.sh   # mode 100755
```

---

### Task 2: Package check script and CI on Windows and Linux

**Files:**
- Create: `tools/Test-Package.ps1`
- Modify: `.github/workflows/ci.yml`, `CLAUDE.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: the Linux publish folder of Task 1.
- Produces: `./tools/Test-Package.ps1 -Executable <path to Canon.API or Canon.API.exe> [-Url http://localhost:5000] [-TimeoutSeconds 60]`; exit code 0 when the package loads the EDSDK, 1 otherwise. Used by Task 3.

- [ ] **Step 1: The script**

Create `tools/Test-Package.ps1`:

```powershell
<#
.SYNOPSIS
    Checks that a published CanonWebAPI package starts and loads the Canon EDSDK.

.DESCRIPTION
    Copies the folder of the executable to a temporary folder, so that its logs do not end up in the package, starts
    it, polls GET /status and passes when the API answers with a connected camera or "No Canon camera detected" (no
    camera on the machine). Any other answer, such as an EDSDK library that cannot be loaded, fails. Used by the CI and
    release workflows on the Windows and Linux runners (PowerShell 7).

.EXAMPLE
    ./tools/Test-Package.ps1 -Executable ./publish/Canon.API.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Url = 'http://localhost:5000',
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path $Executable).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('canonwebapi-package-' + [guid]::NewGuid())
Copy-Item (Split-Path $source) $work -Recurse
$exe = Join-Path $work (Split-Path $source -Leaf)
if ($IsLinux) { & chmod 755 $exe }
$output = Join-Path $work 'console.log'
$errorOutput = Join-Path $work 'console-error.log'

$process = Start-Process $exe -WorkingDirectory $work -PassThru -RedirectStandardOutput $output `
    -RedirectStandardError $errorOutput -ArgumentList @("--urls=$Url", '--AutoUpdate:Enabled=false')
$status = $null

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $status -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
        try {
            $status = Invoke-RestMethod "$Url/status" -TimeoutSec 10
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.WaitForExit()
}

if ($status -and ($status.connected -or "$($status.error)" -match 'No Canon camera detected')) {
    $detail = if ($status.connected) { "camera $($status.cameraName) connected" } else { $status.error }
    Write-Host "Package OK: $detail"
    Remove-Item $work -Recurse -Force
    exit 0
}

$answer = if ($status) { $status | ConvertTo-Json -Compress } else { 'no answer' }
Write-Host "Package check failed. /status: $answer"
Write-Host '--- application output ---'
Get-Content $output, $errorOutput -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
exit 1
```

- [ ] **Step 2: Check it locally (pass, then fail)**

```bash
PWSH=/tmp/claude-1000/-home-gilbert-Documents-Project-CanonWebAPI/055083d5-ab07-4905-b425-34b4a1057fc9/scratchpad/pwsh/pwsh
O=$(mktemp -d)
dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true --output "$O"
$PWSH -NoProfile -File tools/Test-Package.ps1 -Executable "$O/Canon.API" -Url http://localhost:5098; echo "exit $?"
ls "$O"   # no logs folder: the check ran in a copy
B=$(mktemp -d); cp -a "$O"/. "$B"/; rm "$B/libEDSDK.so"
$PWSH -NoProfile -File tools/Test-Package.ps1 -Executable "$B/Canon.API" -Url http://localhost:5098; echo "exit $?"
```

Expected: the first run prints `Package OK: No Canon camera detected: Device not found` (or `camera Canon EOS R100 connected` when a camera is plugged in and free) and exit 0; the publish folder has no `logs`; the second run prints `Package check failed` with an `/status` error about loading `EDSDK` (`Unable to load shared library 'EDSDK'...`) and exit 1. Do not leave any `Canon.API` process running (`ss -ltn | grep 5098` prints nothing).

- [ ] **Step 3: CI on Windows and Linux**

Replace the whole `.github/workflows/ci.yml` with:

```yaml
name: CI

on:
  pull_request:
  push:
    branches:
      - main

permissions:
  contents: read

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-and-test:
    runs-on: windows-latest

    steps:
    - name: Checkout
      uses: actions/checkout@v5

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Restore dependencies
      run: dotnet restore CanonSDK.sln

    - name: Build solution
      run: dotnet build CanonSDK.sln --configuration Release --no-restore

    - name: Run unit tests
      run: dotnet test CanonSDK.sln --configuration Release --no-build --logger trx --results-directory TestResults

    - name: Publish the Windows package
      run: dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0-windows --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ./publish

    - name: Check that the Windows package starts and loads the EDSDK
      run: ./tools/Test-Package.ps1 -Executable ./publish/Canon.API.exe
      shell: pwsh

    - name: Upload test results
      if: always()
      uses: actions/upload-artifact@v4
      with:
        name: test-results-windows
        path: TestResults

  build-and-test-linux:
    runs-on: ubuntu-latest

    steps:
    - name: Checkout
      uses: actions/checkout@v5

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Install libusb (needed by the Linux EDSDK)
      run: sudo apt-get update && sudo apt-get install -y libusb-1.0-0

    - name: Restore dependencies
      run: dotnet restore CanonSDK.sln

    - name: Build solution
      run: dotnet build CanonSDK.sln --configuration Release --no-restore

    - name: Run unit tests
      run: dotnet test CanonSDK.sln --configuration Release --no-build --logger trx --results-directory TestResults

    - name: Publish the Linux package
      run: dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true --output ./publish

    - name: Check that the Linux package starts and loads the EDSDK
      run: ./tools/Test-Package.ps1 -Executable ./publish/Canon.API
      shell: pwsh

    - name: Upload test results
      if: always()
      uses: actions/upload-artifact@v4
      with:
        name: test-results-linux
        path: TestResults
```

Check the syntax: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('ci.yml OK')"`

- [ ] **Step 4: Documentation**

`CLAUDE.md`, section "## Testing", add a bullet after the Canon.API.Tests one:

```markdown
- CI (pull requests and `main`): build and tests on Windows and Linux runners, then both packages are published and started by `tools/Test-Package.ps1`, which checks that they load the EDSDK (`/status`: "No Canon camera detected")
```

`CHANGELOG.md`, `## [Unreleased]` / `### Changed`, add:

```markdown
- CI builds and tests on Windows and Linux, then publishes the Windows and Linux packages and starts them to check that
  they load the EDSDK (`tools/Test-Package.ps1`). The published Windows executable was never run before a release.
```

- [ ] **Step 5: Commit**

```bash
git add tools/Test-Package.ps1 .github/workflows/ci.yml CLAUDE.md CHANGELOG.md
git commit -m "Check both packages in CI on Windows and Linux runners

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Atomic release pipeline

**Files:**
- Modify: `.github/workflows/build.yml`, `README.md`, `CLAUDE.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: `tools/Test-Package.ps1` (Task 2), the Linux package content (Task 1).
- Produces: on a `v*` tag, a GitHub release with `CanonWebAPI.zip` and `CanonWebAPI-linux-x64.tar.gz`, then `docs/autoupdate.xml` committed to `main`; from the Actions tab (`workflow_dispatch`), a dry run that builds, tests and checks both packages and publishes nothing.

- [ ] **Step 1: The workflow**

Replace the whole `.github/workflows/build.yml` with:

```yaml
name: Release CanonWebAPI

on:
  push:
    tags:
      - 'v*'
  # Dry run from the Actions tab: builds, tests and checks both packages of the selected branch; publishes nothing.
  workflow_dispatch:

permissions:
  contents: write

jobs:
  prepare:
    runs-on: ubuntu-latest
    outputs:
      sha: ${{ steps.commit.outputs.SHA }}
      version: ${{ steps.version.outputs.VERSION }}

    steps:
    # A release is built from main: this workflow commits the version and the AutoUpdater XML to it.
    - name: Checkout
      uses: actions/checkout@v5
      with:
        ref: ${{ github.event_name == 'push' && 'main' || github.ref }}
        fetch-depth: 0
        token: ${{ secrets.GITHUB_TOKEN }}

    # Refuse to release if main is not the tagged commit, so the packages always match the tag.
    - name: Check that the tag points to main
      if: github.event_name == 'push'
      shell: pwsh
      run: |
        $mainSha = git rev-parse HEAD
        $tagSha = "${{ github.sha }}"
        if ($mainSha -ne $tagSha) {
          Write-Error "Tag ${{ github.ref_name }} points to $tagSha but main is at $mainSha. Tag the latest commit of main."
          exit 1
        }
        echo "Tag ${{ github.ref_name }} matches main ($mainSha)"

    - name: Get the version
      id: version
      shell: pwsh
      run: |
        if ("${{ github.event_name }}" -eq "push") {
          $version = "${{ github.ref_name }}" -replace '^v', ''
        }
        else {
          $version = [regex]::Match((Get-Content Canon.API/Canon.API.csproj -Raw), '<Version>(.*)</Version>').Groups[1].Value
        }
        echo "VERSION=$version" >> $env:GITHUB_OUTPUT
        echo "Version: $version"

    - name: Check and update project version
      if: github.event_name == 'push'
      id: version_check
      shell: pwsh
      run: |
        $tagVersion = "${{ steps.version.outputs.VERSION }}"

        # Read current version from project file
        $projectFile = "Canon.API/Canon.API.csproj"
        $projectContent = Get-Content $projectFile
        $currentVersion = ""

        foreach ($line in $projectContent) {
          if ($line -match '<Version>(.*)</Version>') {
            $currentVersion = $matches[1]
            break
          }
        }

        echo "Current project version: $currentVersion"
        echo "Tag version: $tagVersion"

        if ($currentVersion -ne $tagVersion) {
          echo "VERSION_MISMATCH=true" >> $env:GITHUB_OUTPUT
          echo "Versions do not match - updating project files"

          # Update Canon.API project file
          $projectContent -replace '<Version>.*</Version>', "<Version>$tagVersion</Version>" -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$tagVersion</AssemblyVersion>" -replace '<FileVersion>.*</FileVersion>', "<FileVersion>$tagVersion</FileVersion>" | Set-Content $projectFile

          # Update Canon.Core project file if it has version info
          $coreProjectFile = "Canon.Core/Canon.Core.csproj"
          if (Test-Path $coreProjectFile) {
            $coreContent = Get-Content $coreProjectFile
            if ($coreContent -match '<Version>') {
              $coreContent -replace '<Version>.*</Version>', "<Version>$tagVersion</Version>" -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$tagVersion</AssemblyVersion>" -replace '<FileVersion>.*</FileVersion>', "<FileVersion>$tagVersion</FileVersion>" | Set-Content $coreProjectFile
            }
          }
        } else {
          echo "VERSION_MISMATCH=false" >> $env:GITHUB_OUTPUT
          echo "Versions match - no update needed"
        }

    - name: Commit version updates
      if: github.event_name == 'push' && steps.version_check.outputs.VERSION_MISMATCH == 'true'
      shell: pwsh
      run: |
        git config --local user.email "action@github.com"
        git config --local user.name "GitHub Action"
        git add "*.csproj"
        git commit -m "Update project versions to ${{ steps.version.outputs.VERSION }}"
        git push origin main

    - name: Commit to build
      id: commit
      shell: pwsh
      run: echo "SHA=$(git rev-parse HEAD)" >> $env:GITHUB_OUTPUT

  windows:
    needs: prepare
    runs-on: windows-latest

    steps:
    - name: Checkout
      uses: actions/checkout@v5
      with:
        ref: ${{ needs.prepare.outputs.sha }}

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Restore dependencies
      run: dotnet restore CanonSDK.sln

    - name: Build solution
      run: dotnet build CanonSDK.sln --configuration Release --no-restore

    - name: Run unit tests
      run: dotnet test CanonSDK.sln --configuration Release --no-build

    - name: Publish the Windows package
      run: dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0-windows --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ./publish

    - name: Check that the Windows package starts and loads the EDSDK
      run: ./tools/Test-Package.ps1 -Executable ./publish/Canon.API.exe
      shell: pwsh

    - name: Create the Windows archive
      run: Compress-Archive -Path "./publish/*" -DestinationPath "./CanonWebAPI.zip"
      shell: powershell

    - name: Upload the Windows archive
      uses: actions/upload-artifact@v4
      with:
        name: windows-package
        path: CanonWebAPI.zip
        if-no-files-found: error

  linux:
    needs: prepare
    runs-on: ubuntu-latest

    steps:
    - name: Checkout
      uses: actions/checkout@v5
      with:
        ref: ${{ needs.prepare.outputs.sha }}

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Install libusb (needed by the Linux EDSDK)
      run: sudo apt-get update && sudo apt-get install -y libusb-1.0-0

    - name: Restore dependencies
      run: dotnet restore CanonSDK.sln

    - name: Build solution
      run: dotnet build CanonSDK.sln --configuration Release --no-restore

    - name: Run unit tests
      run: dotnet test CanonSDK.sln --configuration Release --no-build

    - name: Publish the Linux package
      run: dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true --output ./package/CanonWebAPI

    - name: Check that the Linux package starts and loads the EDSDK
      run: ./tools/Test-Package.ps1 -Executable ./package/CanonWebAPI/Canon.API
      shell: pwsh

    - name: Create the Linux archive
      run: |
        chmod 755 package/CanonWebAPI/Canon.API package/CanonWebAPI/install.sh
        tar -czf CanonWebAPI-linux-x64.tar.gz -C package CanonWebAPI

    - name: Upload the Linux archive
      uses: actions/upload-artifact@v4
      with:
        name: linux-package
        path: CanonWebAPI-linux-x64.tar.gz
        if-no-files-found: error

  # Only when both packages were built and checked: a failure publishes nothing and leaves the photo booths alone.
  release:
    needs: [prepare, windows, linux]
    if: github.event_name == 'push'
    runs-on: ubuntu-latest

    steps:
    - name: Checkout main
      uses: actions/checkout@v5
      with:
        ref: main
        fetch-depth: 0
        token: ${{ secrets.GITHUB_TOKEN }}

    - name: Download the packages
      uses: actions/download-artifact@v4
      with:
        path: packages
        merge-multiple: true

    - name: Create GitHub Release
      uses: softprops/action-gh-release@v2
      with:
        tag_name: ${{ github.ref_name }}
        files: |
          packages/CanonWebAPI.zip
          packages/CanonWebAPI-linux-x64.tar.gz
        fail_on_unmatched_files: true
        generate_release_notes: true
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

    - name: Update AutoUpdater XML
      shell: pwsh
      run: |
        $version = "${{ needs.prepare.outputs.version }}"
        $releaseUrl = "https://github.com/${{ github.repository }}/releases/download/v$version/CanonWebAPI.zip"

        # Create docs folder if it doesn't exist
        if (!(Test-Path "docs")) {
          New-Item -ItemType Directory -Path "docs"
        }

        # Create AutoUpdater XML content
        $xmlContent = @"
        <?xml version="1.0" encoding="UTF-8"?>
        <item>
          <version>$version</version>
          <url>$releaseUrl</url>
          <changelog>https://github.com/${{ github.repository }}/releases/tag/v$version</changelog>
          <mandatory>true</mandatory>
        </item>
        "@

        $xmlContent | Out-File -FilePath "docs/autoupdate.xml" -Encoding UTF8

    - name: Commit AutoUpdater XML
      shell: pwsh
      run: |
        git config --local user.email "action@github.com"
        git config --local user.name "GitHub Action"
        git add docs/autoupdate.xml
        if (!(git diff --staged --quiet)) {
          git commit -m "Update AutoUpdater XML for version ${{ needs.prepare.outputs.version }}"
          git push origin main
        }
```

Check the syntax and the wiring:

```bash
python3 - <<'EOF'
import yaml
w = yaml.safe_load(open('.github/workflows/build.yml'))
jobs = w['jobs']
assert list(jobs) == ['prepare', 'windows', 'linux', 'release'], list(jobs)
assert jobs['release']['needs'] == ['prepare', 'windows', 'linux']
assert jobs['windows']['needs'] == 'prepare' and jobs['linux']['needs'] == 'prepare'
print('build.yml OK, triggers:', list(w[True]))
EOF
```

Expected: `build.yml OK, triggers: ['push', 'workflow_dispatch']` (PyYAML reads the `on` key as `True`).

- [ ] **Step 2: Documentation**

`README.md`, "### Creating Releases": replace items 4 and 5 with:

```markdown
4. **Packages**: The workflow builds and tests the solution, publishes the Windows and Linux packages and starts both to check that they load the EDSDK (`tools/Test-Package.ps1`)
5. **Atomic release**: Only when both packages succeeded, it creates the release (`CanonWebAPI.zip`, `CanonWebAPI-linux-x64.tar.gz`) and updates the AutoUpdater XML; a failure publishes nothing
```

and add after the list (before the `docs/autoupdate.xml` sentence):

```markdown
To check the pipeline before tagging, run the "Release CanonWebAPI" workflow by hand from the Actions tab (on `main`): it builds, tests and checks both packages and publishes nothing.
```

`CLAUDE.md`, "## Release Process": replace the five bullets ("Trigger: ..." to "Updates AutoUpdater XML automatically") with:

```markdown
- Trigger: push a tag matching `v*` on the latest commit of `main` (or run the workflow by hand: dry run, nothing published)
- `prepare` job: checks the tag, compares the tag version with the project versions and commits them if they differ
- `windows` and `linux` jobs, in parallel: build, test, publish `CanonWebAPI.zip` (win-x64) and `CanonWebAPI-linux-x64.tar.gz` (linux-x64, with `packaging/linux/`), and start each package with `tools/Test-Package.ps1`
- `release` job, only when both succeeded: creates the GitHub release with both packages and updates the AutoUpdater XML (Windows package)
```

`CHANGELOG.md`, `## [Unreleased]` / `### Changed`, add:

```markdown
- The release workflow builds, tests and checks the Windows and Linux packages in parallel, and creates the release
  (both packages) and updates the AutoUpdater XML only when both succeeded. It can be run by hand from the Actions tab
  as a dry run that publishes nothing.
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml README.md CLAUDE.md CHANGELOG.md
git commit -m "Build, check and release the Windows and Linux packages together

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Pull request and CI (controller)

**Files:** none.

- [ ] **Step 1:** Push `feature/linux-package` and open a pull request to `main` (body ends with the Claude Code line). Wait for the CI: `build-and-test` (Windows) and `build-and-test-linux` must pass, including both package checks.
- [ ] **Step 2:** If the Linux package check fails on the runner with another `/status` error than a library load failure (spec, Risks), report it to the user before changing the expected message.

---

### Task 5: Installation test with the camera (manual, with the user)

**Files:** none (findings go to the user).

- [ ] **Step 1: Build the archive exactly as the release job**

```bash
S=/tmp/claude-1000/-home-gilbert-Documents-Project-CanonWebAPI/055083d5-ab07-4905-b425-34b4a1057fc9/scratchpad/linux-package
rm -rf "$S" && mkdir -p "$S/package"
dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0 --runtime linux-x64 --self-contained true -p:PublishSingleFile=true --output "$S/package/CanonWebAPI"
chmod 755 "$S/package/CanonWebAPI/Canon.API" "$S/package/CanonWebAPI/install.sh"
tar -czf "$S/CanonWebAPI-linux-x64.tar.gz" -C "$S/package" CanonWebAPI
mkdir "$S/extract" && tar -xzf "$S/CanonWebAPI-linux-x64.tar.gz" -C "$S/extract" && ls -l "$S/extract/CanonWebAPI"
```

- [ ] **Step 2: Install as a service.** Ask the user to plug the camera into this computer (not the VM) and to run `! sudo /tmp/claude-1000/-home-gilbert-Documents-Project-CanonWebAPI/055083d5-ab07-4905-b425-34b4a1057fc9/scratchpad/linux-package/extract/CanonWebAPI/install.sh`. Then check:

```bash
systemctl status canonwebapi --no-pager
ls -l /opt/canonwebapi /opt/canonwebapi/logs
ls -l /dev/bus/usb/$(lsusb -d 04a9: | awk '{print $2 "/" substr($4, 1, 3)}')   # group canonwebapi
curl -s http://localhost:5000/status
```

Expected: service active; files owned by root, `logs/` by `canonwebapi`; the camera device in group `canonwebapi`; `/status` connected.
- [ ] **Step 3: Camera test against the service:**

```bash
/tmp/claude-1000/-home-gilbert-Documents-Project-CanonWebAPI/055083d5-ab07-4905-b425-34b4a1057fc9/scratchpad/pwsh/pwsh -NoProfile -File tools/Test-Camera.ps1 -BaseUrl http://localhost:5000 -NoBrowser
```

(captures with the live view, flash changes, power cycle). Then read `/opt/canonwebapi/logs/canon-api*.log` and `journalctl -u canonwebapi --no-pager | tail -50`.
- [ ] **Step 4: Update and uninstall.** Ask the user to run the `install.sh` of the extracted package again (update: service restarted, files replaced), then `! sudo /opt/canonwebapi/install.sh --uninstall`. Check that `systemctl status canonwebapi`, `/etc/udev/rules.d/60-canonwebapi.rules`, `/opt/canonwebapi` and `id canonwebapi` are all gone.

---

### Task 6: Release 1.5.0.0 (controller, with the user)

**Files:**
- Modify: `Canon.API/Canon.API.csproj`, `docs/openapi.json`, `README.md`, `CHANGELOG.md`

- [ ] **Step 1: Version.** `Canon.API/Canon.API.csproj`: `AssemblyVersion`, `FileVersion` and `Version` to `1.5.0.0`. `docs/openapi.json`: `"version": "1.4.0"` to `"version": "1.5.0"`. README: "**Current version: 1.4.0.0**" to "**Current version: 1.5.0.0**", and the example tag in "Creating Releases" to `v1.5.0.0`.
- [ ] **Step 2: Upgrade note.** README, after the "Upgrading from 1.3.x" note:

```markdown
> **Upgrading from 1.4.x**: the "flash firing" setting is no longer written while the live view runs (it froze the EOS R100): with `Canon:ForceFlashFiring` it is set when the camera connects, before the live view starts and before a capture taken without live view, and a value set with `POST /flash` stays until the live view stops. Linux is supported (see [Installing on Linux](#installing-on-linux)). From the sources, `dotnet run` and `dotnet publish` need `-f net10.0-windows` or `-f net10.0`. See the [changelog](CHANGELOG.md#1500---<date>).
```

(`<date>` is the release date in `YYYY-MM-DD` form, the same as in the changelog heading.)

- [ ] **Step 3: Changelog.** `## [Unreleased]` becomes `## [1.5.0.0] - <date>` with a new empty `## [Unreleased]` above it; the links at the end: `[Unreleased]: .../compare/v1.5.0.0...HEAD` and `[1.5.0.0]: .../compare/v1.4.0.0...v1.5.0.0`.
- [ ] **Step 4:** Build and tests (`dotnet build CanonSDK.sln`, `dotnet test CanonSDK.sln`), commit "Bump version to 1.5.0.0 and date the 1.5.0.0 release", push, wait for the CI.
- [ ] **Step 5:** After the user merges the pull request: run the "Release CanonWebAPI" workflow by hand on `main` (dry run) and check that `windows` and `linux` pass. Then, only with the user's go-ahead, tag the latest commit of `main` with `v1.5.0.0` and push the tag; check the release assets and `docs/autoupdate.xml`.

# Linux support, step 1: run the API on Linux

Date: 2026-10-04. Status: approved design.

## Context

- A photo booth (Windows, EOS R100, dummy battery, no memory card) froze when taking a picture: the API reported the
  camera as disconnected and no picture was returned. The cause is unknown and the photo booth log is not available.
- The R100 is now plugged into a Linux development machine (Ubuntu 26.04, x86_64, .NET 10 SDK installed).
- Canon ships EDSDK 13.20.21 for Linux (x86_64, ARM64, ARM32). Its headers are identical to the Windows ones.
  `libEDSDK.so` needs `libusb-1.0` and `libudev`.
- Linux becomes a supported platform in two steps:
  - Step 1 (this spec): the API runs on Linux, so the camera can be tested on the development machine with full logs.
  - Step 2 (separate spec): distribution (release package, service, USB permissions, CI, other architectures).
- No automatic update on Linux for now.

## Goals

- `dotnet run --project Canon.API -f net10.0` runs the API on Linux x64 against a real camera, with the same behavior
  as on Windows.
- The Windows build, release package and automatic update are unchanged, apart from `-f net10.0-windows` in the
  publish commands.
- Debug logs show every EDSDK event and every step of a capture, to find where the photo booth capture fails.

## Non-goals (step 2 or later)

Linux release package, self-contained Linux publish, systemd service, udev rules, Linux CI, ARM64/ARM32, automatic
update on Linux, macOS. Fixing the capture failure itself is a separate change, made once its root cause is known.

## Design

### 1. Canon.Core: cross-platform library

- Target framework `net10.0` (was `net10.0-windows`). The STA apartment of the Canon thread stays Windows only (already
  guarded by `OperatingSystem.IsWindows()`). `Canon.Core.Tests` and `Canon.API.Tests` also target `net10.0`.
- Every P/Invoke declaration uses the library name `"EDSDK"` (a constant of the `EDSDK` class) instead of
  `"EDSDK.dll"`. .NET resolves it to `EDSDK.dll` on Windows and `libEDSDK.so` on Linux.
- No other declaration changes: the headers are identical, and the type sizes are the same on 64-bit Linux
  (`EdsInt32` is `int`, `EdsUInt64` is 64-bit, `char` arrays are marshalled as UTF-8). Callbacks use the platform
  default calling convention.
- Native libraries: the Windows DLLs stay in `EDSDK/`. `libEDSDK.so` (from `EDSDK/Library/x86_64` of the 13.20.21
  Linux SDK) is added as `EDSDK/linux-x64/libEDSDK.so`.
- The title returned when the SDK cannot be loaded (`CameraExceptionHandler`) covers both platforms: `EDSDK.dll` or
  `libEDSDK.so` missing, wrong architecture, or `libusb-1.0` missing on Linux.
- `PlatformTarget` stays x64.

### 2. Canon.API: two target frameworks

- `TargetFrameworks`: `net10.0-windows;net10.0`.
- Conditioned on the Windows target: the `Autoupdater.NET.Official` reference, `RuntimeIdentifier` `win-x64`,
  `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, `EnableCompressionInSingleFile`.
  The `net10.0` target is framework-dependent and has no runtime identifier in step 1.
- `Program.cs`: the AutoUpdater `using` and `ConfigureAutoUpdater` are compiled for Windows only (`#if WINDOWS`).
  On the other targets, an Information log says that automatic updates are not available on this platform.
- Native libraries are copied by Canon.API according to its target (moved from `Canon.Core.csproj`, the API being
  the only executable): `EDSDK.dll` and `EdsImage.dll` for `net10.0-windows`, `libEDSDK.so` for `net10.0`. Copied to
  the output and publish folders, excluded from the single file, as today.
- Commands: `dotnet run --project Canon.API -f net10.0` on Linux, `-f net10.0-windows` on Windows. The publish
  commands (`.github/workflows/build.yml`, `Dockerfile`, `build.bat`) add `-f net10.0-windows`. The opt-in OpenAPI
  generation command uses `-f net10.0`, which runs on Windows and Linux, so the document is written once.
- Running on Linux:
  - The user needs read/write access to the camera USB device (desktop sessions get it through `uaccess`).
  - Nothing else may hold the camera: no GNOME (gvfs) gphoto2 mount, no VMware USB passthrough.
  - Logs: console and `Canon.API/bin/Debug/net10.0/logs/canon-api.log`.
  - Use the `http` launch profile (`http://localhost:5159`).

### 3. Diagnostics

- Debug level, category `Canon.Core.CanonCamera`, enabled with `Logging:LogLevel:Canon.Core` = `Debug`. Off by
  default (`Default` is `Information`).
- Logged:
  - every event received from the SDK: state events (name and parameter), object events (name), property events
    (property id and parameter);
  - each step of a capture, with its EDSDK result code and its duration: UI lock,
    flash target, flash firing, UI unlock, shutter press (each attempt), shutter release (each attempt), transfer
    request, download, then completion, failure or timeout.
- The UI unlock result was ignored until now: it is logged, as a warning when it fails, and stays non-fatal.
- Event names come from a pure helper in `EdsdkHelper`, so they can be unit tested.
- Logging only: no behavior change.

### 4. Testing

Unit tests (run on Linux and Windows):

- Every `DllImport` of the `EDSDK` class uses the `"EDSDK"` library name.
- Event names: known state and object events map to their names, unknown codes to hexadecimal.
- Existing tests unchanged and passing.

Build: the Windows CI builds the solution, so both targets of Canon.API.

Manual test with the R100 on Linux, one variable at a time:

1. Start the API with `Canon.Core` at Debug. Set the camera as in the photo booth (shooting mode, flash on the
   shoe, no memory card if possible).
2. `GET /status`, open `/videostream` in a browser, then `POST /takepicture` with the default parameters.
3. If the capture fails, the log shows the last step that succeeded. Then retry with `Canon:ForceFlashFiring` =
   `false`, then with `useAutoFocus=false`.

The result decides the fix.

### 5. Documentation

- README: "Running on Linux (development)" section: prerequisites (.NET 10 SDK, `libusb-1.0`), command, logs,
  camera access.
- CLAUDE.md: target frameworks, `-f` in the commands, Linux library.
- CHANGELOG (Unreleased): Linux x64 support without automatic update, Debug logs of EDSDK events and capture steps;
  `dotnet run` and `dotnet publish` need `-f`.

## Risks

- The Linux EDSDK may behave differently from the Windows one (e.g. hot plug through libusb and udev). If the camera
  cannot be detected or its session cannot be opened on Linux, report the EDSDK error and stop: no workaround in
  step 1.
- If the R100 does not reproduce the photo booth failure on Linux, the photo booth log is still needed.

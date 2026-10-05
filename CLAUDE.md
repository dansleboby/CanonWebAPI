# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CanonWebAPI is a .NET 10 web API for remotely controlling Canon DSLR and mirrorless cameras through the Canon EDSDK. The solution provides REST endpoints for camera control, live view streaming, and image capture.

## Architecture

The solution consists of 4 projects:

- **Canon.API**: ASP.NET Core Web API (main entry point); targets `net10.0-windows` (Windows release, AutoUpdater.NET) and `net10.0` (Linux, no automatic update)
- **Canon.Core**: Core library wrapping Canon EDSDK functionality
- **Canon.Core.Tests**: xUnit unit tests of Canon.Core (no camera needed, run on Linux too)
- **Canon.API.Tests**: xUnit unit tests of the pure parts of Canon.API, compiled from the Canon.API sources so they do not depend on the API project and its packages

### Key Components

- **CanonCamera** (`Canon.Core/CanonCamera.cs`): Main camera abstraction: connection/reconnection, properties, capture, live view
- **CanonCameraOptions** (`Canon.Core/CanonCameraOptions.cs`): Settings bound to the `Canon` section of appsettings.json
- **LiveViewBroadcaster** (`Canon.Core/LiveViewBroadcaster.cs`): Single live view producer shared by all `/videostream` clients
- **CanonController** (`Canon.API/Controllers/CanonController.cs`): REST API controller exposing camera operations
- **CameraExceptionHandler** (`Canon.API/Infrastructure/CameraExceptionHandler.cs`): Maps exceptions to problem details / HTTP status codes
- **CanonThread** (`Canon.Core/CanonThread.cs`): Dedicated STA thread running every EDSDK call and pumping `EdsGetEvent`
- **EDSDK.cs** (`Canon.Core/EDSDK.cs`): P/Invoke declarations for Canon EDSDK (aligned with the 13.20.21 headers)
- **EdsdkHelper** (`Canon.Core/EdsdkHelper.cs`): Error messages and property value tables (from the EDSDK API reference)
- **CameraProperty** (`Canon.Core/CameraProperty.cs`): Enum defining camera properties

### Dependencies

- Requires Canon EDSDK 13.20.21 **64-bit** DLLs (`EDSDK.dll`, `EdsImage.dll`, from `EDSDK_64/Dll` of the SDK) in the EDSDK folder
- Linux: `EDSDK/linux-x64/libEDSDK.so` (from `EDSDK/Library/x86_64` of the 13.20.21 Linux SDK), needs `libusb-1.0`; the `net10.0` target uses invariant globalization (no ICU needed: only the invariant culture is used)
- Linux package (`packaging/linux/`): the udev rule uses final assignments (`MODE:=`, `GROUP:=`) because `60-libgphoto2*.rules` sorts after it and sets `GROUP="plugdev"` on add/bind; `install.sh` applies it with an add event (libgphoto2 skips change events)
- Uses Serilog for logging
- Swagger/OpenAPI for API documentation
- Platform target: x64 (Windows; Linux through the `net10.0` target of Canon.API)

## Development Commands

### Build
```bash
dotnet build CanonSDK.sln
```

### Run API Server
```bash
dotnet run --project Canon.API -f net10.0-windows   # Linux: -f net10.0 --launch-profile http
```

### Run Unit Tests
```bash
dotnet test CanonSDK.sln
```

### Docker (Windows containers)
```bash
docker build --target test -t canonwebapi:test .   # build + tests
docker build -t canonwebapi .                       # build + tests + publish + runtime image
```

### Build Specific Project
```bash
dotnet build Canon.Core
dotnet build Canon.API
```

### Clean Solution
```bash
dotnet clean CanonSDK.sln
```

## API Endpoints

The Canon.API project exposes these REST endpoints (OpenAPI at `/openapi/v1.json`, Swagger UI at `/swagger`, copy in `docs/openapi.json`):
- GET/POST `/iso`, `/aperture`, `/shutterspeed`, `/exposure`, `/whitebalance` - Camera settings
- GET/POST `/settings` - Mode + iso/aperture/shutterSpeed/whiteBalance in one call; POST validates everything before writing
- POST `/takepicture` - Capture image (`useAutoFocus`, `fileTypes`; JPEG only by default)
- GET `/videostream` - MJPEG live view stream (shared by all clients)
- GET `/liveview` - Single live view frame
- GET `/latestpicture` - Retrieve last captured image
- POST `/autofocus` - Trigger autofocus
- GET `/cameraname`, `/mode`, `/temperature`, `/status` - Camera information
- GET/POST `/flash` - "Flash firing" setting (forced to Fire by default, outside the live view)

Errors are problem details: 400 invalid value, 409 capture refused or setting locked by the shooting mode, 503 not connected/busy, 504 capture timeout.

## Important Notes

- The camera can be connected via USB before or after the application starts (automatic connection and reconnection)
- Canon EOS Utility must NOT be running (conflicts with EDSDK access)
- Requires compatible Canon camera with EDSDK support
- Canon.Core and the tests target `net10.0`; Canon.API targets `net10.0-windows` (Windows release, AutoUpdater.NET) and `net10.0` (Linux). The release publishes the Windows package with `-f net10.0-windows -r win-x64` and the Linux package with `-f net10.0 -r linux-x64` (both self-contained single files)
- Uses structured logging with Serilog (logs to console and `logs/canon-api.log` next to the executable; levels from `Logging:LogLevel`)
- Content root is the executable folder: settings files are read next to the executable whatever the working directory
- Operator settings go in `appsettings.Local.json`, never shipped: automatic updates overwrite `appsettings.json`
- Target deployment: photo booth with a Canon EOS R100, dummy battery, no memory card (pictures are saved to the PC only)
- `docs/openapi.json` must be regenerated when endpoints change

## AutoUpdater Integration

Applies to the `net10.0-windows` target only: the `net10.0` target (Linux) has no automatic update.

The application includes AutoUpdater.NET with these configurations:
- Automatic version checking on startup
- No admin privileges required (`RunUpdateAsAdmin = false`)
- Custom download path to avoid temp folder permission issues
- The update check runs synchronously before the web server starts: nothing to stop when the updater exits the application
- Updates sourced from GitHub releases via `docs/autoupdate.xml`

## Release Process

The project uses GitHub Actions for automated releases (record every change in the `Unreleased` section of `CHANGELOG.md`; never edit `docs/autoupdate.xml` by hand):
- Trigger: push a tag matching `v*` on the latest commit of `main` (or run the workflow by hand: dry run, nothing published)
- `prepare` job: checks the tag, compares the tag version with the project versions and commits them if they differ
- `windows` and `linux` jobs, in parallel: build, test, publish `CanonWebAPI.zip` (win-x64) and `CanonWebAPI-linux-x64.tar.gz` (linux-x64, with `packaging/linux/`), and start each package with `tools/Test-Package.ps1`
- `release` job, only when both succeeded: creates the GitHub release with both packages and updates the AutoUpdater XML (Windows package, written with a UTF-8 BOM as it always was); a dry run only downloads the packages and generates the XML
- Token scope: `contents: read` for the workflow and no persisted credentials in the build jobs (they run NuGet build logic and the tests); only `prepare` and `release` get `contents: write`

## EDSDK Integration Notes

- `EdsInitializeSDK`/`EdsTerminateSDK` are called once per process; the camera session is opened on demand and reopened after a disconnection (hot plug via `EdsSetCameraAddedHandler`)
- **CanonThread** runs every EDSDK call; callbacks are raised on it during `EdsGetEvent`. From a callback, use `CanonThread.Post` to run SDK calls after the callback returns
- Event handlers are registered before `EdsOpenSession`; private properties (TempStatus, FixedMovie) are enabled before it too
- Every transfer request must end with `EdsDownloadComplete` or `EdsDownloadCancel` (unwanted file types are cancelled)
- Downloads go to memory streams; the progress callback is registered before `EdsDownload`
- Live view is started with `Evf_OutputDevice` and stopped when no client uses it
- "Device busy" answers are retried after ~500 ms, as in the Canon samples; only the shutter press is retried, the release is always sent
- Flash (`EdsCreateFlashSettingRef`, `Flash_Target`, `Flash_Firing`): the UI must be locked while setting it; the SDK only reports values set remotely, so the setting is forced when the camera connects, before the live view starts and before a capture without live view (studio flash on the shoe in the photo booth). Never write it while the live view runs: the EOS R100 freezes (shutter busy until it is turned off and on); POST /flash pauses the live view
- Settings: `CameraSettingRules` (pure, tested) decides what is settable per AE mode; EdsGetPropertyDesc gives the accepted values (its `form`/`access` fields are reserved, always 0); an empty list means not settable now (as in the Canon samples). Set only values from that list (API reference 3.1.20)
- Memory management critical due to unmanaged EDSDK resources: release every ref (`EdsRelease`), except, on Linux, a camera that is gone: the SDK has already closed its session and releasing it crashes (`CLinuxPtpHelper::Terminate`)

## Testing

- **Canon.Core.Tests**: xUnit tests of the logic that does not need a camera (value tables, settings rules, timeouts, file types, CanonThread, LiveViewBroadcaster, EDSDK structure layouts)
- **Canon.API.Tests**: xUnit tests of the error mapping (CameraExceptionHandler), the request models and the log levels
- CI (pull requests and `main`): build and tests on Windows and Linux runners, then both packages are published and started by `tools/Test-Package.ps1`, which checks that they load the EDSDK (`/status`: "No Canon camera detected")
- End-to-end testing requires a physical Canon camera connected via USB (not possible in Windows containers)
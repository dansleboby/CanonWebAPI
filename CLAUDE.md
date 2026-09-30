# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CanonWebAPI is a .NET 9 web API for remotely controlling Canon DSLR and mirrorless cameras through the Canon EDSDK. The solution provides REST endpoints for camera control, live view streaming, and image capture.

## Architecture

The solution consists of 5 projects:

- **Canon.API**: ASP.NET Core Web API (main entry point)
- **Canon.Core**: Core library wrapping Canon EDSDK functionality
- **Canon.Core.Tests**: xUnit unit tests of Canon.Core (no camera needed, run on Linux too)
- **Canon.Test**: Console application for testing Canon.Core
- **Canon.Test.Avalonia**: Desktop GUI test application using Avalonia UI

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
- Uses Serilog for logging
- Swagger/OpenAPI for API documentation
- Platform target: x64 (Windows only)

## Development Commands

### Build
```bash
dotnet build CanonSDK.sln
```

### Run API Server
```bash
dotnet run --project Canon.API
```

### Run Console Test App
```bash
dotnet run --project Canon.Test
```

### Run Avalonia GUI Test App
```bash
dotnet run --project Canon.Test.Avalonia
```

### Run Unit Tests
```bash
dotnet test Canon.Core.Tests
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
- POST `/takepicture` - Capture image (`useAutoFocus`, `fileTypes`; JPEG only by default)
- GET `/videostream` - MJPEG live view stream (shared by all clients)
- GET `/liveview` - Single live view frame
- GET `/latestpicture` - Retrieve last captured image
- POST `/autofocus` - Trigger autofocus
- GET `/cameraname`, `/mode`, `/temperature`, `/status` - Camera information

Errors are problem details: 400 invalid value, 409 capture refused, 503 not connected/busy, 504 capture timeout.

## Important Notes

- Camera must be connected via USB before starting the application
- Canon EOS Utility must NOT be running (conflicts with EDSDK access)
- Requires compatible Canon camera with EDSDK support
- All projects target .NET 9 with Windows-specific dependencies
- Uses structured logging with Serilog (logs to console and `logs/canon-api.log`)
- Target deployment: photo booth with a Canon EOS R100, dummy battery, no memory card (pictures are saved to the PC only)
- `docs/openapi.json` must be regenerated when endpoints change

## AutoUpdater Integration

The application includes AutoUpdater.NET with these configurations:
- Automatic version checking on startup
- No admin privileges required (`RunUpdateAsAdmin = false`)
- Custom download path to avoid temp folder permission issues
- Graceful web server shutdown during updates
- Updates sourced from GitHub releases via `docs/autoupdate.xml`

## Release Process

The project uses GitHub Actions for automated releases:
- Trigger: Push tags matching `v*` pattern
- Workflow compares tag version with project file versions
- Updates project files automatically if versions don't match
- Creates GitHub releases with packaged binaries
- Updates AutoUpdater XML automatically

## EDSDK Integration Notes

- `EdsInitializeSDK`/`EdsTerminateSDK` are called once per process; the camera session is opened on demand and reopened after a disconnection (hot plug via `EdsSetCameraAddedHandler`)
- **CanonThread** runs every EDSDK call; callbacks are raised on it during `EdsGetEvent`. From a callback, use `CanonThread.Post` to run SDK calls after the callback returns
- Event handlers are registered before `EdsOpenSession`; private properties (TempStatus, FixedMovie) are enabled before it too
- Every transfer request must end with `EdsDownloadComplete` or `EdsDownloadCancel` (unwanted file types are cancelled)
- Downloads go to memory streams; the progress callback is registered before `EdsDownload`
- Live view is started with `Evf_OutputDevice` and stopped when no client uses it
- "Device busy" answers are retried after ~500 ms, as in the Canon samples
- Memory management critical due to unmanaged EDSDK resources: release every ref (`EdsRelease`)

## Testing

- **Canon.Core.Tests**: xUnit tests of the logic that does not need a camera (value tables, timeouts, file types, CanonThread, LiveViewBroadcaster)
- **Canon.Test**: Console application for basic Canon.Core functionality testing
- **Canon.Test.Avalonia**: GUI application for interactive testing and development
- End-to-end testing requires a physical Canon camera connected via USB (not possible in Windows containers)
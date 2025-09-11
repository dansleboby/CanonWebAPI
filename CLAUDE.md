# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CanonWebAPI is a .NET 9 web API for remotely controlling Canon DSLR and mirrorless cameras through the Canon EDSDK. The solution provides REST endpoints for camera control, live view streaming, and image capture.

## Architecture

The solution consists of 5 main projects:

- **Canon.API**: ASP.NET Core Web API (main entry point)
- **Canon.Core**: Core library wrapping Canon EDSDK functionality
- **Canon.Test**: Console application for testing Canon.Core
- **Canon.Test.Avalonia**: Desktop GUI test application using Avalonia UI
- **CanonSDK**: Legacy project (appears unused)

### Key Components

- **CanonCamera** (`Canon.Core/CanonCamera.cs`): Main camera abstraction class handling EDSDK communication
- **CanonController** (`Canon.API/Controllers/CanonController.cs`): REST API controller exposing camera operations
- **CanonThread** (`Canon.Core/CanonThread.cs`): Thread management for EDSDK operations
- **EDSDK.cs** (`Canon.Core/EDSDK.cs`): P/Invoke declarations for Canon EDSDK
- **CameraProperty** (`Canon.Core/CameraProperty.cs`): Enum defining camera properties

### Dependencies

- Requires Canon EDSDK DLLs (`EDSDK.dll`, `EdsImage.dll`) in EDSDK folder
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

The Canon.API project exposes these REST endpoints:
- GET/POST `/iso`, `/aperture`, `/shutterspeed`, `/exposure`, `/whitebalance` - Camera settings
- POST `/takepicture` - Capture image
- GET `/videostream` - MJPEG live view stream
- GET `/latestpicture` - Retrieve last captured image
- POST `/autofocus` - Trigger autofocus
- GET `/cameraname` - Get camera model

## Important Notes

- Camera must be connected via USB before starting the application
- Canon EOS Utility must NOT be running (conflicts with EDSDK access)
- Requires compatible Canon camera with EDSDK support
- All projects target .NET 9 with Windows-specific dependencies
- Uses structured logging with Serilog (logs to console and `logs/canon-api.log`)
- API includes Swagger documentation available in development mode

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

- **CanonCamera** manages EDSDK lifecycle and event handling
- **CanonThread** ensures EDSDK operations run on dedicated thread
- P/Invoke declarations in EDSDK.cs provide low-level camera access
- Event handlers for camera state, properties, and progress tracking
- Semaphore-based synchronization for picture taking operations
- Memory management critical due to unmanaged EDSDK resources

## Testing

- **Canon.Test**: Console application for basic Canon.Core functionality testing
- **Canon.Test.Avalonia**: GUI application for interactive testing and development
- No formal unit test framework currently implemented
- Testing requires physical Canon camera connected via USB
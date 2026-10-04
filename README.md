# CanonWebAPI

> **⚠️ BETA SOFTWARE**: This project is currently in beta stage. While functional, it may contain bugs and is subject to breaking changes. Use with caution in production environments.

A web API for remotely controlling Canon DSLR and mirrorless cameras. This project utilizes the Canon EDSDK to communicate with the camera and includes automatic update capabilities.

**Current version: 1.4.0.0** (.NET 10, Canon EDSDK 13.20.21). See the [changelog](CHANGELOG.md) for the changes of each version.

> **Upgrading from 1.0.x**: errors are now returned as problem details with new status codes (e.g. 503 when the camera is not connected, 504 instead of 408 on capture timeout), and values unknown to the value tables are returned in hexadecimal (`"0x99"`). See the [changelog](CHANGELOG.md#1100---2026-09-30).

> **Upgrading from 1.2.x**: the setters (`/iso`, `/aperture`, `/shutterspeed`, `/exposure`, `/whitebalance`) now answer 409 `not-settable-in-mode` when the shooting mode locks the setting, and setting errors carry machine readable fields (`reason`, `acceptedValues`...). See [Setting errors](#setting-errors) and the [changelog](CHANGELOG.md#1300---2026-09-30).

> **Upgrading from 1.3.x**: settings changed in `appsettings.json` are reset by this update: put them in `appsettings.Local.json`, which updates never overwrite (see [Configuration](#configuration)). `POST /settings` refuses unknown fields (400), and f/4.5 in 1/3 stop steps now reads `"4.5 (1/3)"` (sending `"4.5"` still works). See the [changelog](CHANGELOG.md#1400---2026-09-30).

## Features

*   Get camera information (e.g., camera name, shooting mode, temperature restrictions).
*   Get and set camera settings:
    *   ISO Speed
    *   Aperture
    *   Shutter Speed
    *   Exposure Compensation
    *   White Balance
*   Take pictures and download them (JPEG by default, other file types on request).
*   Live view streaming via MJPEG, shared by every connected client.
*   Trigger autofocus.
*   Force the "flash firing" setting (flash on the accessory shoe, e.g. a studio flash).
*   Retrieve the last taken picture.
*   Automatic reconnection when the camera is turned off or unplugged and plugged back (hot plug).
*   OpenAPI document and Swagger UI.
*   **Automatic updates** via AutoUpdater.NET integration (Windows).
*   **Version display** in console on startup.

## Project Structure

The solution is divided into the following projects:

*   `Canon.API`: An ASP.NET Core web application that exposes the camera controls as a RESTful API.
*   `Canon.Core`: A .NET library that wraps the Canon EDSDK, providing a higher-level interface to interact with the camera.
*   `Canon.Core.Tests`: Unit tests (xUnit) of `Canon.Core` that do not need a camera.
*   `Canon.API.Tests`: Unit tests (xUnit) of the error mapping, the request models and the log levels of `Canon.API`.
*   `EDSDK`: Canon EDSDK 13.20.21 64-bit libraries: `EDSDK.dll` and `EdsImage.dll` (Windows), `linux-x64/libEDSDK.so` (Linux).

## Getting Started

### Prerequisites

*   A compatible Canon camera (tested with **Canon EOS R100** and **Canon T7**).
*   The camera connected to the computer via USB.
*   **.NET 10 SDK** (or newer) to build the project.
*   The Canon EOS Utility software should not be running, as it can prevent this application from connecting to the camera.
*   **Windows** or **Linux** (x64). On Linux, see [Running on Linux](#running-on-linux).

### Installation

1.  Clone this repository.
2.  Ensure the `EDSDK` folder is present in the project's root directory: `EDSDK.dll` and `EdsImage.dll` on Windows, `linux-x64/libEDSDK.so` on Linux. These files are essential for the `Canon.Core` library to communicate with the camera.
3.  Build the solution using Visual Studio or the `dotnet build` command.
4.  Run the `Canon.API` project. This will start the web server, on `http://localhost:5000` by default for the release
    executable (`http://localhost:5159` with `dotnet run`); set `ASPNETCORE_URLS` (or `--urls`) to change it.

## API Endpoints

The following endpoints are available once the `Canon.API` project is running.
The OpenAPI document is served at `/openapi/v1.json` (a copy is kept in [`docs/openapi.json`](docs/openapi.json)) and the Swagger UI at `/swagger`.

Setting values are sent as a JSON string, e.g. `"100"` with `Content-Type: application/json`.

| Method | Path                         | Description                                             | Request Body (Example) |
|--------|------------------------------|---------------------------------------------------------|------------------------|
| GET    | `/status`                    | Connection state, camera name, shooting mode, temperature and live view state. Never fails because of the camera. | N/A |
| GET    | `/cameraname`                | Gets the connected camera's name.                       | N/A                    |
| GET    | `/mode`                      | Gets the shooting mode (mode dial position, whether exposure settings can be changed, movie mode). | N/A |
| GET    | `/temperature`               | Gets the restrictions applied by the camera because of its internal temperature. | N/A |
| GET    | `/flash`                     | Gets the "flash firing" setting as last set by the API (a change made in the camera menu is not reported by the SDK). | N/A |
| POST   | `/flash`                     | Sets the "flash firing" setting. The camera must be in P, Tv, Av or M. | `"fire"` or `"off"` |
| GET    | `/settings`                  | Shooting mode, plus ISO, aperture, shutter speed, exposure compensation and white balance in one call: `{ value, supportedValues, settable }` each (`settable`: allowed by the shooting mode and the camera accepts at least one value now). | N/A |
| POST   | `/settings`                  | Applies several settings at once (e.g. a day / night preset). Every value is checked before anything is written, then they are written in a fixed order (iso, aperture, shutterSpeed, exposureCompensation, whiteBalance), never during a capture. Returns the settings read back from the camera, as `GET /settings`. Omitted fields are left unchanged; an unknown field is refused (400). | `{ "iso": "400", "aperture": "5.6", "shutterSpeed": "1/125", "exposureCompensation": "0", "whiteBalance": "Daylight" }` |
| GET    | `/iso`                       | Gets the current ISO speed and a list of supported values. | N/A                    |
| POST   | `/iso`                       | Sets the ISO speed.                                     | `"100"`                |
| GET    | `/aperture`                  | Gets the current aperture and a list of supported values. | N/A                    |
| POST   | `/aperture`                  | Sets the aperture.                                      | `"5.6"`                |
| GET    | `/shutterspeed`              | Gets the current shutter speed and a list of supported values. | N/A                    |
| POST   | `/shutterspeed`              | Sets the shutter speed.                                 | `"1/125"`              |
| GET    | `/exposure`                  | Gets the current exposure compensation and supported values. | N/A                    |
| POST   | `/exposure`                  | Sets the exposure compensation (not available in M, nor in the basic zone modes). | `"+1/3"`         |
| GET    | `/whitebalance`              | Gets the current white balance and supported values.    | N/A                    |
| POST   | `/whitebalance`              | Sets the white balance.                                 | `"Auto"`               |
| POST   | `/takepicture`               | Takes a picture and returns the file (JPEG by default). Query parameters: `useAutoFocus` (default `true`), `fileTypes` (e.g. `cr3`, `jpg,cr3`, `*`). The file name is in the `X-File-Name` header. | N/A |
| GET    | `/videostream`               | MJPEG live view stream. Several clients share the same frames. | N/A             |
| GET    | `/liveview`                  | A single live view image (JPEG).                        | N/A                    |
| GET    | `/latestpicture`             | Gets the last picture received from the camera.         | N/A                    |
| POST   | `/autofocus`                 | Triggers the camera's autofocus mechanism.              | N/A                    |

Values the camera does not know a label for are returned as raw hexadecimal values (e.g. `"0x99"`), which can also be sent back.
A setting that is not valid in the current state (e.g. exposure compensation in M) reads `"Not valid"`.

### Errors

Errors are returned as [problem details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with the EDSDK error code in `errorCode` when there is one:

| Status | Meaning |
|--------|---------|
| 400    | Invalid value, or value not accepted by the camera now (`acceptedValues` lists the accepted values). |
| 404    | No picture / live view image available. |
| 409    | The camera refused to take the picture (focus failure, no lens, movie mode...), or the setting cannot be changed in the current shooting mode. |
| 503    | Camera not connected, or busy (`Retry-After` header). |
| 504    | The camera did not deliver the picture in time. |

#### Setting errors

`POST /settings` and the setters (`/iso`, `/aperture`, `/shutterspeed`, `/exposure`, `/whitebalance`) check the value before writing it
and answer with the same machine readable problem details:

| Status | `reason` | Meaning |
|--------|----------|---------|
| 400    | `invalid-value` | The label cannot be read (e.g. `"1/7"`, `"f/5.6"`). |
| 400    | `value-not-accepted` | The camera does not accept this value now (lens, exposure step...). |
| 409    | `not-settable-in-mode` | The setting cannot be changed in the current shooting mode, or the camera accepts no value for it now (empty list: movie mode, no lens...). |

Extensions: `reason`, `property` (`iso`, `aperture`, `shutterSpeed`, `exposureCompensation`, `whiteBalance`), `acceptedValues`,
`aeMode`, `aeModeCode`, and `errors`: every refused setting (`property`, `value`, `reason`, `acceptedValues`, `detail`).
With `POST /settings`, nothing is written when a value is refused; when several are, the status is 409 if one of them
cannot be changed in the current mode (the top level fields then describe that one), 400 otherwise.

Settings that can be changed in each mode (other modes: none, the camera chooses by itself). The EDSDK does not report
it (the `form`/`access` fields of `EdsGetPropertyDesc` are reserved), hence this table:

| Mode | ISO | Aperture | Shutter speed | Exposure compensation | White balance |
|------|-----|----------|---------------|-----------------------|---------------|
| P, A-DEP | ✓ | | | ✓ | ✓ |
| Tv | ✓ | | ✓ | ✓ | ✓ |
| Av | ✓ | ✓ | | ✓ | ✓ |
| M | ✓ | ✓ | ✓ | ✓ with ISO Auto | ✓ |
| Bulb | ✓ | ✓ | | | ✓ |
| Fv | ✓ | ✓ | ✓ | ✓ | ✓ |

If the camera refuses a value while `POST /settings` writes it, the problem details list the settings already written in
`applied` and the one that failed in `failed` (`property`, `value`, `detail`, `errorCode`); the status follows the camera error
(e.g. 503 when busy or disconnected).

## Configuration

Settings are read, in increasing order of precedence, from `appsettings.json`, `appsettings.Local.json` (both next to the
executable, whatever the working directory), environment variables (e.g. `Canon__CaptureFileTypes__0=jpg`) and the command line.
Logs are written to the console and to `logs/canon-api.log` next to the executable.

Put the settings of a photo booth in `appsettings.Local.json`, never in `appsettings.json`: `appsettings.json` is part of the
release package and every automatic update overwrites it, while `appsettings.Local.json` is never shipped. It only needs the
settings that differ from the defaults, e.g.:

```json
{
  "Canon": {
    "FlashTarget": "External",
    "CaptureFileTypes": [ "jpg", "cr3" ]
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Canon:CaptureFileTypes` | `["jpg"]` | File types downloaded after a capture (`jpg`, `heif`, `cr3`, `*`...); the first matching file the camera sends is returned. Other files are cancelled on the camera. Can be overridden per request with `fileTypes`. |
| `Canon:CaptureTimeoutSeconds` | `10` | Time allowed to receive a capture, added to the exposure time of the current shutter speed. |
| `Canon:AutoFocusHoldMilliseconds` | `800` | How long `/autofocus` holds the shutter button halfway. |
| `Canon:LiveViewSmallImage` | `false` | Uses the smaller live view image (less bandwidth, lower resolution; not supported by every camera). |
| `Canon:KeepCameraScreenOn` | `false` | Keeps the camera screen on during the live view. When `false`, the live view is sent to the PC only, which turns the camera screen off and locks its buttons. |
| `Canon:PreventAutoPowerOff` | `true` | Extends the camera auto power off timer when the camera announces it will turn off. |
| `Canon:ForceFlashFiring` | `true` | Sets "flash firing" to Fire when the camera connects and before every capture, so a flash on the accessory shoe always fires even if the setting was changed on the camera. Requires P, Tv, Av or M. |
| `Canon:FlashTarget` | `Unspecified` | Target of the flash settings: `Unspecified` (e.g. studio flash triggered by the shoe center contact) or `External` (flash communicating with the camera). |
| `Canon:BusyRetryCount` / `BusyRetryDelayMilliseconds` | `3` / `500` | Retries when the camera answers "device busy". |
| `LiveView:FrameIntervalMilliseconds` | `30` | Delay between two live view frames. |
| `LiveView:IdleStopDelayMilliseconds` | `3000` | The camera live view stops this long after the last `/videostream` client left. |
| `LiveView:ErrorRetryDelayMilliseconds` | `1000` | Delay before retrying after a live view error (e.g. camera disconnected). |
| `AutoUpdate:Enabled` | `true` | Checks for updates at startup. Windows only. |
| `AutoUpdate:Url` | GitHub Pages `autoupdate.xml` | Update feed read by AutoUpdater.NET. Windows only. |
| `Logging:LogLevel` | `Default`: `Information`, `Microsoft.AspNetCore`: `Warning` | Minimum log level, by category prefix (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`). `Canon.Core`: `Debug` logs every EDSDK event and each step of a capture (UI lock, flash settings, shutter button, transfer) with its result and duration. |

## Automatic Updates

This application includes automatic update functionality powered by AutoUpdater.NET:

*   **Automatic version checking** on startup, before the web server starts and the camera is opened
*   **Seamless updates** without requiring admin privileges
*   Updates are downloaded from GitHub releases automatically
*   `appsettings.json` is replaced by each update: keep the settings of a photo booth in `appsettings.Local.json`

Linux has no automatic update yet (the `net10.0` target does not include AutoUpdater.NET).

## Development & Building

### Building from Source

```bash
# Clone the repository
git clone https://github.com/dansleboby/CanonWebAPI.git
cd CanonWebAPI

# Restore dependencies
dotnet restore CanonSDK.sln

# Build the solution
dotnet build CanonSDK.sln --configuration Release

# Run the API
dotnet run --project Canon.API -f net10.0-windows   # on Linux: -f net10.0
```

### Creating Releases

This project uses automated GitHub Actions for releases:

1. **Changelog**: Move the changes of the `Unreleased` section of [CHANGELOG.md](CHANGELOG.md) under the new version and date, and update the version in `Canon.API/Canon.API.csproj`
2. **Tag-based releases**: Push a tag like `v1.4.0.0` on the latest commit of `main` to trigger automated build and release (the workflow refuses a tag on another commit)
3. **Version synchronization**: The workflow automatically updates project versions to match the tag
4. **Unit tests**: The workflow runs `Canon.Core.Tests` and `Canon.API.Tests` before packaging
5. **Automatic packaging**: Creates release packages and updates the AutoUpdater XML
6. **GitHub releases**: Automatically creates GitHub releases with generated notes

`docs/autoupdate.xml` is updated by the workflow only: changing it by hand makes every installation download that version.

### Running the API

```bash
dotnet run --project Canon.API -f net10.0-windows   # on Linux: -f net10.0
```

### Running on Linux

The `net10.0` target of `Canon.API` runs on Linux x64 with the Linux version of the EDSDK (`EDSDK/linux-x64/libEDSDK.so`).
There is no automatic update and no release package for Linux yet: run it from the sources.

Prerequisites: .NET 10 SDK and `libusb-1.0` (Ubuntu: `sudo apt install dotnet-sdk-10.0 libusb-1.0-0`).

```bash
dotnet run --project Canon.API -f net10.0 --launch-profile http

# Every EDSDK event and each step of a capture (result and duration) in the logs:
dotnet run --project Canon.API -f net10.0 --launch-profile http -- --Logging:LogLevel:Canon.Core=Debug
```

*   The user running the API needs read/write access to the camera USB device; desktop sessions get it automatically.
*   Nothing else may use the camera: if the desktop mounted it, unmount it (GNOME: `gio mount -l`, then `gio mount -u <location>`), and do not pass it to a virtual machine.
*   Logs: console and `Canon.API/bin/Debug/net10.0/logs/`.

### Unit Tests

```bash
dotnet test CanonSDK.sln
```

The `CI` GitHub Actions workflow builds the solution and runs these tests on every pull request and push to `main`.

The tests cover the value tables, the settings rules, the capture timeout, the file type filter, the SDK thread, the live view
broadcaster, the structure layouts of the EDSDK wrapper, the error mapping of the API, the request models and the log levels.
They do not need a camera and also run on Linux/macOS.

### Docker (Windows containers)

The `Dockerfile` builds, tests and publishes the application in a Windows container (Docker Desktop in *Windows containers* mode, or Windows Server):

```powershell
# Build + unit tests only
docker build --target test -t canonwebapi:test .

# Build + tests + publish, then a runtime image for smoke tests
docker build -t canonwebapi .
docker run --rm -p 5159:5159 canonwebapi
# http://localhost:5159/status, http://localhost:5159/swagger

# Extract the published executable (same files as the GitHub release)
docker create --name canonwebapi-publish canonwebapi
docker cp canonwebapi-publish:C:\app .\publish
docker rm canonwebapi-publish
```

Windows containers cannot access USB devices, so the camera is not reachable from a container: `/status` reports `connected: false`. Tests with a camera are done on the photo booth machine.

### OpenAPI Document

The document is always available at `/openapi/v1.json`. To regenerate `docs/openapi.json` at build time (Windows or Linux):

```bash
dotnet build Canon.API -f net10.0 -t:Build -t:GenerateOpenApiDocuments
```

The build-time hook of the `Microsoft.Extensions.ApiDescription.Server` package skips the inner builds of a multi-targeted
project, so `-p:OpenApiGenerateDocumentsOnBuild=true` with `-f` generates nothing; the `net10.0` target keeps the Windows
update check out of the generator, which runs the application's entry point.

## Compatibility

### Tested Camera Models
*   **Canon EOS R100** ✅
*   **Canon T7** ✅

### System Requirements
*   **OS**: Windows 10/11 (x64); Linux x64 from the sources (see [Running on Linux](#running-on-linux))
*   **Runtime**: none to install: the release is a self-contained executable (.NET 10)
*   **Dependencies**: Canon EDSDK 13.20.21 64-bit libraries (included; up to 1.0.0.11: EDSDK 13.19.0)

> **Note**: While this software has been tested with the above camera models, it should work with other Canon cameras that support the EDSDK. However, functionality may vary depending on the specific camera model and its supported features.

## License notices

The Canon EDSDK libraries in `EDSDK/` are based in part on the work of the Independent JPEG Group.

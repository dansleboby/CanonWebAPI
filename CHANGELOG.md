# Changelog

All notable changes to CanonWebAPI are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions match the Git tags (`v1.0.0.11`) that trigger the release workflow.

## [Unreleased]

### Changed
- The release workflow refuses to run when the tag does not point to the latest commit of `main`,
  so the published package always matches the tag.

- **Migrated from .NET 9 to .NET 10 (LTS)**, supported until November 2028 (.NET 9 support ends in November 2026).
  The release is still a self-contained executable: nothing to install on the photo booth computers.
- Packages updated (ASP.NET Core OpenAPI 10, Serilog.AspNetCore 10, Swagger UI 10, AutoUpdater.NET 1.9.3).
- The OpenAPI document is now OpenAPI 3.1 (was 3.0), without duplicated schemas.

### Added
- CI workflow building the solution and running the unit tests on every pull request and push to `main`.

## [1.1.0.0] - Unreleased

### Changed
- **Canon EDSDK updated from 13.19.0 to 13.20.21** (64-bit). Adds support for the EOS R6 Mark III and EOS R6V.
  `EdsImage.dll` is now the 64-bit library (Canon shipped the 32-bit one in the 64-bit folder of 13.19.0).
- P/Invoke wrapper (`EDSDK.cs`) aligned with the 13.20.21 headers: new error codes (no lens, retracted lens,
  flash charging, movie mode...), properties, events, `EdsEvfAFMode` and `EdsEvfZoom` values,
  `EdsGetPropertyDescEx`, `EdsCreateFlashSettingRef`, `EdsGetCsdFileData` / `EdsSetCsdFileData`.
- **Errors are returned as problem details** (`application/problem+json`) with consistent status codes:
  400 invalid value, 404 nothing available, 409 capture refused by the camera, 503 camera not connected or busy
  (`Retry-After`), 504 capture timeout (was 408). The EDSDK error code is in `errorCode`.
- Values unknown to the value tables are returned as raw hexadecimal values (e.g. `"0x99"`, was decimal)
  and can be sent back to set them.
- Setting a value is checked against the values the camera accepts now; the error lists the accepted values.
- Capture timeout adapts to the exposure time of the current shutter speed (`Canon:CaptureTimeoutSeconds` + exposure).
- Pictures are taken with *press / release shutter button* (EDSDK sample 9) instead of the legacy TakePicture command.
  "Device busy" answers are retried; only the press is retried and the button is always released.
- `/autofocus` holds the shutter button halfway for 800 ms (was 100 ms, often too short to focus),
  configurable with `Canon:AutoFocusHoldMilliseconds`.
- The live view is downloaded once and shared by every `/videostream` client. It is started with the first client
  and stopped a few seconds after the last one left (it used to stay on as long as the application ran).
- The OpenAPI document (`/openapi/v1.json`) and Swagger UI (`/swagger`) are available in every environment.

### Added
- `GET /status`: connection, camera name, shooting mode, temperature, flash and live view state. Never fails because of the camera.
- `GET /mode`: shooting mode (mode dial position), whether exposure settings can be changed remotely, movie mode.
- `GET /temperature`: restrictions applied by the camera because of its internal temperature.
- `GET /flash`, `POST /flash`: "flash firing" setting. By default it is forced to Fire when the camera connects and
  before every capture (`Canon:ForceFlashFiring`, `Canon:FlashTarget`), for a studio flash on the accessory shoe.
- `GET /liveview`: a single live view image.
- `POST /takepicture?fileTypes=...`: choose the returned file type (`jpg`, `cr3`, `heif`, `*`). JPEG only by default
  (`Canon:CaptureFileTypes`). The file name is returned in the `X-File-Name` header.
- Automatic reconnection when the camera is turned off or unplugged and plugged back (hot plug).
- The camera auto power off timer is extended when the camera announces it will turn off (`Canon:PreventAutoPowerOff`).
- Configuration in `appsettings.json`: `Canon`, `LiveView` and `AutoUpdate` sections (see README).
  `AutoUpdate:Enabled` can disable the update check.
- Live view options: `Canon:LiveViewSmallImage`, `Canon:KeepCameraScreenOn`.
- `docs/openapi.json`: copy of the OpenAPI document.
- `Canon.Core.Tests`: unit tests (xUnit) that do not need a camera; run by the release workflow.
- `Dockerfile` (Windows containers) to build, test and publish the application.

### Removed
- `Canon.Test.Avalonia` desktop proof of concept and `Canon.Test` console application, used during the initial
  development. The API (`/status`, `/takepicture`...) and the `Canon.Core.Tests` unit tests replace them.

### Fixed
- Exposure compensation values were unusable (the two columns of the documentation table had been merged,
  only 11 positive values with invalid labels). All 41 values from -5 to +5 are now available.
- Missing ISO (64000 to 819200), shutter speed (1/10000 to 1/32000), aperture (f/3.4) and white balance values.
- The download progress callback was registered after the download and never called.
- Every capture left an empty temporary file behind; pictures are now downloaded in memory.
- Unwanted files (e.g. RAW) and failed downloads are cancelled on the camera (`EdsDownloadCancel`) instead of being
  left in its buffer.
- The SDK was initialized again after each disconnection without being terminated, and the session was never closed.
  The SDK is now initialized once, and the session is closed on disconnection and when the application stops.
- Possible deadlock when the application stopped (SDK thread), and requests left waiting forever.
- MJPEG stream boundary did not match the declared one (`--frame` vs `----frame`).
- Writing an error after the video stream had started threw an exception.
- A late file from a timed out capture could be returned by the next capture.
- `/cameraname` and the GET setting endpoints returned 500 for camera errors.
- Live view is enabled when disabled in the camera settings (`Evf_Mode`).

## [1.0.0.11] - 2025-11-16

### Fixed
- Better error handling when taking pictures: capture errors reported by the camera (focus failure, card...)
  are translated into readable messages instead of timing out.
- Safer completion of pending captures and clearer timeout messages.

## [1.0.0.10] - 2025-10-11

### Changed
- Release package renamed to `CanonWebAPI.zip`.

## [1.0.0.9] - 2025-10-11

### Changed
- The API is published as a self-contained single-file executable (no .NET runtime to install).

### Added
- Project documentation (`CLAUDE.md`).

## [1.0.0.8] - 2025-09-11

### Changed
- Release workflow: branch checkout clarified.

## [1.0.0.7] - 2025-09-11

### Changed
- Build workflow replaced by a tag-based release process.

## [1.0.0.5] - 2025-09-10

### Added
- Assembly version automatically updated from the release tag.

## [1.0.0.4] - 2025-09-10

### Fixed
- Release workflow: more reliable branch checkout.

## [1.0.0.3] - 2025-09-10

### Fixed
- Release workflow: commit of the AutoUpdater XML.

## [1.0.0.2] - 2025-09-10

### Fixed
- Release workflow: GitHub Actions permissions.

## [1.0.0.1] - 2025-09-10

### Fixed
- Release workflow: YAML error.

## [1.0.0.0] - 2025-09-10

First release.

### Added
- REST API to control a Canon camera through the Canon EDSDK: ISO, aperture, shutter speed, exposure compensation
  and white balance (get / set with supported values), picture capture with or without autofocus, latest picture,
  autofocus, MJPEG live view stream, camera name.
- `Canon.Core` library wrapping the EDSDK, with a dedicated SDK thread and property / progress events.
- `Canon.Test` console and `Canon.Test.Avalonia` desktop test applications.
- Serilog logging (console and `logs/canon-api.log`), Swagger in development.
- Automatic updates with AutoUpdater.NET, from GitHub releases.
- CI/CD workflow building the release package.

[Unreleased]: https://github.com/dansleboby/CanonWebAPI/compare/v1.1.0.0...HEAD
[1.1.0.0]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.11...v1.1.0.0
[1.0.0.11]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.10...v1.0.0.11
[1.0.0.10]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.9...v1.0.0.10
[1.0.0.9]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.8...v1.0.0.9
[1.0.0.8]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.7...v1.0.0.8
[1.0.0.7]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.5...v1.0.0.7
[1.0.0.5]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.4...v1.0.0.5
[1.0.0.4]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.3...v1.0.0.4
[1.0.0.3]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.2...v1.0.0.3
[1.0.0.2]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.1...v1.0.0.2
[1.0.0.1]: https://github.com/dansleboby/CanonWebAPI/compare/v1.0.0.0...v1.0.0.1
[1.0.0.0]: https://github.com/dansleboby/CanonWebAPI/releases/tag/v1.0.0.0

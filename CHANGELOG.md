# Changelog

All notable changes to CanonWebAPI are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions match the Git tags (`v1.0.0.11`) that trigger the release workflow.

## [Unreleased]

### Added
- Linux package in every release: `CanonWebAPI-linux-x64.tar.gz`, a self-contained executable for Linux x64 with the
  Linux EDSDK, and `install.sh`, which installs it as a systemd service with the USB access it needs
  (`/opt/canonwebapi`, `canonwebapi` user, udev rule); `install.sh --uninstall` removes it.
- `tools/Test-Camera.ps1` (with `Test-Camera.cmd`): runs the photo booth camera checks against the API (live view,
  captures, flash changes during the live view, power cycle) and saves the pictures, the results and the API log.
- Linux x64 support: the `net10.0` target of `Canon.API` runs on Linux with the Linux version of the EDSDK
  (`EDSDK/linux-x64/libEDSDK.so`, needs `libusb-1.0`). No automatic update and no release package on Linux yet.
- Debug logs (`Logging:LogLevel:Canon.Core` = `Debug`): every EDSDK event, and each step of a capture (UI lock, flash
  settings, shutter button, transfer) with its result and duration.

### Changed
- `Canon.API` has two target frameworks: `dotnet run` and `dotnet publish` need `-f net10.0-windows` (Windows) or
  `-f net10.0` (Linux). `Canon.Core` and the unit tests target `net10.0`.
- A failed UI unlock after setting the flash is logged as a warning; it was ignored.
- The problem title returned when the Canon EDSDK cannot be loaded now names both platforms: "Canon EDSDK could not be
  loaded (EDSDK.dll or libEDSDK.so missing, wrong architecture, or libusb-1.0 missing on Linux)" (was "...(EDSDK.dll
  missing or wrong architecture)"). Client-visible text.
- Regenerating `docs/openapi.json` at build time now uses
  `dotnet build Canon.API -f net10.0 -t:Build -t:GenerateOpenApiDocuments` (Windows or Linux); the former
  `-p:OpenApiGenerateDocumentsOnBuild=true` form generates nothing now that `Canon.API` has two target frameworks.

### Fixed
- The camera froze when a picture was taken while the live view ran, as in the photo booth: the shutter stayed busy
  (503 "Camera busy"), no picture was returned, and the camera did not respond until it was turned off and on. Writing
  the flash setting during the live view freezes the camera (EOS R100), and `Canon:ForceFlashFiring` wrote it before
  every capture. It is now written when the camera connects, before the live view starts (once per live view session)
  and before a capture taken without live view; `POST /flash` pauses the live view while it writes it. With
  `Canon:ForceFlashFiring`, a value set with `POST /flash` therefore stays until the live view stops (it was set back to
  Fire before every capture), and `GET /flash` returns the last value set while the live view runs.
- Linux: the application crashed when the camera was turned off or unplugged. The SDK closes the session itself then,
  and releasing the camera closed it a second time, which crashed (`CLinuxPtpHelper::Terminate` from `EdsRelease`). On
  Linux the camera object of a camera that is gone is now left to the SDK.
- The session of a camera that is gone (turned off, unplugged, or a call failed because it is disconnected) is no longer
  closed before the camera is released, on Windows too, as in the Canon samples.
- Linux: a camera turned back on or plugged back in was not reconnected, because the SDK lists it a moment after
  reporting it (about 1 s). The connection is now retried for about 10 s.

## [1.4.0.0] - 2026-09-30

### Added
- `appsettings.Local.json` (next to `appsettings.json`) for the settings of a photo booth. It is not part of the release
  package, so automatic updates no longer reset them: move any setting changed in `appsettings.json` to this file.
- `exposureCompensation` in `GET /settings` and `POST /settings`.
- `Canon.API.Tests`: unit tests of the error mapping, the request models and the log levels, run by the CI and release
  workflows and by the Dockerfile.

### Changed
- Exposure compensation can be set in manual exposure mode (M) when ISO is Auto (also when `POST /settings` sets ISO
  to Auto in the same call), as the camera allows. It was always refused in M.
- An aperture or shutter speed label shared by a 1/2 and a 1/3 stop value (e.g. `"2.5"` and `"2.5 (1/3)"`) is set to the
  one the camera accepts with its current exposure step, instead of being refused.
- f/4.5 values are labelled like the other pairs: 0x2B (1/3 stop) is now `"4.5 (1/3)"` and 0x2C (1/2 stop) `"4.5"`.
  Sending `"4.5"` still works with both exposure steps.
- `POST /flash` waits for a capture or an autofocus in progress to finish.
- `POST /settings` refuses an unknown field (400) instead of ignoring it, e.g. a misspelled setting.
- Settings files and logs (`logs/canon-api.log`) are next to the executable whatever the working directory (shortcut,
  scheduled task...); `appsettings.json` was only read from the working directory.
- `Logging:LogLevel` applies to the logs: it was ignored, and every request was logged by ASP.NET Core.
- `Retry-After` can be read by browser clients (CORS).
- OpenAPI document: HEIF and CR3 content types for `/takepicture` and `/latestpicture`, 409 for `/autofocus` and
  `POST /flash`, no 503 for `/status` and `/latestpicture`.
- The update check no longer tries to stop the web server before updating: it runs before the server starts.

### Fixed
- Automatic updates overwrote `appsettings.json`, resetting the settings of the photo booth (see `appsettings.Local.json`).
- `Canon:CaptureFileTypes` always included `jpg`: the configured types were added to the default one instead of replacing
  it (e.g. `["cr3"]` also accepted JPEG files). The documentation no longer claims an order of preference: the first
  matching file the camera sends is returned.
- An open `/videostream` delayed the application shutdown by 30 s; closing the window during that time left the camera
  session open. Streams now end as soon as the application stops.
- The file of a capture whose request was cancelled by the client could answer the next capture.
- `/liveview` could return a frame up to a few seconds old after the last `/videostream` client left, or a frame of a
  previous live view.
- EDSDK wrapper declarations not used by the application, aligned with the 13.20.21 headers: `EdsWrite` (64-bit written
  size), `EdsCreateFileStreamEx` (declared under a name the DLL does not export), `EdsSetFramePoint`, `FocusShiftSetting`
  (8 fields) and `EdsManualWBData` (header layout and serialization helpers).
- Unit tests that could not detect the regression they were written for (live view errors, shared live view producer)
  or depended on timing (SDK thread disposal).

## [1.3.0.0] - 2026-09-30

### Added
- `GET /settings`: shooting mode, plus ISO, aperture, shutter speed and white balance in one call, each with its value,
  the values the camera accepts now and whether it can be changed in the current shooting mode (`settable`).
- `POST /settings`: applies several settings at once (e.g. a day / night preset). Every value is checked before anything
  is written; they are then written in a fixed order, never during a capture, and the settings read back from the camera
  are returned. A write failure lists the settings already written (`applied`) and the one that failed (`failed`).

### Changed
- **Setters (`/iso`, `/aperture`, `/shutterspeed`, `/exposure`, `/whitebalance`) check that the setting can be changed in the
  current shooting mode**: 409 with reason `not-settable-in-mode` (e.g. shutter speed in Av, anything in a scene mode,
  exposure compensation in M). The camera used to be asked anyway, answering 400, 409 or 500 depending on the SDK error.
- Setting errors are machine readable: `reason` (`invalid-value`, `value-not-accepted`, `not-settable-in-mode`), `property`,
  `acceptedValues`, `aeMode`, `aeModeCode` and `errors` in the problem details (the accepted values used to be only in the message).
- Setting a value waits for a capture or an autofocus in progress to finish.
- A setting for which the camera lists no settable value (movie mode, no lens...) is refused with 409 `not-settable-in-mode`,
  as the Canon samples disable it; the EDSDK 13.20 API reference asks to set only values from that list.
- ISO, aperture, shutter speed and exposure compensation read `"Not valid"` instead of `"0xFFFFFFFF"` when the setting is not
  valid in the current state (e.g. exposure compensation in M).
- `GET /whitebalance` returns an empty `supportedValues` instead of failing when the camera does not list the white balance values.

## [1.2.0.0] - 2026-09-30

### Changed
- **Migrated from .NET 9 to .NET 10 (LTS)**, supported until November 2028 (.NET 9 support ends in November 2026).
  The release is still a self-contained executable: nothing to install on the photo booth computers.
- Packages updated (ASP.NET Core OpenAPI 10, Serilog.AspNetCore 10, Swagger UI 10, AutoUpdater.NET 1.9.3).
- The OpenAPI document is now OpenAPI 3.1 (was 3.0), without duplicated schemas.
- The release workflow refuses to run when the tag does not point to the latest commit of `main`,
  so the published package always matches the tag.

### Added
- CI workflow building the solution and running the unit tests on every pull request and push to `main`.

## [1.1.0.0] - 2026-09-30

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

[Unreleased]: https://github.com/dansleboby/CanonWebAPI/compare/v1.4.0.0...HEAD
[1.4.0.0]: https://github.com/dansleboby/CanonWebAPI/compare/v1.3.0.0...v1.4.0.0
[1.3.0.0]: https://github.com/dansleboby/CanonWebAPI/compare/v1.2.0.0...v1.3.0.0
[1.2.0.0]: https://github.com/dansleboby/CanonWebAPI/compare/v1.1.0.0...v1.2.0.0
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

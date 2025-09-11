# CanonWebAPI

> **⚠️ BETA SOFTWARE**: This project is currently in beta stage. While functional, it may contain bugs and is subject to breaking changes. Use with caution in production environments.

A web API for remotely controlling Canon DSLR and mirrorless cameras. This project utilizes the Canon EDSDK to communicate with the camera and includes automatic update capabilities.

## Features

*   Get camera information (e.g., camera name).
*   Get and set camera settings:
    *   ISO Speed
    *   Aperture
    *   Shutter Speed
    *   Exposure Compensation
    *   White Balance
*   Take pictures and download them.
*   Live view streaming via MJPEG.
*   Trigger autofocus.
*   Retrieve the last taken picture from the camera.
*   **Automatic updates** via AutoUpdater.NET integration.
*   **Version display** in console on startup.

## Project Structure

The solution is divided into the following projects:

*   `Canon.API`: An ASP.NET Core web application that exposes the camera controls as a RESTful API.
*   `Canon.Core`: A .NET library that wraps the Canon EDSDK, providing a higher-level interface to interact with the camera.
*   `Canon.Test`: A simple console application for testing the `Canon.Core` library.
*   `Canon.Test.Avalonia`: A desktop application built with Avalonia UI for testing camera functionalities.
*   `EDSDK`: Contains the necessary Canon EDSDK libraries (`EDSDK.dll`, `EdsImage.dll`).

## Getting Started

### Prerequisites

*   A compatible Canon camera (tested with **Canon EOS R100** and **Canon T7**).
*   The camera connected to the computer via USB.
*   **.NET 9 SDK** (or newer).
*   The Canon EOS Utility software should not be running, as it can prevent this application from connecting to the camera.
*   **Windows** operating system (x64 architecture required).

### Installation

1.  Clone this repository.
2.  Ensure the `EDSDK` folder, containing `EDSDK.dll` and `EdsImage.dll`, is present in the project's root directory. These files are essential for the `Canon.Core` library to communicate with the camera.
3.  Build the solution using Visual Studio or the `dotnet build` command.
4.  Run the `Canon.API` project. This will start the web server.

## API Endpoints

The following endpoints are available once the `Canon.API` project is running:

| Method | Path                         | Description                                             | Request Body (Example) |
|--------|------------------------------|---------------------------------------------------------|------------------------|
| GET    | `/cameraname`                | Gets the connected camera's name.                       | N/A                    |
| GET    | `/iso`                       | Gets the current ISO speed and a list of supported values. | N/A                    |
| POST   | `/iso`                       | Sets the ISO speed.                                     | `{"value": "100"}`     |
| GET    | `/aperture`                  | Gets the current aperture and a list of supported values. | N/A                    |
| POST   | `/aperture`                  | Sets the aperture.                                      | `{"value": "5.6"}`     |
| GET    | `/shutterspeed`              | Gets the current shutter speed and a list of supported values. | N/A                    |
| POST   | `/shutterspeed`              | Sets the shutter speed.                                 | `{"value": "1/125"}`   |
| GET    | `/exposure`                  | Gets the current exposure compensation and supported values. | N/A                    |
| POST   | `/exposure`                  | Sets the exposure compensation.                         | `{"value": "+1"}`      |
| GET    | `/whitebalance`              | Gets the current white balance and supported values.    | N/A                    |
| POST   | `/whitebalance`              | Sets the white balance.                                 | `{"value": "Auto"}`    |
| POST   | `/takepicture`               | Takes a picture and returns the JPEG image. The `useAutoFocus` query parameter (default `true`) can be used to control autofocus. | N/A |
| GET    | `/videostream`               | Starts an MJPEG live view stream.                       | N/A                    |
| GET    | `/latestpicture`             | Gets the last picture taken from the camera's memory.   | N/A                    |
| POST   | `/autofocus`                 | Triggers the camera's autofocus mechanism.              | N/A                    |

## Automatic Updates

This application includes automatic update functionality powered by AutoUpdater.NET:

*   **Automatic version checking** on startup
*   **Seamless updates** without requiring admin privileges
*   **Graceful shutdown** handling for web server during updates
*   Updates are downloaded from GitHub releases automatically

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
dotnet run --project Canon.API
```

### Creating Releases

This project uses automated GitHub Actions for releases:

1. **Tag-based releases**: Push a tag like `v1.0.0.9` to trigger automated build and release
2. **Version synchronization**: The workflow automatically updates project versions to match the tag
3. **Automatic packaging**: Creates release packages and updates the AutoUpdater XML
4. **GitHub releases**: Automatically creates GitHub releases with generated notes

### Running Different Projects

```bash
# Run the Web API
dotnet run --project Canon.API

# Run the Console Test Application  
dotnet run --project Canon.Test

# Run the Avalonia GUI Test Application
dotnet run --project Canon.Test.Avalonia
```

## Compatibility

### Tested Camera Models
*   **Canon EOS R100** ✅
*   **Canon T7** ✅

### System Requirements
*   **OS**: Windows (x64)
*   **Runtime**: .NET 9 or newer
*   **Dependencies**: Canon EDSDK libraries (included)

> **Note**: While this software has been tested with the above camera models, it should work with other Canon cameras that support the EDSDK. However, functionality may vary depending on the specific camera model and its supported features.

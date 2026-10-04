# Linux Support, Step 1 - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run CanonWebAPI on Linux x64 against a real camera, with Debug logs of every EDSDK event and capture step, then use it to find why the photo booth R100 froze on capture.

**Architecture:** Canon.Core becomes a plain `net10.0` library whose P/Invokes load `"EDSDK"` (`EDSDK.dll` on Windows, `libEDSDK.so` on Linux). Canon.API targets `net10.0-windows` (unchanged Windows release with AutoUpdater.NET) and `net10.0` (Linux, no automatic update); each target copies its own native library. A `TraceCall` helper in `CanonCamera` logs the result and duration of the SDK calls of a capture.

**Tech Stack:** .NET 10 (SDK 10.0.112 on the dev machine), ASP.NET Core, Serilog, xUnit, Canon EDSDK 13.20.21 (Windows DLLs, Linux x86_64 `libEDSDK.so`).

**Spec:** `specs/2026-10-04-linux-edsdk-design.md`

## Global Constraints

- Branch `feature/linux-edsdk`. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Target frameworks: `net10.0` (Canon.Core, Canon.Core.Tests, Canon.API.Tests); `net10.0-windows;net10.0` (Canon.API). `PlatformTarget` stays `x64`.
- The Windows release is unchanged: self-contained single-file `win-x64` executable, `EDSDK.dll` and `EdsImage.dll` next to it, AutoUpdater.NET.
- No automatic update on Linux.
- Linux library: `EDSDK/linux-x64/libEDSDK.so`, taken from `EDSDK/Library/x86_64/libEDSDK.so` of `~/Downloads/EDSDK_v13.20.21_Linux.zip`.
- Code comments in English, senior altitude (only what the code cannot say). ASCII punctuation in code, comments and docs.
- Every change is recorded in the `Unreleased` section of `CHANGELOG.md`.
- Build and tests on the dev machine (Linux): `dotnet build CanonSDK.sln`, `dotnet test CanonSDK.sln`. Building the Windows target on Linux works thanks to `EnableWindowsTargeting` in `Directory.Build.props`.

## File Structure

| File | Change |
|------|--------|
| `Canon.Core/EDSDK.cs` | Library name constant, 55 `DllImport`s use it |
| `Canon.Core/Canon.Core.csproj` | `net10.0`; native library items move to Canon.API |
| `Canon.Core/EdsdkHelper.cs` | `DescribeEvent`, `DescribeResult` |
| `Canon.Core/CanonCamera.cs` | `TraceCall` helper, Debug logs of events and capture steps, UI unlock warning |
| `Canon.Core.Tests/Canon.Core.Tests.csproj`, `Canon.API.Tests/Canon.API.Tests.csproj` | `net10.0` |
| `Canon.Core.Tests/EdsdkInteropTests.cs` | Test: every import uses `"EDSDK"` |
| `Canon.Core.Tests/EdsdkHelperTests.cs` | Tests of the event and result names |
| `Canon.API/Canon.API.csproj` | Two targets, Windows-only properties and package, native libraries per target |
| `Canon.API/Program.cs` | AutoUpdater under `#if WINDOWS` |
| `Canon.API/Infrastructure/CameraExceptionHandler.cs` | Platform-neutral "EDSDK could not be loaded" title |
| `EDSDK/linux-x64/libEDSDK.so` | New |
| `.github/workflows/build.yml`, `Dockerfile`, `build.bat` | Publish with `-f net10.0-windows` |
| `Directory.Build.props` | Comment |
| `README.md`, `CLAUDE.md`, `CHANGELOG.md` | Linux, commands, logs |

---

### Task 1: Canon.Core loads the EDSDK by a cross-platform name and targets net10.0

**Files:**
- Modify: `Canon.Core/EDSDK.cs:9-12` and every `[DllImport("EDSDK.dll")]`
- Modify: `Canon.Core/Canon.Core.csproj`, `Canon.Core.Tests/Canon.Core.Tests.csproj`, `Canon.API.Tests/Canon.API.Tests.csproj`
- Test: `Canon.Core.Tests/EdsdkInteropTests.cs`

**Interfaces:**
- Produces: `EDSDK.Library` (`private const string`, value `"EDSDK"`), used by every `DllImport` of `EDSDK`.

- [ ] **Step 1: Write the failing test**

In `Canon.Core.Tests/EdsdkInteropTests.cs`, add `using System.Reflection;` to the usings, and this test to the `EdsdkInteropTests` class:

```csharp
    [Fact]
    public void Every_import_uses_the_cross_platform_library_name()
    {
        var imports = typeof(EDSDK).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<DllImportAttribute>())
            .OfType<DllImportAttribute>()
            .ToList();

        // Guards against a reflection change returning nothing, which would make the test pass vacuously.
        Assert.True(imports.Count > 50, $"{imports.Count} imports found");
        Assert.All(imports, import => Assert.Equal("EDSDK", import.Value));
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Canon.Core.Tests --filter "FullyQualifiedName~Every_import_uses_the_cross_platform_library_name"`
Expected: FAIL, `Assert.Equal() Failure` with expected `"EDSDK"` and actual `"EDSDK.dll"`.

- [ ] **Step 3: Use the library name constant**

In `Canon.Core/EDSDK.cs`, replace:

```csharp
internal class EDSDK
{
    [DllImport("EDSDK.dll")]
    public static extern uint EdsGetEvent();
```

with:

```csharp
internal class EDSDK
{
    // Resolved to EDSDK.dll on Windows and libEDSDK.so on Linux.
    private const string Library = "EDSDK";

    [DllImport(Library)]
    public static extern uint EdsGetEvent();
```

Then replace the other imports:

```bash
sed -i 's/\[DllImport("EDSDK.dll")\]/[DllImport(Library)]/' Canon.Core/EDSDK.cs
grep -c 'DllImport(Library)' Canon.Core/EDSDK.cs   # 55
grep -c 'EDSDK.dll' Canon.Core/EDSDK.cs            # 0
```

- [ ] **Step 4: Target net10.0**

In `Canon.Core/Canon.Core.csproj`, `Canon.Core.Tests/Canon.Core.Tests.csproj` and `Canon.API.Tests/Canon.API.Tests.csproj`, replace:

```xml
    <TargetFramework>net10.0-windows</TargetFramework>
```

with:

```xml
    <TargetFramework>net10.0</TargetFramework>
```

Keep the native library items of `Canon.Core.csproj` for now: Task 2 moves them.

- [ ] **Step 5: Run the tests**

Run: `dotnet test CanonSDK.sln`
Expected: PASS for both test projects (`Failed: 0`), build with 0 warnings. The new test passes.

- [ ] **Step 6: Commit**

```bash
git add Canon.Core/EDSDK.cs Canon.Core/Canon.Core.csproj Canon.Core.Tests/Canon.Core.Tests.csproj Canon.API.Tests/Canon.API.Tests.csproj Canon.Core.Tests/EdsdkInteropTests.cs
git commit -m "Load the EDSDK by a cross-platform library name and target net10.0 in Canon.Core

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Canon.API runs on Linux (net10.0 target)

**Files:**
- Create: `EDSDK/linux-x64/libEDSDK.so`
- Modify: `Canon.API/Canon.API.csproj`, `Canon.Core/Canon.Core.csproj`, `Canon.API/Program.cs`, `Canon.API/Infrastructure/CameraExceptionHandler.cs:118`
- Modify: `Canon.API.Tests/Canon.API.Tests.csproj` (comment), `Directory.Build.props` (comment)
- Modify: `.github/workflows/build.yml:110`, `Dockerfile:49`, `build.bat`
- Modify: `README.md`, `CLAUDE.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: Task 1 (`Canon.Core` targets `net10.0`, imports load `"EDSDK"`).
- Produces: `dotnet run --project Canon.API -f net10.0 --launch-profile http` runs the API on Linux at `http://localhost:5159`, with `libEDSDK.so` in `Canon.API/bin/Debug/net10.0/`.

- [ ] **Step 1: Verify the Linux target does not exist yet**

Run: `dotnet build Canon.API -f net10.0`
Expected: FAIL (restore or build error: the project has no `net10.0` target yet).

- [ ] **Step 2: Add the Linux library**

```bash
unzip -j -o ~/Downloads/EDSDK_v13.20.21_Linux.zip 'EDSDK/Library/x86_64/libEDSDK.so' -d EDSDK/linux-x64
file EDSDK/linux-x64/libEDSDK.so   # ELF 64-bit LSB shared object, x86-64
```

- [ ] **Step 3: Move the native library items to Canon.API**

In `Canon.Core/Canon.Core.csproj`, delete this item group:

```xml
  <ItemGroup>
    <None Include="..\EDSDK\EDSDK.dll" Link="EDSDK.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </None>
    <None Include="..\EDSDK\EdsImage.dll" Link="EdsImage.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </None>
  </ItemGroup>
```

- [ ] **Step 4: Give Canon.API two targets**

Replace the whole `Canon.API/Canon.API.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <!-- net10.0-windows: Windows release, with AutoUpdater.NET (Windows Desktop runtime). net10.0: Linux, no automatic update. -->
    <TargetFrameworks>net10.0-windows;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
	  <PlatformTarget>x64</PlatformTarget>

    <!-- Assembly Version Configuration -->
    <AssemblyVersion>1.4.0.0</AssemblyVersion>
    <FileVersion>1.4.0.0</FileVersion>
    <Version>1.4.0.0</Version>

    <!-- OpenAPI document generated at build time only when requested (see README) -->
    <OpenApiGenerateDocumentsOnBuild>false</OpenApiGenerateDocumentsOnBuild>
    <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/../docs</OpenApiDocumentsDirectory>
    <OpenApiGenerateDocumentsOptions>--file-name openapi</OpenApiGenerateDocumentsOptions>
  </PropertyGroup>

  <!-- Self-Contained Single-File Deployment (Windows release) -->
  <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove="logs\**" />
    <Content Remove="logs\**" />
    <EmbeddedResource Remove="logs\**" />
    <None Remove="logs\**" />
  </ItemGroup>

  <ItemGroup>
    <!-- Operator settings: copied next to the executable for dotnet run, never published so an automatic update cannot overwrite them. -->
    <Content Update="appsettings.Local.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="Never" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
    <PackageReference Include="Swashbuckle.AspNetCore.SwaggerUI" Version="10.2.3" />
    <!-- Build-time generation of docs/openapi.json, opt-in: dotnet build Canon.API -f net10.0 -t:Build -t:GenerateOpenApiDocuments -->
    <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.12">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
    <PackageReference Include="Autoupdater.NET.Official" Version="1.9.3" />
  </ItemGroup>

  <!-- Canon EDSDK native libraries, next to the executable (outside the single file). -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
    <None Include="..\EDSDK\EDSDK.dll" Link="EDSDK.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </None>
    <None Include="..\EDSDK\EdsImage.dll" Link="EdsImage.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </None>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <None Include="..\EDSDK\linux-x64\libEDSDK.so" Link="libEDSDK.so">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </None>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Canon.Core\Canon.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Compile AutoUpdater for Windows only**

In `Canon.API/Program.cs`, replace:

```csharp
using Canon.API.Infrastructure;
using Serilog;
using AutoUpdaterDotNET;
```

with:

```csharp
using Canon.API.Infrastructure;
using Serilog;
#if WINDOWS
using AutoUpdaterDotNET;
#endif
```

Replace:

```csharp
    if (app.Configuration.GetValue("AutoUpdate:Enabled", true))
        ConfigureAutoUpdater(app);
    else
        Log.Information("Automatic updates are disabled");
```

with:

```csharp
#if WINDOWS
    if (app.Configuration.GetValue("AutoUpdate:Enabled", true))
        ConfigureAutoUpdater(app);
    else
        Log.Information("Automatic updates are disabled");
#else
    Log.Information("Automatic updates are not available on this platform");
#endif
```

Put `#if WINDOWS` on the line before `static void ConfigureAutoUpdater(WebApplication app)` and `#endif` after its closing brace (end of file).

- [ ] **Step 6: Platform-neutral load error and comments**

In `Canon.API/Infrastructure/CameraExceptionHandler.cs`, replace:

```csharp
        DllNotFoundException or BadImageFormatException => (StatusCodes.Status500InternalServerError, "Canon EDSDK could not be loaded (EDSDK.dll missing or wrong architecture)"),
```

with:

```csharp
        DllNotFoundException or BadImageFormatException => (StatusCodes.Status500InternalServerError, "Canon EDSDK could not be loaded (EDSDK.dll or libEDSDK.so missing, wrong architecture, or libusb-1.0 missing on Linux)"),
```

In `Canon.API.Tests/Canon.API.Tests.csproj`, replace:

```xml
  <!--
    Canon.API needs the Windows Desktop runtime (AutoUpdater.NET): referencing it would make these tests Windows only.
    The parts under test do not depend on it, so their sources are compiled here instead.
  -->
```

with:

```xml
  <!-- The parts under test are compiled from the Canon.API sources, so these tests do not depend on the API project and its packages. -->
```

In `Directory.Build.props`, replace:

```xml
    <!-- Allows building the net10.0-windows projects (and running the unit tests) on Linux/macOS, e.g. in CI or dev containers. -->
```

with:

```xml
    <!-- Allows building the Windows target of Canon.API on Linux/macOS, e.g. in CI or dev containers. -->
```

- [ ] **Step 7: Publish the Windows target explicitly**

- `.github/workflows/build.yml`, step "Publish Canon.API as standalone executable":
  `run: dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0-windows --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ./publish`
- `Dockerfile`, publish stage:
  `RUN dotnet publish Canon.API/Canon.API.csproj -c Release -f net10.0-windows -r win-x64 --self-contained true `` ` ``
  (the next line, `-p:PublishSingleFile=true --no-restore -o C:\publish`, is unchanged)
- `build.bat`:
  `dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0-windows --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ./publish --verbosity minimal`

- [ ] **Step 8: Build and check the outputs**

```bash
dotnet build CanonSDK.sln
ls Canon.API/bin/Debug/net10.0/libEDSDK.so Canon.API/bin/Debug/net10.0-windows/win-x64/EDSDK.dll Canon.API/bin/Debug/net10.0-windows/win-x64/EdsImage.dll
ls Canon.API/bin/Debug/net10.0/ | grep -c -i 'EDSDK.dll\|EdsImage.dll'          # 0
ls Canon.API/bin/Debug/net10.0-windows/win-x64/ | grep -c 'libEDSDK.so'         # 0
dotnet test CanonSDK.sln
```

Expected: build with 0 warnings and 0 errors; the three files listed; both counts 0; tests `Failed: 0`.

- [ ] **Step 9: Check the Windows release publish from Linux**

```bash
P=$(mktemp -d)
dotnet publish Canon.API/Canon.API.csproj --configuration Release --framework net10.0-windows --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output "$P"
ls "$P"   # Canon.API.exe, EDSDK.dll, EdsImage.dll, appsettings*.json (no appsettings.Local.json, no libEDSDK.so)
rm -rf "$P"
```

- [ ] **Step 10: Start the API on Linux**

Run in the background: `dotnet run --project Canon.API -f net10.0 --launch-profile http`
Then: `curl -s http://localhost:5159/status` and `curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5159/openapi/v1.json`
Expected: the console log contains `Automatic updates are not available on this platform`; `/status` returns JSON (`connected` true when the camera is plugged in and free, otherwise false with an `error` that is not a `DllNotFoundException`); `/openapi/v1.json` returns 200. If the camera is plugged in and free but `connected` is false, stop and report the EDSDK error: no workaround in step 1 (spec, Risks). Stop the API.

- [ ] **Step 11: Regenerate the OpenAPI document with the Linux target**

Run: `dotnet build Canon.API -f net10.0 -t:Build -t:GenerateOpenApiDocuments && git diff --stat docs/openapi.json`
Expected: no difference (no endpoint changed). If the file differs, stop and report the difference.

Erratum: the `-p:OpenApiGenerateDocumentsOnBuild=true` form is a no-op in a multi-targeted project; the generated document also differs cosmetically from the committed one (schema order, `default: null`, final newline), so it is not regenerated in this plan.

- [ ] **Step 12: Documentation**

`README.md`:
- Project Structure, `EDSDK` line: `*   \`EDSDK\`: Canon EDSDK 13.20.21 64-bit libraries: \`EDSDK.dll\` and \`EdsImage.dll\` (Windows), \`linux-x64/libEDSDK.so\` (Linux).`
- Prerequisites, replace the Windows line with: `*   **Windows** or **Linux** (x64). On Linux, see [Running on Linux](#running-on-linux).`
- Installation step 2: `Ensure the \`EDSDK\` folder is present in the project's root directory: \`EDSDK.dll\` and \`EdsImage.dll\` on Windows, \`linux-x64/libEDSDK.so\` on Linux. These files are essential for the \`Canon.Core\` library to communicate with the camera.`
- "Building from Source" and "Running the API": `dotnet run --project Canon.API -f net10.0-windows` with the comment `# on Linux: -f net10.0`.
- After "Running the API", add:

````markdown
### Running on Linux

The `net10.0` target of `Canon.API` runs on Linux x64 with the Linux version of the EDSDK (`EDSDK/linux-x64/libEDSDK.so`).
There is no automatic update and no release package for Linux yet: run it from the sources.

Prerequisites: .NET 10 SDK and `libusb-1.0` (Ubuntu: `sudo apt install dotnet-sdk-10.0 libusb-1.0-0`).

```bash
dotnet run --project Canon.API -f net10.0 --launch-profile http
```

*   The user running the API needs read/write access to the camera USB device; desktop sessions get it automatically.
*   Nothing else may use the camera: if the desktop mounted it, unmount it (GNOME: `gio mount -l`, then `gio mount -u <location>`), and do not pass it to a virtual machine.
*   Logs: console and `Canon.API/bin/Debug/net10.0/logs/`.
````

- "OpenAPI Document": replace "To regenerate `docs/openapi.json` at build time (on Windows):" and its command with "To regenerate `docs/openapi.json` at build time (Windows or Linux):" and `dotnet build Canon.API -f net10.0 -t:Build -t:GenerateOpenApiDocuments`.
- System Requirements, OS line: `*   **OS**: Windows 10/11 (x64); Linux x64 from the sources (see [Running on Linux](#running-on-linux))`.

`CLAUDE.md`:
- Architecture, Canon.API line: `- **Canon.API**: ASP.NET Core Web API (main entry point); targets \`net10.0-windows\` (Windows release, AutoUpdater.NET) and \`net10.0\` (Linux, no automatic update)`
- Dependencies: after the EDSDK DLL line add `- Linux: \`EDSDK/linux-x64/libEDSDK.so\` (from \`EDSDK/Library/x86_64\` of the 13.20.21 Linux SDK), needs \`libusb-1.0\``; replace `- Platform target: x64 (Windows only)` with `- Platform target: x64 (Windows; Linux through the \`net10.0\` target of Canon.API)`.
- "Run API Server": `dotnet run --project Canon.API -f net10.0-windows   # Linux: -f net10.0 --launch-profile http`.
- "Build Specific Project": `dotnet build Canon.API` builds both targets (no change needed).
- Important Notes: replace `- All projects target .NET 10 (\`net10.0-windows\`) with Windows-specific dependencies` with `- Canon.Core and the tests target \`net10.0\`; Canon.API targets \`net10.0-windows\` (release, AutoUpdater.NET) and \`net10.0\` (Linux). Publish the release with \`-f net10.0-windows\``.

`CHANGELOG.md`, under `## [Unreleased]`:

```markdown
### Added
- Linux x64 support: the `net10.0` target of `Canon.API` runs on Linux with the Linux version of the EDSDK
  (`EDSDK/linux-x64/libEDSDK.so`, needs `libusb-1.0`). No automatic update and no release package on Linux yet.

### Changed
- `Canon.API` has two target frameworks: `dotnet run` and `dotnet publish` need `-f net10.0-windows` (Windows) or
  `-f net10.0` (Linux). `Canon.Core` and the unit tests target `net10.0`.
```

- [ ] **Step 13: Commit**

```bash
git add EDSDK/linux-x64/libEDSDK.so Canon.API/Canon.API.csproj Canon.Core/Canon.Core.csproj Canon.API/Program.cs Canon.API/Infrastructure/CameraExceptionHandler.cs Canon.API.Tests/Canon.API.Tests.csproj Directory.Build.props .github/workflows/build.yml Dockerfile build.bat README.md CLAUDE.md CHANGELOG.md
git commit -m "Run the API on Linux with a net10.0 target and the Linux EDSDK

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Names of EDSDK events and results

**Files:**
- Modify: `Canon.Core/EdsdkHelper.cs`
- Test: `Canon.Core.Tests/EdsdkHelperTests.cs`

**Interfaces:**
- Produces:
  - `public static string EdsdkHelper.DescribeEvent(uint eventId)`: the name of the `EDSDK` constant (`"StateEvent_Shutdown"`, `"ObjectEvent_DirItemRequestTransfer"`, `"PropertyEvent_PropertyChanged"`...), or `"0x..."` when unknown.
  - `public static string EdsdkHelper.DescribeResult(uint errorCode)`: `"OK"`, `"<message> (0x<code>)"` for a known error, `"0x<code>"` otherwise.

- [ ] **Step 1: Write the failing tests**

Add to the `EdsdkHelperTests` class in `Canon.Core.Tests/EdsdkHelperTests.cs`:

```csharp
    [Theory]
    [InlineData(EDSDK.StateEvent_Shutdown, "StateEvent_Shutdown")]
    [InlineData(EDSDK.StateEvent_CaptureError, "StateEvent_CaptureError")]
    [InlineData(EDSDK.ObjectEvent_DirItemRequestTransfer, "ObjectEvent_DirItemRequestTransfer")]
    [InlineData(EDSDK.PropertyEvent_PropertyChanged, "PropertyEvent_PropertyChanged")]
    [InlineData(0xABCDu, "0xABCD")]
    public void Events_are_named_after_their_constant(uint eventId, string name)
    {
        Assert.Equal(name, EdsdkHelper.DescribeEvent(eventId));
    }

    [Fact]
    public void Results_are_described_with_their_code()
    {
        Assert.Equal("OK", EdsdkHelper.DescribeResult(EDSDK.EDS_ERR_OK));
        Assert.Equal("Device is busy (0x2019)", EdsdkHelper.DescribeResult(EDSDK.EDS_ERR_PTP_DEVICE_BUSY));
        Assert.Equal("0x12345678", EdsdkHelper.DescribeResult(0x12345678));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test Canon.Core.Tests --filter "FullyQualifiedName~Events_are_named_after_their_constant|FullyQualifiedName~Results_are_described_with_their_code"`
Expected: FAIL to compile, `EdsdkHelper` has no `DescribeEvent` / `DescribeResult`.

- [ ] **Step 3: Implement the helpers**

At the top of `Canon.Core/EdsdkHelper.cs` (before `namespace Canon.Core;`), add `using System.Reflection;`. After `GetErrorMessage`, add:

```csharp
    /// <summary>
    /// "OK", the error message followed by its code, or the code alone when unknown.
    /// </summary>
    public static string DescribeResult(uint errorCode) =>
        errorCode == EDSDK.EDS_ERR_OK ? "OK"
        : ErrorMessages.TryGetValue(errorCode, out var message) ? $"{message} ({FormatRawValue(errorCode)})"
        : FormatRawValue(errorCode);

    // Property, object and state event constants of EDSDK, by value ("*_All" are subscription masks, not events).
    private static readonly Dictionary<uint, string> EventNames = typeof(EDSDK)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(uint) && !field.Name.EndsWith("_All")
            && (field.Name.StartsWith("PropertyEvent_") || field.Name.StartsWith("ObjectEvent_") || field.Name.StartsWith("StateEvent_")))
        .GroupBy(field => (uint)field.GetRawConstantValue()!)
        .ToDictionary(group => group.Key, group => group.First().Name);

    /// <summary>
    /// Name of an EDSDK event (e.g. "StateEvent_Shutdown"), or its code when unknown.
    /// </summary>
    public static string DescribeEvent(uint eventId) =>
        EventNames.TryGetValue(eventId, out var name) ? name : FormatRawValue(eventId);
```

(`FormatRawValue` already exists in `EdsdkHelper`: `$"0x{value:X}"`.)

- [ ] **Step 4: Run the tests**

Run: `dotnet test CanonSDK.sln`
Expected: `Failed: 0`, the 6 new test cases pass.

- [ ] **Step 5: Commit**

```bash
git add Canon.Core/EdsdkHelper.cs Canon.Core.Tests/EdsdkHelperTests.cs
git commit -m "Name EDSDK events and results for the logs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Debug logs of EDSDK events and capture steps

**Files:**
- Modify: `Canon.Core/CanonCamera.cs`
- Modify: `README.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: `EdsdkHelper.DescribeEvent(uint)`, `EdsdkHelper.DescribeResult(uint)` (Task 3).
- Produces: `private uint TraceCall(string step, Func<uint> call)` in `CanonCamera`; Debug logs under the `Canon.Core.CanonCamera` category, enabled with `--Logging:LogLevel:Canon.Core=Debug`.

These calls need the native SDK and a camera: they are checked by running the API (Step 9) and in Task 5, not by unit tests. Logging only: no behavior change, apart from a warning when the UI unlock fails.

- [ ] **Step 1: The TraceCall helper**

In `Canon.Core/CanonCamera.cs`, add `using System.Diagnostics;` as the first using. Before the `#endregion` that closes the `Connection` region (after the `RunWithBusyRetryAsync(Action<nint> ...)` overload), add:

```csharp
    /// <summary>
    /// Runs an SDK call and logs its result and duration at Debug level.
    /// </summary>
    private uint TraceCall(string step, Func<uint> call)
    {
        if (_logger is not { } logger || !logger.IsEnabled(LogLevel.Debug))
            return call();

        var start = Stopwatch.GetTimestamp();
        var result = call();
        logger.LogDebug("{Step}: {Result} ({Duration:0} ms)", step, EdsdkHelper.DescribeResult(result), Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return result;
    }
```

- [ ] **Step 2: Log every SDK event**

In `OnCameraPropertyChanged`, before `if (inEvent != EDSDK.PropertyEvent_PropertyChanged || PropertyChanged == null)`, add:

```csharp
        _logger?.LogDebug("SDK event {Event}: property 0x{Property:X}, parameter 0x{Parameter:X}", EdsdkHelper.DescribeEvent(inEvent), inPropertyId, inParam);

```

In `OnCameraStateChanged`, before `switch (inEvent)`, add:

```csharp
        _logger?.LogDebug("SDK event {Event}: parameter 0x{Parameter:X}", EdsdkHelper.DescribeEvent(inEvent), inParameter);

```

In `OnCameraObject`, before `try`, add:

```csharp
        _logger?.LogDebug("SDK event {Event}", EdsdkHelper.DescribeEvent(inEvent));

```

- [ ] **Step 3: Trace the flash settings**

In `SetFlashFiringCore`, replace:

```csharp
            EDSDK.EdsSendStatusCommand(camera, EDSDK.CameraState_UILock, 1).ThrowIfEdSdkError("Could not lock the camera UI");

            try
            {
                EDSDK.EdsSetPropertyData(_flashRef, EDSDK.PropID_Flash_Target, 0, sizeof(uint), target)
                    .ThrowIfEdSdkError("Could not set the flash target");
                EDSDK.EdsSetPropertyData(_flashRef, EDSDK.PropID_Flash_Firing, 0, sizeof(uint), firing ? 1u : 0u)
                    .ThrowIfEdSdkError("Could not set flash firing (the camera must be in P, Tv, Av or M)");
            }
            finally
            {
                EDSDK.EdsSendStatusCommand(camera, EDSDK.CameraState_UIUnLock, 0);
            }
```

with:

```csharp
            TraceCall("UI lock", () => EDSDK.EdsSendStatusCommand(camera, EDSDK.CameraState_UILock, 1)).ThrowIfEdSdkError("Could not lock the camera UI");

            try
            {
                TraceCall("Flash target", () => EDSDK.EdsSetPropertyData(_flashRef, EDSDK.PropID_Flash_Target, 0, sizeof(uint), target))
                    .ThrowIfEdSdkError("Could not set the flash target");
                TraceCall("Flash firing", () => EDSDK.EdsSetPropertyData(_flashRef, EDSDK.PropID_Flash_Firing, 0, sizeof(uint), firing ? 1u : 0u))
                    .ThrowIfEdSdkError("Could not set flash firing (the camera must be in P, Tv, Av or M)");
            }
            finally
            {
                var unlock = TraceCall("UI unlock", () => EDSDK.EdsSendStatusCommand(camera, EDSDK.CameraState_UIUnLock, 0));
                if (unlock != EDSDK.EDS_ERR_OK)
                    _logger?.LogWarning("Could not unlock the camera UI: {Result}", EdsdkHelper.DescribeResult(unlock));
            }
```

- [ ] **Step 4: Trace the shutter button**

In `TakePictureAsync`, after the `acceptedTypes` lines (`if (acceptedTypes.Count == 0) acceptedTypes = _defaultFileTypes;`), add:

```csharp
        var clock = Stopwatch.StartNew();
        _logger?.LogDebug("Capture requested (auto focus: {AutoFocus}, file types: {FileTypes})", useAutoFocus, string.Join(", ", acceptedTypes));
```

Replace the press:

```csharp
                    await RunWithBusyRetryAsync(camera =>
                        EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)button)
                            .ThrowIfEdSdkError("Could not press the shutter button"), cancellationToken);
```

with:

```csharp
                    await RunWithBusyRetryAsync(camera =>
                        TraceCall($"Press shutter button ({button})", () => EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)button))
                            .ThrowIfEdSdkError("Could not press the shutter button"), cancellationToken);
```

Replace:

```csharp
                return await pending.Completion.Task.WaitAsync(timeout, cancellationToken);
```

with:

```csharp
                _logger?.LogDebug("Waiting up to {Timeout} s for the file", timeout.TotalSeconds);
                var image = await pending.Completion.Task.WaitAsync(timeout, cancellationToken);
                _logger?.LogDebug("Capture completed in {Duration} ms: {File}", clock.ElapsedMilliseconds, image.FileName);
                return image;
```

In `ReleaseShutterButtonAsync`, replace:

```csharp
                await RunAsync(camera =>
                    EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF)
                        .ThrowIfEdSdkError("Could not release the shutter button"));
```

with:

```csharp
                await RunAsync(camera =>
                    TraceCall("Release shutter button", () => EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF))
                        .ThrowIfEdSdkError("Could not release the shutter button"));
```

In `AutoFocus`, replace:

```csharp
            await RunWithBusyRetryAsync(camera =>
                EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Halfway)
                    .ThrowIfEdSdkError("Could not send AF command"));
```

with:

```csharp
            await RunWithBusyRetryAsync(camera =>
                TraceCall("Press shutter button halfway", () => EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Halfway))
                    .ThrowIfEdSdkError("Could not send AF command"));
```

- [ ] **Step 5: Trace the transfer**

In `TransferFile`, after the `EdsGetDirectoryItemInfo` error block, add:

```csharp
            _logger?.LogDebug("Transfer requested: {File} ({Size} bytes), pending capture: {Pending}", info.szFileName, info.Size, pending != null);

```

and in the "file type not requested" block, replace `EDSDK.EdsDownloadCancel(dirItem);` with:

```csharp
                TraceCall("Download cancel", () => EDSDK.EdsDownloadCancel(dirItem));
```

In `DownloadToMemory`, replace:

```csharp
            err = EDSDK.EdsDownload(dirItem, info.Size, stream);
```

with:

```csharp
            err = TraceCall("Download", () => EDSDK.EdsDownload(dirItem, info.Size, stream));
```

and replace:

```csharp
            EDSDK.EdsDownloadComplete(dirItem).ThrowIfEdSdkError("Failed to complete download");
```

with:

```csharp
            TraceCall("Download complete", () => EDSDK.EdsDownloadComplete(dirItem)).ThrowIfEdSdkError("Failed to complete download");
```

- [ ] **Step 6: Build and run the tests**

Run: `dotnet build CanonSDK.sln && dotnet test CanonSDK.sln`
Expected: 0 warnings, 0 errors, `Failed: 0`.

- [ ] **Step 7: Documentation**

`README.md`:
- In "Running on Linux", after the `dotnet run` command line, add inside the same code block:

```bash
# Every EDSDK event and each step of a capture (result and duration) in the logs:
dotnet run --project Canon.API -f net10.0 --launch-profile http -- --Logging:LogLevel:Canon.Core=Debug
```

- Configuration table, `Logging:LogLevel` row description: append ` \`Canon.Core\`: \`Debug\` logs every EDSDK event and each step of a capture (UI lock, flash settings, shutter button, transfer) with its result and duration.`

`CHANGELOG.md`, under `## [Unreleased]`:
- In `### Added`: `- Debug logs (\`Logging:LogLevel:Canon.Core\` = \`Debug\`): every EDSDK event, and each step of a capture (UI lock, flash settings, shutter button, transfer) with its result and duration.`
- In `### Changed`: `- A failed UI unlock after setting the flash is logged as a warning; it was ignored.`

- [ ] **Step 8: Commit**

```bash
git add Canon.Core/CanonCamera.cs README.md CHANGELOG.md
git commit -m "Log EDSDK events and capture steps at Debug level

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 9: Smoke check with the camera**

Run in the background: `dotnet run --project Canon.API -f net10.0 --launch-profile http -- --Logging:LogLevel:Canon.Core=Debug`
The camera connects at startup. Expected in the log: `Connected to camera Canon EOS R100`, `SDK event ...` lines and, when the camera supports the flash settings, `UI lock: OK (... ms)`, `Flash target: ...`, `Flash firing: ...`, `UI unlock: ...`. Stop the API.

---

### Task 5: Reproduce the photo booth capture on Linux (manual, with the user)

**Files:** none (findings go to the `r100-capture-disconnect` work state and to the user).

**Interfaces:**
- Consumes: the Linux API with Debug logs (Tasks 2 and 4).

- [ ] **Step 1: Prepare the camera like the photo booth**

Ask the user to set the R100 as in the photo booth (shooting mode, flash on the shoe, no memory card if possible, same power supply if available), to plug it into this computer, and to watch the camera screen during the test.

- [ ] **Step 2: Make sure nothing else holds the camera**

```bash
lsusb -d 04a9:                     # Canon device present
lsusb -t | grep -i imaging         # Driver=[none]: no other program holds it
gio mount -l | grep -i -A2 canon   # nothing: not mounted by the desktop
```

If the desktop mounted it: `gio mount -u <location shown by gio mount -l>`.

- [ ] **Step 3: Run the photo booth sequence**

```bash
S=$(mktemp -d)
dotnet run --project Canon.API -f net10.0 --launch-profile http -- --Logging:LogLevel:Canon.Core=Debug   # background
curl -s http://localhost:5159/status
curl -s -N -o /dev/null --max-time 120 http://localhost:5159/videostream &   # the photo booth live view
sleep 5
curl -s -X POST 'http://localhost:5159/takepicture' -o "$S/capture-1.jpg" -D - -w '%{http_code} %{time_total}s\n'
```

Expected when it works: `200`, `X-File-Name` header, a JPEG in `$S/capture-1.jpg` (`file` says JPEG).

- [ ] **Step 4: Read the log**

```bash
grep -n -E 'SDK event|UI lock|UI unlock|Flash|shutter|Transfer|Download|Capture|disconnected|error|Warn' Canon.API/bin/Debug/net10.0/logs/canon-api*.log | tail -80
```

Write down the last step that succeeded, the first failure (result code), the events around it and what the user saw on the camera.

- [ ] **Step 5: Change one variable at a time**

If the capture failed, restart the API and the stream before each run:
1. `-- --Logging:LogLevel:Canon.Core=Debug --Canon:ForceFlashFiring=false` (no flash setting before the capture).
2. Flash setting back, `POST /takepicture?useAutoFocus=false` (no autofocus).
3. Without `/videostream` (no live view).

If the capture works, repeat Step 3 ten times to check it does not fail intermittently.

- [ ] **Step 6: Report**

Record the results in the `r100-capture-disconnect` work state and give the user the root cause with the log lines that prove it. The fix follows superpowers:systematic-debugging Phase 4 (failing test when possible, one change), in a separate commit.

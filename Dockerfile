# escape=`

# Windows container image to build, test and publish CanonWebAPI.
# Requires Docker in "Windows containers" mode (Docker Desktop on Windows 10/11, or Windows Server).
#
#   docker build --target test -t canonwebapi:test .   # build + unit tests only
#   docker build -t canonwebapi .                       # build + tests + runtime image
#
# Windows containers cannot access USB devices: the camera itself is not reachable from a container.
# The runtime image is meant for smoke tests (API, OpenAPI document, error handling) and for producing the release files.

ARG WINDOWS_VERSION=ltsc2022

# ---------------------------------------------------------------------------
# Build and unit tests
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-${WINDOWS_VERSION} AS build
WORKDIR C:\src

# Restore first so the package layer is cached when only the code changes.
COPY Directory.Build.props ./
COPY Canon.Core/Canon.Core.csproj Canon.Core/
COPY Canon.Core.Tests/Canon.Core.Tests.csproj Canon.Core.Tests/
COPY Canon.API.Tests/Canon.API.Tests.csproj Canon.API.Tests/
COPY Canon.API/Canon.API.csproj Canon.API/
RUN dotnet restore Canon.Core.Tests/Canon.Core.Tests.csproj && `
    dotnet restore Canon.API.Tests/Canon.API.Tests.csproj && `
    dotnet restore Canon.API/Canon.API.csproj -r win-x64

COPY EDSDK/ EDSDK/
COPY Canon.Core/ Canon.Core/
COPY Canon.Core.Tests/ Canon.Core.Tests/
COPY Canon.API.Tests/ Canon.API.Tests/
COPY Canon.API/ Canon.API/

RUN dotnet build Canon.Core.Tests/Canon.Core.Tests.csproj -c Release --no-restore && `
    dotnet build Canon.API.Tests/Canon.API.Tests.csproj -c Release --no-restore

FROM build AS test
RUN dotnet test Canon.Core.Tests/Canon.Core.Tests.csproj -c Release --no-build `
    --logger "trx;LogFileName=core.trx" --results-directory C:\testresults && `
    dotnet test Canon.API.Tests/Canon.API.Tests.csproj -c Release --no-build `
    --logger "trx;LogFileName=api.trx" --results-directory C:\testresults

# ---------------------------------------------------------------------------
# Publish: same self-contained single-file executable as the GitHub release
# ---------------------------------------------------------------------------
FROM test AS publish
RUN dotnet publish Canon.API/Canon.API.csproj -c Release -f net10.0-windows -r win-x64 --self-contained true `
    -p:PublishSingleFile=true --no-restore -o C:\publish

# ---------------------------------------------------------------------------
# Runtime (smoke tests): http://localhost:5159/status, /swagger, /openapi/v1.json
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/windows/servercore:${WINDOWS_VERSION} AS runtime
WORKDIR C:\app
COPY --from=publish C:\publish\ .

# No update check in a container, listen on every interface of the container.
ENV AutoUpdate__Enabled=false `
    ASPNETCORE_URLS=http://+:5159

EXPOSE 5159
ENTRYPOINT ["C:\\app\\Canon.API.exe"]

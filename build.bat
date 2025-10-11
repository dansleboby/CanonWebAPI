@echo off
echo Building Canon.API for production...

rem Clean previous builds
echo Cleaning solution...
dotnet clean CanonSDK.sln --configuration Release

rem Restore packages
echo Restoring packages...
dotnet restore CanonSDK.sln

rem Build Canon.API in Release configuration
echo Building Canon.API...
dotnet build Canon.API --configuration Release --no-restore

rem Publish Canon.API for deployment as standalone executable
echo Publishing Canon.API as standalone executable...
dotnet publish Canon.API/Canon.API.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ./publish --verbosity minimal

echo.
echo Build completed successfully!
echo Published files are in ./publish folder
echo.
pause
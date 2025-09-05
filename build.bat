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

rem Publish Canon.API for deployment
echo Publishing Canon.API...
dotnet publish Canon.API --configuration Release --output ./publish --no-build --verbosity minimal

echo.
echo Build completed successfully!
echo Published files are in ./publish folder
echo.
pause
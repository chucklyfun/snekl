@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "PROJECT=%ROOT%src\Snekl.Server\Snekl.Server.csproj"
set "EXE=%ROOT%src\Snekl.Server\bin\Debug\net8.0\snekl.exe"

set "CMD=%~1"
if /I "%CMD%"=="stop" goto :stop
if /I "%CMD%"=="status" goto :nobuild
if /I "%CMD%"=="help" goto :nobuild
if /I "%CMD%"=="--help" goto :nobuild
if /I "%CMD%"=="-h" goto :nobuild
if /I "%CMD%"=="pack" goto :pack
if /I "%CMD%"=="android" goto :pack
goto :build

:stop
rem Older snekl.exe only kills when --force is present; new builds ignore the flag.
if exist "%EXE%" (
  "%EXE%" stop --force
  exit /b %ERRORLEVEL%
)
dotnet run --project "%PROJECT%" --no-launch-profile --no-build -- stop --force
exit /b %ERRORLEVEL%

:nobuild
if exist "%EXE%" (
  "%EXE%" %*
  exit /b %ERRORLEVEL%
)
dotnet run --project "%PROJECT%" --no-launch-profile --no-build -- %*
exit /b %ERRORLEVEL%

:pack
set "SIDE=%ROOT%.snekl\cli"
if not exist "%SIDE%" mkdir "%SIDE%"
dotnet build "%PROJECT%" -c Release -o "%SIDE%" --nologo
if errorlevel 1 exit /b %ERRORLEVEL%
"%SIDE%\snekl.exe" %*
exit /b %ERRORLEVEL%

:build
tasklist /FI "IMAGENAME eq snekl.exe" /NH 2>nul | findstr /I /C:"snekl.exe" >nul
if not errorlevel 1 (
  echo snekl.exe is still running and locks the Debug build output.
  echo Stop it first:  %~dp0snekl stop
  exit /b 1
)
dotnet run --project "%PROJECT%" --no-launch-profile -- %*
exit /b %ERRORLEVEL%

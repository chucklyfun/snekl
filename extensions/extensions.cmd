@echo off
setlocal
set "ROOT=%~dp0"
set "ASPNETCORE_ENVIRONMENT=Development"
set "DOTNET_URLS=http://0.0.0.0:5301"
dotnet run --project "%ROOT%commissions\Snekl.Commissions\Snekl.Commissions.csproj" --no-launch-profile %*

[CmdletBinding()]
param(
    [string]$MonoGamePath = "D:\SteamLibrary\steamapps\common\Schedule I_alternate",
    [string]$Il2CppGamePath = "D:\SteamLibrary\steamapps\common\Schedule I_public"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

dotnet run --project (Join-Path $PSScriptRoot "S1AntiCheat.ContractVerifier\S1AntiCheat.ContractVerifier.csproj") -c Release
if ($LASTEXITCODE -ne 0) {
    throw "The contract verifier failed."
}

dotnet build (Join-Path $projectRoot "S1AntiCheat.csproj") -c Mono
if ($LASTEXITCODE -ne 0) {
    throw "The Mono build failed."
}

dotnet build (Join-Path $projectRoot "S1AntiCheat.csproj") -c Il2cpp
if ($LASTEXITCODE -ne 0) {
    throw "The IL2CPP build failed."
}

& (Join-Path $PSScriptRoot "Verify-GameSurface.ps1") -MonoGamePath $MonoGamePath -Il2CppGamePath $Il2CppGamePath

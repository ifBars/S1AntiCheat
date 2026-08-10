[CmdletBinding()]
param(
    [ValidateSet("Mono", "Il2cpp")]
    [string]$Runtime = "Il2cpp",

    [ValidateSet("Clean", "Risky", "Ownership")]
    [string]$Scenario = "Clean",

    [string]$GamePath = "",

    [string]$GseSteamApiPath = "",

    [string]$RiskyModPath = "",

    [string]$InstanceRoot = "",

    [string]$EvidenceRoot = "",

    [string]$HostSteamId = "76561198000000421",

    [string]$ClientSteamId = "76561198000000422",

    [ValidateRange(60, 300)]
    [int]$TimeoutSeconds = 180,

    [switch]$KeepInstances,

    [switch]$KeepEvidence
)

$ErrorActionPreference = "Stop"
$runStartedUtc = [DateTime]::UtcNow

function Assert-Path {
    param([string]$Path, [string]$Description)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function New-FileLinkOrCopy {
    param([string]$SourcePath, [string]$DestinationPath)

    try {
        New-Item -ItemType HardLink -Path $DestinationPath -Target $SourcePath -Force | Out-Null
    }
    catch {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
    }
}

function Copy-IsolatedGame {
    param([string]$SourcePath, [string]$DestinationPath)

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $SourcePath -Force) {
        $destination = Join-Path $DestinationPath $item.Name
        if (-not $item.PSIsContainer) {
            New-FileLinkOrCopy -SourcePath $item.FullName -DestinationPath $destination
            continue
        }

        if ($item.Name -eq "Schedule I_Data") {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            foreach ($dataItem in Get-ChildItem -LiteralPath $item.FullName -Force) {
                $dataDestination = Join-Path $destination $dataItem.Name
                if ($dataItem.PSIsContainer) {
                    if ($dataItem.Name -eq "Plugins") {
                        Copy-Item -LiteralPath $dataItem.FullName -Destination $dataDestination -Recurse -Force
                    }
                    else {
                        New-Item -ItemType Junction -Path $dataDestination -Target $dataItem.FullName | Out-Null
                    }
                }
                else {
                    New-FileLinkOrCopy -SourcePath $dataItem.FullName -DestinationPath $dataDestination
                }
            }
            continue
        }

        if ($item.Name -eq "MelonLoader") {
            Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
            continue
        }

        if ($item.Name -in @("Mods", "UserLibs", "UserData")) {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            continue
        }

        New-Item -ItemType Junction -Path $destination -Target $item.FullName | Out-Null
    }

    foreach ($requiredDirectory in @("Mods", "UserLibs", "UserData")) {
        New-Item -ItemType Directory -Path (Join-Path $DestinationPath $requiredDirectory) -Force | Out-Null
    }
}

function Set-GseIdentity {
    param(
        [string]$InstancePath,
        [string]$SteamId,
        [string]$AccountName,
        [string]$SteamApiSource
    )

    $pluginDirectory = Join-Path $InstancePath "Schedule I_Data\Plugins\x86_64"
    Copy-Item -LiteralPath $SteamApiSource -Destination (Join-Path $pluginDirectory "steam_api64.dll") -Force
    $settingsDirectory = Join-Path $pluginDirectory "steam_settings"
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $config = @(
        "[user::general]",
        "account_name=$AccountName",
        "account_steamid=$SteamId",
        "language=english"
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText((Join-Path $settingsDirectory "configs.user.ini"), $config)
}

function Assert-InstallManifest {
    param(
        [string]$InstancePath,
        [string[]]$ExpectedMods,
        [string[]]$ExpectedUserLibs,
        [string]$Role
    )

    $actualMods = @(Get-ChildItem -LiteralPath (Join-Path $InstancePath "Mods") -File |
        Select-Object -ExpandProperty Name | Sort-Object)
    $actualUserLibs = @(Get-ChildItem -LiteralPath (Join-Path $InstancePath "UserLibs") -File |
        Select-Object -ExpandProperty Name | Sort-Object)
    $expectedModSet = @($ExpectedMods | Sort-Object)
    $expectedLibrarySet = @($ExpectedUserLibs | Sort-Object)
    if (Compare-Object $expectedModSet $actualMods) {
        throw "$Role Mods mismatch. Expected: $($expectedModSet -join ', '). Actual: $($actualMods -join ', ')."
    }
    if (Compare-Object $expectedLibrarySet $actualUserLibs) {
        throw "$Role UserLibs mismatch. Expected: $($expectedLibrarySet -join ', '). Actual: $($actualUserLibs -join ', ')."
    }
}

function Wait-ForFile {
    param(
        [string]$Path,
        [datetime]$Deadline,
        [System.Diagnostics.Process]$Owner,
        [string]$Phase
    )

    $nextStatus = [datetime]::MinValue
    while ((Get-Date) -lt $Deadline) {
        if (Test-Path -LiteralPath $Path) {
            return
        }
        if ($Owner.HasExited) {
            throw "$Phase failed because process $($Owner.Id) exited with code $($Owner.ExitCode)."
        }
        if ((Get-Date) -ge $nextStatus) {
            Write-Host "${Phase}: waiting for $(Split-Path -Leaf $Path)" -ForegroundColor DarkGray
            $nextStatus = (Get-Date).AddSeconds(5)
        }
        Start-Sleep -Milliseconds 500
    }

    throw "$Phase timed out waiting for $Path"
}

function Wait-ForLogMatch {
    param(
        [string]$Path,
        [string]$Pattern,
        [datetime]$Deadline,
        [System.Diagnostics.Process]$Owner,
        [string]$Phase
    )

    $nextStatus = [datetime]::MinValue
    while ((Get-Date) -lt $Deadline) {
        if ((Test-Path -LiteralPath $Path) -and
            (Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue) -match $Pattern) {
            return
        }
        if ($Owner.HasExited) {
            throw "$Phase failed because process $($Owner.Id) exited with code $($Owner.ExitCode)."
        }
        if ((Get-Date) -ge $nextStatus) {
            Write-Host "${Phase}: waiting for host rejection marker" -ForegroundColor DarkGray
            $nextStatus = (Get-Date).AddSeconds(5)
        }
        Start-Sleep -Milliseconds 500
    }

    throw "$Phase timed out waiting for pattern $Pattern in $Path"
}

function Stop-LaunchedProcess {
    param([System.Diagnostics.Process]$Process, [string]$Role)

    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped $Role process $($Process.Id)." -ForegroundColor DarkGray
    }
}

function Remove-IsolatedRoot {
    param([string]$Path, [string]$AllowedRoot)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedAllowedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedAllowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove isolated path outside the instance root: $resolvedPath"
    }

    Get-ChildItem -LiteralPath $resolvedPath -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        ForEach-Object {
            if ($_.PSIsContainer) {
                [System.IO.Directory]::Delete($_.FullName, $false)
            }
            else {
                Remove-Item -LiteralPath $_.FullName -Force
            }
        }
    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

function Copy-EvidenceFile {
    param([string]$Source, [string]$Destination)

    if (Test-Path -LiteralPath $Source) {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($GamePath)) {
    $GamePath = if ($Runtime -eq "Mono") {
        "D:\SteamLibrary\steamapps\common\Schedule I_alternate"
    }
    else {
        "D:\SteamLibrary\steamapps\common\Schedule I_public"
    }
}
if ([string]::IsNullOrWhiteSpace($GseSteamApiPath)) {
    $GseSteamApiPath = Join-Path $repoRoot "artifacts\gse-cache\extracted\release\regular\x64\steam_api64.dll"
}
if ([string]::IsNullOrWhiteSpace($RiskyModPath)) {
    $RiskyModPath = Join-Path $repoRoot "..\ModMenus\Modern_Cheat_Menu.dll"
}

$resolvedGamePath = [System.IO.Path]::GetFullPath($GamePath)
$resolvedGsePath = [System.IO.Path]::GetFullPath($GseSteamApiPath)
$resolvedRiskyModPath = [System.IO.Path]::GetFullPath($RiskyModPath)
if ([string]::IsNullOrWhiteSpace($InstanceRoot)) {
    $InstanceRoot = Join-Path (Split-Path -Parent $resolvedGamePath) "S1AntiCheat.SmokeInstances"
}
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repoRoot "artifacts\p2p-smoke"
}
$resolvedInstanceRoot = [System.IO.Path]::GetFullPath($InstanceRoot)
$resolvedEvidenceRoot = [System.IO.Path]::GetFullPath($EvidenceRoot)

Assert-Path (Join-Path $resolvedGamePath "Schedule I.exe") "Schedule I executable"
Assert-Path $resolvedGsePath "GSE steam_api64.dll"
Assert-Path (Join-Path $resolvedGamePath "Schedule I_Data\StreamingAssets\DefaultSave") "Default save fixture"
if ($Runtime -eq "Mono") {
    Assert-Path (Join-Path $resolvedGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll") "Mono Assembly-CSharp"
}
else {
    Assert-Path (Join-Path $resolvedGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll") "IL2CPP interop Assembly-CSharp"
}
if ($Scenario -eq "Risky") {
    if ($Runtime -ne "Il2cpp") {
        throw "The supplied risky menu sample is IL2CPP-only. Run Scenario=Risky with Runtime=Il2cpp."
    }
    Assert-Path $resolvedRiskyModPath "Risky mod sample"
}
if ($HostSteamId -eq $ClientSteamId) {
    throw "HostSteamId and ClientSteamId must be different."
}
$gseVersion = (Get-Item -LiteralPath $resolvedGsePath).VersionInfo
if ($gseVersion.CompanyName -ne "GSE") {
    throw "The supplied Steam API is not identified as GSE: $resolvedGsePath"
}
if (@(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "Schedule I.exe" }).Count -gt 0) {
    throw "Refusing to run while an existing Schedule I process is active."
}

$gamePrefix = $resolvedGamePath.TrimEnd('\') + '\'
$instancePrefix = $resolvedInstanceRoot.TrimEnd('\') + '\'
if ($resolvedInstanceRoot.StartsWith($gamePrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    $resolvedGamePath.StartsWith($instancePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The game and isolated instance roots must not contain one another."
}

$runId = "{0}-{1}-{2}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $Runtime.ToLowerInvariant(), $Scenario.ToLowerInvariant()
$runRoot = Join-Path $resolvedInstanceRoot $runId
$hostPath = Join-Path $runRoot "host"
$clientPath = Join-Path $runRoot "client"
$sharedPath = Join-Path $runRoot "shared"
$evidencePath = Join-Path $resolvedEvidenceRoot $runId
$hostProcess = $null
$clientProcess = $null
$passed = $false

try {
    New-Item -ItemType Directory -Path $sharedPath -Force | Out-Null
    New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null

    Write-Host "Building S1 Anti-Cheat and the $Runtime P2P probe..." -ForegroundColor Cyan
    & dotnet build (Join-Path $repoRoot "S1AntiCheat.csproj") -c $Runtime -p:AutomateLocalDeployment=false
    if ($LASTEXITCODE -ne 0) { throw "S1 Anti-Cheat $Runtime build failed." }
    & dotnet build (Join-Path $scriptRoot "S1AntiCheat.P2PSmoke\S1AntiCheat.P2PSmoke.csproj") -c $Runtime -p:GamePath=$resolvedGamePath
    if ($LASTEXITCODE -ne 0) { throw "P2P probe $Runtime build failed." }

    $framework = if ($Runtime -eq "Mono") { "netstandard2.1" } else { "net6.0" }
    $runtimeFileName = if ($Runtime -eq "Mono") { "S1AntiCheat_Mono.dll" } else { "S1AntiCheat_Il2Cpp.dll" }
    $runtimeDll = Join-Path $repoRoot "bin\$Runtime\$framework\$runtimeFileName"
    $apiDll = Join-Path $repoRoot "S1AntiCheat.API\bin\$Runtime\netstandard2.1\S1AntiCheat.API.dll"
    $probeDll = Join-Path $scriptRoot "S1AntiCheat.P2PSmoke\bin\$Runtime\$framework\S1AntiCheat.P2PSmoke.dll"
    Assert-Path $runtimeDll "S1 Anti-Cheat runtime"
    Assert-Path $apiDll "S1 Anti-Cheat API"
    Assert-Path $probeDll "P2P probe"

    Write-Host "Preparing isolated host and client installs..." -ForegroundColor Cyan
    Copy-IsolatedGame -SourcePath $resolvedGamePath -DestinationPath $hostPath
    Copy-IsolatedGame -SourcePath $resolvedGamePath -DestinationPath $clientPath
    Set-GseIdentity -InstancePath $hostPath -SteamId $HostSteamId -AccountName "S1AC-Host" -SteamApiSource $resolvedGsePath
    Set-GseIdentity -InstancePath $clientPath -SteamId $ClientSteamId -AccountName "S1AC-Client" -SteamApiSource $resolvedGsePath

    foreach ($instance in @($hostPath, $clientPath)) {
        Copy-Item -LiteralPath $runtimeDll -Destination (Join-Path $instance "Mods\$runtimeFileName") -Force
        Copy-Item -LiteralPath $probeDll -Destination (Join-Path $instance "Mods\S1AntiCheat.P2PSmoke.dll") -Force
        Copy-Item -LiteralPath $apiDll -Destination (Join-Path $instance "UserLibs\S1AntiCheat.API.dll") -Force
    }
    if ($Scenario -eq "Risky") {
        Copy-Item -LiteralPath $resolvedRiskyModPath -Destination (Join-Path $clientPath ("Mods\" + (Split-Path -Leaf $resolvedRiskyModPath))) -Force
    }

    $preferences = @(
        "[S1AntiCheat]",
        "EnableAdmissionGate = true",
        "TrustSteamFriendsInLobby = false",
        "TrustAllCurrentLobbyMembers = true",
        "RequireClientAntiCheat = true",
        "VerificationMode = `"BlockKnownRisky`"",
        "VerificationTimeoutSeconds = 20",
        "DisconnectOnExploitAttempt = true",
        "EnableRpcOwnershipGuards = true",
        "EnableClientMutationGuards = true"
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText((Join-Path $hostPath "UserData\MelonPreferences.cfg"), $preferences)
    [System.IO.File]::WriteAllText((Join-Path $clientPath "UserData\MelonPreferences.cfg"), $preferences)

    $expectedHostMods = @($runtimeFileName, "S1AntiCheat.P2PSmoke.dll")
    $expectedClientMods = @($expectedHostMods)
    if ($Scenario -eq "Risky") { $expectedClientMods += (Split-Path -Leaf $resolvedRiskyModPath) }
    Assert-InstallManifest -InstancePath $hostPath -ExpectedMods $expectedHostMods -ExpectedUserLibs @("S1AntiCheat.API.dll") -Role "Host"
    Assert-InstallManifest -InstancePath $clientPath -ExpectedMods $expectedClientMods -ExpectedUserLibs @("S1AntiCheat.API.dll") -Role "Client"

    Copy-Item -LiteralPath (Join-Path $resolvedGamePath "Schedule I_Data\StreamingAssets\DefaultSave") `
        -Destination (Join-Path $sharedPath "host-save") -Recurse -Force
    $gameAssemblyPath = if ($Runtime -eq "Mono") {
        Join-Path $resolvedGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
    }
    else {
        Join-Path $resolvedGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
    }
    $manifest = @(
        "runId=$runId",
        "runtime=$Runtime",
        "scenario=$Scenario",
        "gamePath=$resolvedGamePath",
        "gameAssemblySha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $gameAssemblyPath).Hash)",
        "gseSha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedGsePath).Hash)",
        "gseVersion=$($gseVersion.FileVersion)",
        "hostSteamId=$HostSteamId",
        "clientSteamId=$ClientSteamId",
        "startedUtc=$($runStartedUtc.ToString('O'))"
    )
    [System.IO.File]::WriteAllLines((Join-Path $sharedPath "manifest.txt"), $manifest)

    $hostPlayerLog = Join-Path $sharedPath "host-player.log"
    $clientPlayerLog = Join-Path $sharedPath "client-player.log"
    $commonArguments = @("--s1ac-smoke-scenario", $Scenario.ToLowerInvariant(), "--s1ac-smoke-timeout", $TimeoutSeconds.ToString())
    $hostArguments = @(
        "--s1ac-smoke-role", "host", "--s1ac-smoke-dir", "`"$sharedPath`"", "--s1ac-smoke-peer", $ClientSteamId,
        "-logFile", "`"$hostPlayerLog`""
    ) + $commonArguments
    $clientArguments = @(
        "--s1ac-smoke-role", "client", "--s1ac-smoke-dir", "`"$sharedPath`"", "--s1ac-smoke-peer", $HostSteamId,
        "-logFile", "`"$clientPlayerLog`""
    ) + $commonArguments

    Write-Host "Launching host..." -ForegroundColor Cyan
    $hostProcess = Start-Process -FilePath (Join-Path $hostPath "Schedule I.exe") -ArgumentList $hostArguments `
        -WorkingDirectory $hostPath -PassThru -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds + 30)
    Wait-ForFile -Path (Join-Path $sharedPath "lobby-ready.txt") -Deadline $deadline -Owner $hostProcess -Phase "host-lobby"

    Write-Host "Launching client..." -ForegroundColor Cyan
    $clientProcess = Start-Process -FilePath (Join-Path $clientPath "Schedule I.exe") -ArgumentList $clientArguments `
        -WorkingDirectory $clientPath -PassThru -WindowStyle Hidden
    Wait-ForFile -Path (Join-Path $sharedPath "client-joined.txt") -Deadline $deadline -Owner $clientProcess -Phase "client-join"
    if ($Scenario -eq "Risky") {
        $rejectionPattern = "Rejected SteamID $ClientSteamId .*Known mod-menu build reported"
        Wait-ForLogMatch -Path (Join-Path $hostPath "MelonLoader\Latest.log") -Pattern $rejectionPattern `
            -Deadline $deadline -Owner $hostProcess -Phase "host-risky-rejection"
        $syntheticHostResult =
            "PASS|S1AntiCheat.P2P|Scenario=risky|Role=host|Outcome=denied|SteamId=$ClientSteamId|Evidence=host-log"
        [System.IO.File]::WriteAllText((Join-Path $sharedPath "result-host.txt"), $syntheticHostResult + [Environment]::NewLine)
    }
    elseif ($Scenario -eq "Ownership") {
        Wait-ForFile -Path (Join-Path $sharedPath "ownership-ready.txt") -Deadline $deadline -Owner $hostProcess -Phase "host-clean-verification"
        Wait-ForFile -Path (Join-Path $sharedPath "ownership-attack-sent.txt") -Deadline $deadline -Owner $clientProcess -Phase "client-ownership-attack"
        $ownershipPattern = "Blocked RPC ownership violation.*RpcReader___Server_SendDie_2166136261"
        Wait-ForLogMatch -Path (Join-Path $hostPath "MelonLoader\Latest.log") -Pattern $ownershipPattern `
            -Deadline $deadline -Owner $hostProcess -Phase "host-ownership-block"
        $syntheticHostResult =
            "PASS|S1AntiCheat.P2P|Scenario=ownership|Role=host|Outcome=blocked|SteamId=$ClientSteamId|Method=PlayerHealth.SendDie"
        [System.IO.File]::WriteAllText((Join-Path $sharedPath "result-host.txt"), $syntheticHostResult + [Environment]::NewLine)
    }
    else {
        Wait-ForFile -Path (Join-Path $sharedPath "result-host.txt") -Deadline $deadline -Owner $hostProcess -Phase "host-verification"
        Wait-ForFile -Path (Join-Path $sharedPath "result-client.txt") -Deadline $deadline -Owner $clientProcess -Phase "client-game-load"
    }

    $hostResult = (Get-Content -LiteralPath (Join-Path $sharedPath "result-host.txt") -Raw).Trim()
    if (-not $hostResult.StartsWith("PASS|", [System.StringComparison]::Ordinal)) {
        throw "Host probe failed: $hostResult"
    }
    if ($Scenario -eq "Clean") {
        $clientResult = (Get-Content -LiteralPath (Join-Path $sharedPath "result-client.txt") -Raw).Trim()
        if (-not $clientResult.StartsWith("PASS|", [System.StringComparison]::Ordinal)) {
            throw "Client probe failed: $clientResult"
        }
    }

    $hostMelonLog = Join-Path $hostPath "MelonLoader\Latest.log"
    $clientMelonLog = Join-Path $clientPath "MelonLoader\Latest.log"
    $hostLogText = Get-Content -LiteralPath $hostMelonLog -Raw
    $clientLogText = Get-Content -LiteralPath $clientMelonLog -Raw
    if ($hostLogText -notmatch "S1 Anti-Cheat 0\.1\.0 initialized" -or
        $clientLogText -notmatch "S1 Anti-Cheat 0\.1\.0 initialized") {
        throw "Both peers did not initialize S1 Anti-Cheat."
    }
    if ($Scenario -eq "Risky") {
        if ($clientLogText -notmatch "Modern Cheat Menu") {
            throw "The risky client did not load the supplied Modern Cheat Menu sample."
        }
        if ($hostResult -notmatch "Outcome=denied") {
            throw "The host did not deny the risky client. Result: $hostResult"
        }
        if ($hostLogText -notmatch "Rejected SteamID $ClientSteamId") {
            throw "The host log did not record the risky client rejection."
        }
    }
    elseif ($Scenario -eq "Ownership") {
        if ($hostResult -notmatch "Outcome=blocked") {
            throw "The host did not block the non-owner PlayerHealth RPC. Result: $hostResult"
        }
    }
    elseif ($hostResult -notmatch "Outcome=verified") {
        throw "The clean client did not reach verified state. Result: $hostResult"
    }

    $passed = $true
}
finally {
    New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
    foreach ($entry in @(
        @{ Source = (Join-Path $sharedPath "manifest.txt"); Name = "manifest.txt" },
        @{ Source = (Join-Path $sharedPath "events-host.txt"); Name = "events-host.txt" },
        @{ Source = (Join-Path $sharedPath "events-client.txt"); Name = "events-client.txt" },
        @{ Source = (Join-Path $sharedPath "result-host.txt"); Name = "result-host.txt" },
        @{ Source = (Join-Path $sharedPath "result-client.txt"); Name = "result-client.txt" },
        @{ Source = (Join-Path $sharedPath "host-player.log"); Name = "host-player.log" },
        @{ Source = (Join-Path $sharedPath "client-player.log"); Name = "client-player.log" },
        @{ Source = (Join-Path $hostPath "MelonLoader\Latest.log"); Name = "host-melon.log" },
        @{ Source = (Join-Path $clientPath "MelonLoader\Latest.log"); Name = "client-melon.log" }
    )) {
        Copy-EvidenceFile -Source $entry.Source -Destination (Join-Path $evidencePath $entry.Name)
    }

    Stop-LaunchedProcess -Process $clientProcess -Role "client"
    Stop-LaunchedProcess -Process $hostProcess -Role "host"

    if (-not $KeepInstances -and (Test-Path -LiteralPath $runRoot)) {
        Start-Sleep -Seconds 1
        Remove-IsolatedRoot -Path $runRoot -AllowedRoot $resolvedInstanceRoot
    }

    if (-not $passed -or $KeepEvidence) {
        Write-Host "Evidence preserved at: $evidencePath" -ForegroundColor Yellow
    }
}

if (-not $passed) {
    throw "S1 Anti-Cheat P2P smoke did not pass."
}

$hostSummary = (Get-Content -LiteralPath (Join-Path $evidencePath "result-host.txt") -Raw).Trim()
Write-Output "$hostSummary|Runtime=$Runtime|Evidence=$evidencePath"
if (-not $KeepEvidence) {
    Remove-Item -LiteralPath $evidencePath -Recurse -Force
}

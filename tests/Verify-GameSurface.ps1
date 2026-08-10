[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MonoGamePath,

    [Parameter(Mandatory = $true)]
    [string]$Il2CppGamePath
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) {
    throw "ilspycmd is required for game-surface verification."
}

$consolePatterns = @(
    "SubmitCommand",
    "AddItemToInventoryCommand",
    "ChangeCashCommand",
    "ChangeOnlineBalanceCommand",
    "SetHealth",
    "SetQuantity"
)

$targets = @(
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\FishNet.Runtime.dll"
        Type = "FishNet.Managing.Server.ServerManager"
        Patterns = @("Transport_OnRemoteConnectionState", "OnRemoteConnection")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Il2CppFishNet.Runtime.dll"
        Type = "Il2CppFishNet.Managing.Server.ServerManager"
        Patterns = @("Transport_OnRemoteConnectionState", "ClientAuthenticated")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\FishNet.Runtime.dll"
        Type = "FishNet.Object.NetworkBehaviour"
        Patterns = @("RegisterTargetRpc", "RegisterServerRpc", "SendTargetRpc", "SendServerRpc")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Il2CppFishNet.Runtime.dll"
        Type = "Il2CppFishNet.Object.NetworkBehaviour"
        Patterns = @("RegisterTargetRpc", "RegisterServerRpc", "SendTargetRpc", "SendServerRpc")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.UI.DailySummary"
        Patterns = @("void Awake()")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.UI.DailySummary"
        Patterns = @("Awake")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.Console"
        Patterns = $consolePatterns
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.Console"
        Patterns = $consolePatterns
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.Money.MoneyManager"
        Patterns = @("ChangeCashBalance", "CreateOnlineTransaction", "RpcReader___Server_CreateOnlineTransaction")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.Money.MoneyManager"
        Patterns = @("ChangeCashBalance", "CreateOnlineTransaction", "RpcReader___Server_CreateOnlineTransaction")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.PlayerScripts.Health.PlayerHealth"
        Patterns = @("SetHealth", "RecoverHealth", "Revive", "RpcReader___Server_SendDie", "RpcReader___Server_SendRevive")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth"
        Patterns = @("SetHealth", "RecoverHealth", "Revive", "RpcReader___Server_SendDie", "RpcReader___Server_SendRevive")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.PlayerScripts.Player"
        Patterns = @("RpcReader___Server_set_CameraPosition", "RpcReader___Server_RequestSavePlayer", "RpcReader___Server_SendValue", "RpcReader___Server_SendWorldSpaceDialogue")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.PlayerScripts.Player"
        Patterns = @("RpcReader___Server_set_CameraPosition", "RpcReader___Server_RequestSavePlayer", "RpcReader___Server_SendValue", "RpcReader___Server_SendWorldSpaceDialogue")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.PlayerScripts.PlayerMovement"
        Patterns = @("Teleport")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.PlayerScripts.PlayerMovement"
        Patterns = @("Teleport")
    },
    @{
        Runtime = "Mono"
        Assembly = Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
        Type = "ScheduleOne.PlayerScripts.PlayerInventory"
        Patterns = @("AddItemToInventory")
    },
    @{
        Runtime = "IL2CPP"
        Assembly = Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
        Type = "Il2CppScheduleOne.PlayerScripts.PlayerInventory"
        Patterns = @("AddItemToInventory")
    }
)

$verified = 0
foreach ($target in $targets) {
    $assembly = [System.IO.Path]::GetFullPath($target.Assembly)
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw "Missing $($target.Runtime) assembly: $assembly"
    }

    $surface = (& ilspycmd -t $target.Type $assembly 2>$null) -join "`n"
    foreach ($pattern in $target.Patterns) {
        if ($surface.IndexOf($pattern, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Missing $($target.Runtime) surface $($target.Type).$pattern"
        }
    }

    $verified++
}

$monoHash = (Get-FileHash -LiteralPath (Join-Path $MonoGamePath "Schedule I_Data\Managed\Assembly-CSharp.dll") -Algorithm SHA256).Hash
$il2CppHash = (Get-FileHash -LiteralPath (Join-Path $Il2CppGamePath "MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll") -Algorithm SHA256).Hash
Write-Output "PASS|S1AntiCheat.GameSurface|$verified targets|Mono=$monoHash|IL2CPP=$il2CppHash"

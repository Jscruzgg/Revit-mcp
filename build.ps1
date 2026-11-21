# Revit Multi Bridge - build + install (ASCII only: PowerShell 5.1 reads files without BOM as ANSI).
# Compiles the add-in for every installed Revit 2024-2027 (or -Years) and the gateway exe, and installs them per user (no admin):
#   add-in : %APPDATA%\Autodesk\Revit\Addins\<Year>\RevitMultiBridge\  +  ...\Addins\<Year>\RevitMultiBridge.addin
#   gateway: %LOCALAPPDATA%\revit_multi_bridge\gateway\RevitMultiBridgeGateway.exe   (the MCP server Claude starts, stdio)
# Needs: pyRevit installed (Roslyn is copied from %APPDATA%\pyRevit-Master\bin\netfx). A Revit that is open keeps its add-in dll
# locked: that year is skipped (close it and run again).
#   -TestOut <dir>  only compile into <dir>\<Year> + gateway (installs nothing)
#   -Register       also add the gateway to Claude Code (claude mcp add -s user revit-multi ...)
param([int[]]$Years, [string]$TestOut, [switch]$Register)
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
$pyr = "$env:APPDATA\pyRevit-Master\bin\netfx"
$fw  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if ($PSBoundParameters.ContainsKey("TestOut") -and -not $TestOut) { throw "-TestOut is empty: nothing compiled or installed." }
if (-not (Test-Path "$pyr\Microsoft.CodeAnalysis.CSharp.dll")) { throw "Roslyn not found in $pyr (install pyRevit)." }
. "$src\src\Build\compile.ps1"

if (-not $Years) {
    $Years = Get-ChildItem "C:\Program Files\Autodesk" -Directory | Where-Object { $_.Name -match '^Revit (\d{4})$' -and [int]$Matches[1] -ge 2024 -and [int]$Matches[1] -le 2027 -and (Test-Path "$($_.FullName)\RevitAPI.dll") } |
             ForEach-Object { [int]$_.Name.Substring(6) } | Sort-Object
}
$hostSrc = @(Get-ChildItem "$src\src\Core", "$src\src\Revit" -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object FullName)
$deps = "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp", "System.Collections.Immutable", "System.Reflection.Metadata",
        "System.Memory", "System.Runtime.CompilerServices.Unsafe", "System.Numerics.Vectors", "System.Threading.Tasks.Extensions", "System.Text.Encoding.CodePages"
function Locked($f) { if (-not (Test-Path $f)) { return $false }; try { [IO.File]::Open($f, "Open", "ReadWrite", "None").Close(); return $false } catch { return $true } }

$failed = 0
foreach ($y in $Years) {
    $revit = "C:\Program Files\Autodesk\Revit $y"
    $addins = "$env:APPDATA\Autodesk\Revit\Addins\$y"
    $out = if ($TestOut) { "$TestOut\$y" } else { "$addins\RevitMultiBridge" }
    try {
        if (-not (Test-Path "$revit\RevitAPI.dll")) { throw "Revit $y is not installed" }
        if (Locked "$out\RevitMultiBridge.dll") { throw "Revit $y is open (the add-in dll is in use): close it and run again" }
        New-Item -ItemType Directory -Force "$out\lib", "$out\icons" | Out-Null
        foreach ($d in $deps) { Copy-Item "$pyr\$d.dll" "$out\lib" -Force }
        if (Test-Path "$pyr\engines\IPY342\System.Buffers.dll") { Copy-Item "$pyr\engines\IPY342\System.Buffers.dll" "$out\lib" -Force }
        Copy-Item "$src\icons\*.png" "$out\icons" -Force
        Compile-RR -Kind host -Year $y -Revit $revit -Lib "$out\lib" -Out "$out\RevitMultiBridge.dll" -Sources $hostSrc
        if (-not $TestOut) { Copy-Item "$src\src\RevitMultiBridge.addin" "$addins\RevitMultiBridge.addin" -Force }
        Write-Host "Revit $y : OK  $out" -ForegroundColor Green
    }
    catch { $failed++; Write-Host "Revit $y : FAILED - $($_.Exception.Message)" -ForegroundColor Red }
}

# gateway: plain .NET Framework 4.8 console exe (every Windows has it)
$gwDir = if ($TestOut) { "$TestOut\gateway" } else { "$env:LOCALAPPDATA\revit_multi_bridge\gateway" }
New-Item -ItemType Directory -Force $gwDir | Out-Null
$gwExe = "$gwDir\RevitMultiBridgeGateway.exe"
$gwSrc = "$src\src\Gateway\Gateway.cs", "$src\src\Core\Core.cs", "$src\src\Core\Json.cs", "$src\src\Core\Peers.cs", "$src\src\Core\Server.cs"
$log = & "$fw\csc.exe" /nologo /codepage:65001 /target:exe /optimize+ "/out:$gwExe" "/r:System.dll" "/r:System.Core.dll" $gwSrc
if ($LASTEXITCODE -ne 0) { $log | Write-Host; Write-Host "Gateway : FAILED" -ForegroundColor Red; $failed++ }
else { Write-Host "Gateway : OK  $gwExe" -ForegroundColor Green }

if ($Register -and -not $TestOut -and $LASTEXITCODE -eq 0) {
    & claude mcp remove -s user revit-multi 2>$null | Out-Null
    & claude mcp add -s user revit-multi -- "$gwExe"
}
if ($failed -gt 0) { exit 1 }

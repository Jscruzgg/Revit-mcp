# Shared compile step of build.ps1 and Installer\Install.ps1 (compatibility phase, 9 Nov 2026). ASCII only (PowerShell 5.1).
# Revit 2024 (.NET Framework 4.8): the csc of the .NET Framework that Windows has (C# 5), same commands as before.
# Revit 2025+ (.NET 8/10, read from <Revit>\RevitAPI.runtimeconfig.json): RRCompile.exe (Roslyn from lib\), references =
#   the installed .NET runtime of that version (Microsoft.NETCore.App + Microsoft.WindowsDesktop.App, both come with Revit).
# Revit 2026+: /define:REVIT2026_OR_GREATER (ElementId.IntegerValue was removed; see RevitIds.IntId).
# Usage: . "<src>\Build\compile.ps1"; Compile-RR -Kind host -Year 2026 -Revit <dir> -Lib <Roslyn dir> -Out <dll> -Sources <files>
$script:rrFw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$script:rrBuildDir = $PSScriptRoot
$script:rrCompiler = $null

# .NET major version Revit runs on (8, 10, ...); 0 = .NET Framework (no runtimeconfig.json before Revit 2025)
function Get-RRNetMajor([string]$revit) {
    $rc = "$revit\RevitAPI.runtimeconfig.json"
    if (-not (Test-Path $rc)) { return 0 }
    $tfm = (Get-Content $rc -Raw | ConvertFrom-Json).runtimeOptions.tfm
    if ($tfm -match '^net(\d+)\.') { return [int]$Matches[1] }
    throw "Unknown target framework '$tfm' in $rc"
}

# managed dlls of the newest installed runtime of that major version
function Get-RRNetRefs([int]$major) {
    $root = "$env:ProgramFiles\dotnet\shared"
    $refs = @()
    foreach ($name in "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App") {
        $dir = Get-ChildItem "$root\$name" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "^$major\.\d+\.\d+$" } |
               Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if (-not $dir) { throw ".NET $major ($name) is not installed in $root (Revit installs it)" }
        $refs += Get-ChildItem "$($dir.FullName)\*.dll" | Where-Object { try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false } } | ForEach-Object FullName
    }
    return $refs
}

# RRCompile.exe, built once per run in %TEMP% with the Framework csc
function Get-RRCompiler([string]$lib) {
    if ($script:rrCompiler) { return $script:rrCompiler }
    $fw = $script:rrFw
    $dir = Join-Path $env:TEMP ("RRCompile\" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force $dir | Out-Null
    $exe = "$dir\RRCompile.exe"
    $log = & "$fw\csc.exe" /nologo /codepage:65001 /optimize+ "/out:$exe" "/r:$lib\Microsoft.CodeAnalysis.dll" "/r:$lib\Microsoft.CodeAnalysis.CSharp.dll" `
             "/r:$lib\System.Collections.Immutable.dll" "/r:$fw\System.Runtime.dll" "/r:$fw\netstandard.dll" "$script:rrBuildDir\RRCompile.cs"
    if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw "RRCompile build failed" }
    $script:rrCompiler = $exe
    return $exe
}

function Compile-RR([string]$Kind, [int]$Year, [string]$Revit, [string]$Lib, [string]$Out, [string[]]$Sources) {
    $fw = $script:rrFw
    $major = Get-RRNetMajor $Revit
    if ($major -eq 0) {
        if ($Kind -eq "host") {
            $refs = "$Revit\RevitAPI.dll", "$Revit\RevitAPIUI.dll", "$Lib\Microsoft.CodeAnalysis.dll", "$Lib\Microsoft.CodeAnalysis.CSharp.dll",
                    "$Lib\System.Collections.Immutable.dll", "$Lib\System.Reflection.Metadata.dll", "$fw\System.Runtime.dll", "$fw\netstandard.dll",
                    "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll", "$fw\System.Xaml.dll"
            $log = & "$fw\csc.exe" /nologo /codepage:65001 /target:library /optimize+ /nowarn:1701,1702,618 "/out:$Out" ($refs | ForEach-Object { "/r:$_" }) $Sources
        }
        else {
            $refs = "$Revit\RevitAPI.dll", "$Revit\RevitAPIUI.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll"
            $log = & "$fw\csc.exe" /nologo /codepage:65001 /target:library /optimize+ /debug:pdbonly /nowarn:618 "/out:$Out" ($refs | ForEach-Object { "/r:$_" }) $Sources
        }
        if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw "Compile failed ($Kind, Revit $Year)" }
        return
    }
    $refs = @("$Revit\RevitAPI.dll", "$Revit\RevitAPIUI.dll") + (Get-RRNetRefs $major)
    if ($Kind -eq "host") { $refs += "$Lib\Microsoft.CodeAnalysis.dll", "$Lib\Microsoft.CodeAnalysis.CSharp.dll" }
    $defines = if ($Year -ge 2026) { "REVIT2026_OR_GREATER" } else { "-" }
    $pdb = if ($Kind -eq "tools") { "1" } else { "0" }
    $exe = Get-RRCompiler $Lib
    $lists = Join-Path $env:TEMP ("RRCompile\" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force $lists | Out-Null
    $refs | Set-Content -Encoding UTF8 "$lists\refs.txt"
    $Sources | Set-Content -Encoding UTF8 "$lists\sources.txt"
    $log = & $exe $Lib $Out "$lists\refs.txt" "$lists\sources.txt" $defines $pdb
    $code = $LASTEXITCODE
    Remove-Item -Recurse -Force $lists -ErrorAction SilentlyContinue
    if ($code -ne 0) { $log | Write-Host; throw "Compile failed ($Kind, Revit $Year, .NET $major)" }
}

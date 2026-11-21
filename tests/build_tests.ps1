# Builds tests\out\FakeRevit.exe (the real Server + BridgeWindow with a fake Revit backend) and the gateway, with the .NET Framework csc.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$out = "$PSScriptRoot\out"
New-Item -ItemType Directory -Force "$out\icons" | Out-Null
Copy-Item "$root\icons\*.png" "$out\icons" -Force
$core = Get-ChildItem "$root\src\Core" -Filter *.cs | Sort-Object FullName | ForEach-Object FullName
$log = & "$fw\csc.exe" /nologo /codepage:65001 /target:exe /optimize+ "/out:$out\FakeRevit.exe" "/r:System.dll" "/r:System.Core.dll" "/r:System.Windows.Forms.dll" "/r:System.Drawing.dll" $core "$PSScriptRoot\FakeRevit.cs"
if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw "FakeRevit build failed" }
$gw = "$root\src\Gateway\Gateway.cs", "$root\src\Core\Core.cs", "$root\src\Core\Json.cs", "$root\src\Core\Peers.cs", "$root\src\Core\Server.cs"
$log = & "$fw\csc.exe" /nologo /codepage:65001 /target:exe /optimize+ "/out:$out\RevitMultiBridgeGateway.exe" "/r:System.dll" "/r:System.Core.dll" $gw
if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw "Gateway build failed" }
"Built: $out\FakeRevit.exe, $out\RevitMultiBridgeGateway.exe"

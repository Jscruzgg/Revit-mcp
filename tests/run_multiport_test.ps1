# End-to-end test of the multiport bridge without Revit: several FakeRevit.exe (= Revit windows of any version) + the gateway over stdio.
# Run tests\build_tests.ps1 first. Prints PASS / FAIL per check; exit code 1 if any check failed.
$ErrorActionPreference = "Stop"
$out = "$PSScriptRoot\out"
$fails = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS  $name" -ForegroundColor Green } else { $script:fails++; Write-Host "FAIL  $name  $detail" -ForegroundColor Red }
}
function Start-Fake([string[]]$a) {
    $psi = New-Object Diagnostics.ProcessStartInfo "$out\FakeRevit.exe", ($a -join " ")
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
    $p = [Diagnostics.Process]::Start($psi)
    $line = $p.StandardOutput.ReadLine()
    return @{ Proc = $p; Line = $line; Port = if ($line -match '^PORT (\d+)') { [int]$Matches[1] } else { 0 } }
}

$fakes = @()
try {
    # four Revit windows: two of the SAME version (2024), one 2025, one 2027
    $fakes += Start-Fake @("2024", "zzTorreA", "--seconds", "90")
    $fakes += Start-Fake @("2024", "zzTorreB", "--seconds", "90")
    $fakes += Start-Fake @("2025", "zzCasa", "--seconds", "90")
    $fakes += Start-Fake @("2027", "zzBodega", "--seconds", "90")
    $ports = $fakes | ForEach-Object { $_.Port }
    Check "each Revit got its own port of the pool" (($ports | Where-Object { $_ -ge 5310 -and $_ -le 5329 } | Sort-Object -Unique).Count -eq 4) ("ports: " + ($ports -join ","))
    $portA = $fakes[0].Port; $portB = $fakes[1].Port

    $list = & "$out\RevitMultiBridgeGateway.exe" --list | Out-String
    Check "gateway --list finds the 4 windows" (($list -match "zzTorreA") -and ($list -match "zzTorreB") -and ($list -match "zzCasa") -and ($list -match "zzBodega")) $list

    # ---- gateway over stdio, like Claude Code ----
    $psi = New-Object Diagnostics.ProcessStartInfo "$out\RevitMultiBridgeGateway.exe"
    $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $gw = [Diagnostics.Process]::Start($psi)
    $gw.StandardInput.AutoFlush = $true
    $pending = @{}
    function Send($id, $method, $params) { $gw.StandardInput.WriteLine((@{ jsonrpc = "2.0"; id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress)) }
    function Receive($id) {
        while (-not $pending.ContainsKey($id)) { $r = $gw.StandardOutput.ReadLine() | ConvertFrom-Json; $pending[[int]$r.id] = $r }
        $r = $pending[$id]; $pending.Remove($id); return $r
    }
    function Tool($id, $name, $arguments) { Send $id "tools/call" @{ name = $name; arguments = $arguments }; $r = Receive $id; return $r.result }

    Send 1 "initialize" @{ protocolVersion = "2025-03-26"; capabilities = @{}; clientInfo = @{ name = "test"; version = "1" } }
    $r = Receive 1
    Check "initialize" ($r.result.serverInfo.name -eq "revit-multi-bridge") ($r | ConvertTo-Json -Depth 5)
    $gw.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    Send 2 "tools/list" @{}
    $names = (Receive 2).result.tools | ForEach-Object { $_.name }
    Check "tools/list has revit_instances + target tools" (($names -contains "revit_instances") -and ($names -contains "revit_write")) ($names -join ",")

    $t = Tool 3 "revit_instances" @{}
    Check "revit_instances lists all versions" (($t.content[0].text -match "Revit 2024") -and ($t.content[0].text -match "Revit 2025") -and ($t.content[0].text -match "Revit 2027") -and ($t.content[0].text -match "zzTorreB")) $t.content[0].text

    $t = Tool 4 "revit_read" @{ code = "Print(1);" }
    Check "no target with several Revit -> asks for target" ($t.isError -and $t.content[0].text -match "pass 'target'") $t.content[0].text

    $t = Tool 5 "revit_read" @{ code = "Print(Doc.Title);"; target = "zzCasa" }
    Check "target by document name reaches Revit 2025" ((-not $t.isError) -and $t.content[0].text -match "fake Revit 2025 / zzCasa") $t.content[0].text

    $t = Tool 6 "revit_read" @{ code = "x"; target = "2024" }
    Check "target by year with two 2024 windows -> ambiguous" ($t.isError -and $t.content[0].text -match "matches 2 Revit windows") $t.content[0].text

    $t = Tool 7 "revit_read" @{ code = "Print(2);"; target = "$portB" }
    Check "target by port reaches the second 2024 window" ((-not $t.isError) -and $t.content[0].text -match "2024 / zzTorreB") $t.content[0].text

    $pidA = $fakes[0].Proc.Id
    $t = Tool 8 "revit_write" @{ code = "Print(3);"; execute = $true; target = "pid:$pidA" }
    Check "target by pid + execute" ((-not $t.isError) -and $t.content[0].text -match "zzTorreA" -and $t.content[0].text -match "EXECUTED") $t.content[0].text

    $t = Tool 9 "revit_read" @{ code = "COMPILE_ERROR"; target = "zzBodega" }
    Check "compile error is reported (Revit 2027)" ($t.isError -and $t.content[0].text -match "COMPILE ERRORS") $t.content[0].text

    $t = Tool 10 "revit_read" @{ code = "x"; target = "Nadie" }
    Check "unknown target -> clear error" ($t.isError -and $t.content[0].text -match "No Revit window matches") $t.content[0].text

    # two Revit windows at once: both scripts take 2 s; in parallel the pair takes ~2 s, not 4
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Send 11 "tools/call" @{ name = "revit_read"; arguments = @{ code = "sleep:2000"; target = "zzTorreA" } }
    Send 12 "tools/call" @{ name = "revit_read"; arguments = @{ code = "sleep:2000"; target = "zzCasa" } }
    $r11 = Receive 11; $r12 = Receive 12; $sw.Stop()
    Check "calls to different Revit run in parallel" ((-not $r11.result.isError) -and (-not $r12.result.isError) -and $sw.ElapsedMilliseconds -lt 3500) ("$($sw.ElapsedMilliseconds) ms")

    # same Revit twice at once: the second one is refused as BUSY (one API thread per Revit)
    Send 13 "tools/call" @{ name = "revit_read"; arguments = @{ code = "sleep:1500"; target = "zzBodega" } }
    Start-Sleep -Milliseconds 400
    $t = Tool 14 "revit_read" @{ code = "x"; target = "zzBodega" }
    $r13 = Receive 13
    Check "same Revit busy -> BUSY, first call still OK" ($t.isError -and $t.content[0].text -match "BUSY" -and -not $r13.result.isError) $t.content[0].text

    # cancel a long script
    Send 15 "tools/call" @{ name = "revit_read"; arguments = @{ code = "sleep:20000"; target = "zzCasa" } }
    Start-Sleep -Milliseconds 500
    $c = Tool 16 "revit_cancel" @{ target = "zzCasa" }
    $r15 = Receive 15
    Check "revit_cancel stops the running script" ($c.content[0].text -match "Cancellation requested" -and $r15.result.content[0].text -match "CANCELLED") ($r15.result.content[0].text)

    # ---- Re-claim: a new Revit 2026 takes TorreA's port; TorreA moves to another free port and stays reachable ----
    $rc = Start-Fake @("2026", "zzNuevo", "--seconds", "60", "--reclaim", "$portA")
    $fakes += $rc
    $rcLine = $rc.Proc.StandardOutput.ReadLine()
    Check "Re-claim takes the port" ($rcLine -eq "RECLAIMED $portA") $rcLine
    Start-Sleep -Milliseconds 2500
    $t = Tool 17 "revit_read" @{ code = "Print(4);"; target = "zzTorreA" }
    $moved = if ($t.content[0].text -match "port (\d+)") { [int]$Matches[1] } else { 0 }
    Check "the Revit that lost the port moved and still answers" ((-not $t.isError) -and $moved -ne $portA -and $moved -ge 5310) $t.content[0].text
    $t = Tool 18 "revit_read" @{ code = "Print(5);"; target = "$portA" }
    Check "the re-claimed port now reaches Revit 2026" ((-not $t.isError) -and $t.content[0].text -match "2026 / zzNuevo") $t.content[0].text

    # ---- window picture with the 5 bridges (off-screen) ----
    $png = "$out\bridge_window.png"
    $snap = Start-Fake @("2026", "zzVentana", "--seconds", "1", "--snapshot", "`"$png`"", "--wait", "3500")
    $snap.Proc.WaitForExit(20000) | Out-Null
    Check "window snapshot written" (Test-Path $png) $png

    $gw.StandardInput.Close(); $gw.WaitForExit(3000) | Out-Null
}
finally {
    foreach ($f in $fakes) { try { if (-not $f.Proc.HasExited) { $f.Proc.Kill() } } catch {} }
    try { if ($gw -and -not $gw.HasExited) { $gw.Kill() } } catch {}
}
if ($fails -gt 0) { Write-Host "$fails check(s) FAILED" -ForegroundColor Red; exit 1 } else { Write-Host "All checks passed" -ForegroundColor Green }

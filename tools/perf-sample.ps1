<#
  Samples what Verso costs while it runs:   pwsh tools/perf-sample.ps1 [-Seconds 60] [-Label "video wallpaper, idle"]

  Adds up CPU and memory of Verso.exe and every process it started (the WebView2 browser, renderer and GPU
  processes), once per interval, and prints the average and the peak. CPU is a percentage of the whole machine.
  GPU is not sampled here: add the "GPU" and "GPU engine" columns in Task Manager > Details and read them for the
  same processes.

  Run it for each state worth knowing about and write the numbers into docs/performance.md:
    - no wallpaper, widgets off          - video wallpaper, desk visible
    - interactive wallpaper              - a maximised window in front (should drop to ~0)
    - everything on                      - a game in full screen (should drop to ~0)
#>
param(
    [int]$Seconds = 60,
    [int]$IntervalSeconds = 5,
    [string]$Label = ""
)

$ErrorActionPreference = "Stop"
$verso = Get-Process Verso -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $verso) { throw "Verso is not running." }

function Get-Tree([int]$rootId) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
    $ids = [System.Collections.Generic.List[int]]::new()
    $ids.Add($rootId)
    for ($i = 0; $i -lt $ids.Count; $i++) {
        foreach ($child in ($all | Where-Object { $_.ParentProcessId -eq $ids[$i] })) {
            if (-not $ids.Contains([int]$child.ProcessId)) { $ids.Add([int]$child.ProcessId) }
        }
    }
    return $ids
}

function Take-Sample {
    $ids = Get-Tree $verso.Id
    $procs = Get-Process -Id $ids -ErrorAction SilentlyContinue
    [pscustomobject]@{
        CpuSecs   = [double](($procs | ForEach-Object { $_.TotalProcessorTime.TotalSeconds }) | Measure-Object -Sum).Sum
        MemoryMB  = [math]::Round((($procs | Measure-Object -Property WorkingSet64 -Sum).Sum) / 1MB, 0)
        Processes = @($procs).Count
    }
}

$cores = [Environment]::ProcessorCount
$rows = @()
$previous = Take-Sample
$end = (Get-Date).AddSeconds($Seconds)

while ((Get-Date) -lt $end) {
    Start-Sleep -Seconds $IntervalSeconds
    $now = Take-Sample
    $cpu = [math]::Max(0, ($now.CpuSecs - $previous.CpuSecs) / ($IntervalSeconds * $cores) * 100)
    $rows += [pscustomobject]@{ CpuPercent = [math]::Round($cpu, 2); MemoryMB = $now.MemoryMB; Processes = $now.Processes }
    $previous = $now
}

$title = if ($Label) { " ($Label)" } else { "" }
Write-Host "Verso$title - $Seconds s, every $IntervalSeconds s, $cores logical processors"
$rows | Format-Table -AutoSize
"{0,-14} avg {1,6:N2} %   peak {2,6:N2} %" -f "CPU", ($rows | Measure-Object CpuPercent -Average).Average, ($rows | Measure-Object CpuPercent -Maximum).Maximum
"{0,-14} avg {1,6:N0} MB  peak {2,6:N0} MB" -f "Memory", ($rows | Measure-Object MemoryMB -Average).Average, ($rows | Measure-Object MemoryMB -Maximum).Maximum
"{0,-14} {1}" -f "Processes", ($rows | Select-Object -Last 1).Processes

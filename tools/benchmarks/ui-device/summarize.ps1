param([Parameter(Mandatory = $true)][string[]]$Paths)

function Get-Median($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    if ($sorted.Count % 2 -eq 0) {
        return ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
    }
    return $sorted[[int][Math]::Floor($sorted.Count / 2)]
}

$rows = foreach ($inputPath in $Paths) {
    foreach ($line in Get-Content -LiteralPath $inputPath -Encoding utf8) {
        $record = $line | ConvertFrom-Json
        if ($record.Kind -eq 'case') {
            [pscustomobject]@{
                File = [IO.Path]::GetFileName($inputPath)
                Run = $record.Label
                Name = $record.Name
                Frames = $record.Frames.Count
                TotalMs = Get-Median $record.Frames.TotalMilliseconds
                PrepareMs = Get-Median $record.Frames.Phases.prepare
                RecordMs = Get-Median $record.Frames.Phases.record
                DrainMs = Get-Median $record.Frames.Phases.drain
                Bytes = Get-Median $record.Frames.AllocatedBytes
            }
        }
    }
}
$rows | ConvertTo-Json -Depth 5

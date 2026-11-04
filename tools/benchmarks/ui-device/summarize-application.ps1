param([Parameter(Mandatory = $true)][string[]]$Paths)

function Get-Percentile($Sorted, [double]$Quantile) {
    return $Sorted[[int][Math]::Ceiling($Sorted.Count * $Quantile) - 1]
}

$rows = foreach ($inputPath in $Paths) {
    foreach ($line in Get-Content -LiteralPath $inputPath -Encoding utf8) {
        $record = $line | ConvertFrom-Json
        if ($record.Kind -ne 'application' -or $record.Samples.Count -eq 0) { continue }
        $intervals = @($record.Samples.CompletionIntervalMilliseconds | Sort-Object)
        $mean = ($intervals | Measure-Object -Average).Average
        [pscustomobject]@{
            File = [IO.Path]::GetFileName($inputPath)
            Label = $record.Label
            Count = $record.Count
            Changing = $record.Changing
            ChangedElements = $record.ChangedElements
            LayoutChanging = $record.LayoutChanging
            Frames = $intervals.Count
            MeanHz = 1000 / $mean
            MeanMs = $mean
            P50Ms = Get-Percentile $intervals 0.50
            P95Ms = Get-Percentile $intervals 0.95
            P99Ms = Get-Percentile $intervals 0.99
            MaxMs = $intervals[-1]
            Over16_667Ms = @($intervals | Where-Object { $_ -gt 1000 / 60 }).Count
            MeanAllocatedBytes = ($record.Samples.AllocatedBytes | Measure-Object -Average).Average
        }
    }
}
$rows | ConvertTo-Json -Depth 5

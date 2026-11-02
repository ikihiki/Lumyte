param([Parameter(Mandatory = $true)][string[]]$Paths)

function Distribution($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    [pscustomobject]@{
        Mean = ($sorted | Measure-Object -Average).Average
        P50 = $sorted[[int][Math]::Ceiling($sorted.Count * 0.50) - 1]
        P95 = $sorted[[int][Math]::Ceiling($sorted.Count * 0.95) - 1]
        P99 = $sorted[[int][Math]::Ceiling($sorted.Count * 0.99) - 1]
        Max = $sorted[-1]
    }
}

$rows = foreach ($path in $Paths) {
    if ($path.EndsWith('.gz')) {
        $stream = [IO.File]::OpenRead((Resolve-Path $path))
        $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
        $reader = [IO.StreamReader]::new($gzip)
        try { $record = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose(); $stream.Dispose() }
    } else { $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    $samples = $record.Samples
    $groups = @($samples.Details.Phases | Group-Object Name)
    $phases = foreach ($group in $groups) {
        $values = @($group.Group.Milliseconds) + @(0) * ($samples.Count - $group.Count)
        [pscustomobject]@{
            Name = $group.Name
            CallsPerFrame = ($group.Group.Calls | Measure-Object -Sum).Sum / $samples.Count
            MillisecondsPerFrame = Distribution $values
            AllocatedBytesPerFrame = ($group.Group.AllocatedBytes | Measure-Object -Sum).Sum / $samples.Count
            CrossThreadSpans = ($group.Group.CrossThreadSpans | Measure-Object -Sum).Sum
        }
    }
    $gcSamples = @($samples | Where-Object { $_.Details.GcPauseMilliseconds -gt 0 })
    $withoutGc = @($samples | Where-Object { $_.Details -and $_.Details.GcPauseMilliseconds -eq 0 })
    $timeline = @($samples | ForEach-Object {
        $recording = $_.Details.Phases | Where-Object Name -eq 'host.record-frame'
        $submission = $_.Details.Phases | Where-Object Name -eq 'gpu.submit'
        if ($recording.StartedTimestamp -and $submission.EndedTimestamp -and $recording.Calls -eq 1 -and $submission.Calls -eq 1) {
            [pscustomobject]@{ Frame = $_.Frame; Recording = $recording.StartedTimestamp; Submission = $submission.EndedTimestamp; Completion = $_.Timestamp }
        }
    })
    $scale = 1000d / [Diagnostics.Stopwatch]::Frequency
    $recordingIntervals = for ($i = 1; $i -lt $timeline.Count; $i++) { ($timeline[$i].Recording - $timeline[$i - 1].Recording) * $scale }
    [pscustomobject]@{
        File = [IO.Path]::GetFileName($path)
        Label = $record.Label
        Frames = $samples.Count
        MeanHz = 1000 / ($samples.CompletionIntervalMilliseconds | Measure-Object -Average).Average
        CompletionIntervalMilliseconds = Distribution $samples.CompletionIntervalMilliseconds
        MeanAllocatedBytes = ($samples.AllocatedBytes | Measure-Object -Average).Average
        Phases = @($phases | Sort-Object { $_.MillisecondsPerFrame.Mean } -Descending)
        Gc = if ($samples[0].Details) { [pscustomobject]@{
            Gen0 = ($samples.Details.Gen0 | Measure-Object -Sum).Sum
            Gen1 = ($samples.Details.Gen1 | Measure-Object -Sum).Sum
            Gen2 = ($samples.Details.Gen2 | Measure-Object -Sum).Sum
            PauseMilliseconds = ($samples.Details.GcPauseMilliseconds | Measure-Object -Sum).Sum
            FramesWithPause = $gcSamples.Count
            PauseDistribution = Distribution $gcSamples.Details.GcPauseMilliseconds
            IntervalsWithPause = Distribution $gcSamples.CompletionIntervalMilliseconds
            IntervalsWithoutPause = Distribution $withoutGc.CompletionIntervalMilliseconds
        } } else { $null }
        Timeline = if ($timeline.Count) { [pscustomobject]@{
            Frames = $timeline.Count
            StopwatchFrequency = [Diagnostics.Stopwatch]::Frequency
            RecordingIntervalsMilliseconds = Distribution $recordingIntervals
            RecordingToSubmissionMilliseconds = Distribution @($timeline | ForEach-Object { ($_.Submission - $_.Recording) * $scale })
            RecordingToCompletionMilliseconds = Distribution @($timeline | ForEach-Object { ($_.Completion - $_.Recording) * $scale })
        } } else { $null }
        SlowFrames = @($samples | Sort-Object CompletionIntervalMilliseconds -Descending | Select-Object -First 12 | ForEach-Object {
            $phaseTimes = [ordered]@{}
            foreach ($phase in $_.Details.Phases | Where-Object Name -in @('surface.capabilities.native', 'signal.batch', 'gpu.submission-wait', 'owner.timer-lateness', 'layout.compute')) {
                $phaseTimes[$phase.Name] = $phase.Milliseconds
            }
            [pscustomobject]@{ Frame = $_.Frame; IntervalMilliseconds = $_.CompletionIntervalMilliseconds; AllocatedBytes = $_.AllocatedBytes; GcPauseMilliseconds = $_.Details.GcPauseMilliseconds; PhaseMilliseconds = $phaseTimes }
        })
    }
}
$rows | ConvertTo-Json -Depth 12

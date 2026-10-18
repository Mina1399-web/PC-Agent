function Assert-IsolatedCandidatePath([string]$CandidateDirectory,[string]$EvidenceRoot) {
    $candidate=[IO.Path]::GetFullPath($CandidateDirectory)
    $evidence=[IO.Path]::GetFullPath($EvidenceRoot)
    if(-not $candidate.StartsWith($evidence+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'candidate_outside_evidence'
    }
    $ancestor=Get-Item -LiteralPath $candidate -Force -ErrorAction Stop
    while($null -ne $ancestor){
        if(($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'candidate_reparse_point'}
        $ancestor=$ancestor.Parent
    }
    # Inspect each directory before traversing it; never follow reparse points.
    $pending=New-Object System.Collections.Generic.Queue[string]
    $pending.Enqueue($candidate)
    while($pending.Count -gt 0){
        foreach($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force -ErrorAction Stop){
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'candidate_reparse_point'}
            if($item.PSIsContainer){$pending.Enqueue($item.FullName)}
        }
    }
    return $candidate
}

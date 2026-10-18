function Test-IsolatedCrashTarget {
    param([string]$RunId,$Service,$Process,[int]$ProtectedPid)
    $id=[guid]::Empty
    if(-not [guid]::TryParseExact($RunId,'N',[ref]$id) -or $id -eq [guid]::Empty -or -not $Service -or -not $Process){return $false}
    $name='HostAgentDevScm_'+$RunId
    $image='C:\ProgramData\HostAgent-DEV-SCM-'+$RunId+'\app\HostAgent.PipeProbe.exe'
    return ($Service.Name -ceq $name -and $Service.State -eq 'Running' -and
        $Service.StartName -ceq ('NT SERVICE\'+$name) -and
        [int]$Service.ProcessId -gt 0 -and [int]$Service.ProcessId -ne $ProtectedPid -and
        $Process.ProcessId -eq $Service.ProcessId -and $Process.ExecutablePath -ieq $image -and
        $Process.CommandLine -ceq $Service.PathName -and
        $Process.CommandLine -match ('^"'+[regex]::Escape($image)+'" isolated-scm-service '+$RunId+' S-1-5-21-[0-9-]+ crash-proof$'))
}
Export-ModuleMember -Function Test-IsolatedCrashTarget

$f = 'Services\AI\AIAssistant.cs'
$c = Get-Content $f -Raw
$start = $c.IndexOf('        string BuildProviderUnavailableNotice()')
$end = $c.IndexOf('        private static string SearchCatalogLine')
$nl = "`r`n"
$q = '"'
$newMethod = "        string BuildProviderUnavailableNotice()$nl        {$nl            var llamaError = _llamaAiService?.LastError ?? ${q}${q};$nl            if (_llamaAiService?.IsCrashed == true ||$nl                llamaError.Contains(${q}CUDA${q}, StringComparison.OrdinalIgnoreCase) ||$nl                llamaError.Contains(${q}unsupported toolchain${q}, StringComparison.OrdinalIgnoreCase) ||$nl                llamaError.Contains(${q}PTX${q}, StringComparison.OrdinalIgnoreCase))$nl            {$nl                return ${q}AI provider unavailable: Ollama CUDA runtime is incompatible with the installed NVIDIA driver. Static fallback cannot execute commands or generate event changes.${q};$nl            }$nl$nl            return ${q}AI provider unavailable. Static fallback cannot execute commands or generate event changes.${q};$nl        }$nl$nl$nl"
$result = $c.Substring(0, $start) + $newMethod + $c.Substring($end)
[System.IO.File]::WriteAllText((Resolve-Path $f), $result)
Write-Output "Done"

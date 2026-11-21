param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRepo 'MindScape'
$taskOutput = Join-Path $taskProject 'Library\MindScapeEnvironment\offline'
$taskData = Join-Path (Split-Path -Parent $UnityEditor) 'Data'
$taskMono = Join-Path $taskData 'MonoBleedingEdge\bin\mono.exe'
$taskCsc = Join-Path $taskData 'MonoBleedingEdge\lib\mono\4.5\csc.exe'
$taskUi = Join-Path $taskProject 'Library\ScriptAssemblies\UnityEngine.UI.dll'
if (-not (Test-Path -LiteralPath $taskUi)) {
    $taskUi = Join-Path $taskData 'Resources\PackageManager\ProjectTemplates\libcache\com.unity.template.3d-8.1.3\ScriptAssemblies\UnityEngine.UI.dll'
}
if (-not (Test-Path -LiteralPath $taskUi)) { throw 'UnityEngine.UI.dll is unavailable. Import the project in Unity first.' }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskArgs = @('-nologo','-nostdlib','-target:library', ('-out:"' + (Join-Path $taskOutput 'MindScape.Environment.dll') + '"'))
foreach ($taskDirectory in @((Join-Path $taskData 'Managed\UnityEngine'), (Join-Path $taskData 'NetStandard\ref\2.1.0'))) {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskDirectory -Filter '*.dll') { $taskArgs += '-r:"' + $taskFile.FullName + '"' }
}
$taskArgs += '-r:"' + $taskUi + '"'
$taskRuntimeSources = @(Get-ChildItem -LiteralPath (Join-Path $taskProject 'Assets\MindScapeEnvironment') -Filter '*.cs')
foreach ($taskFile in $taskRuntimeSources) { $taskArgs += '"' + $taskFile.FullName + '"' }
$taskResponse = Join-Path $taskOutput 'runtime.rsp'
Set-Content -LiteralPath $taskResponse -Value $taskArgs -Encoding utf8
& $taskMono $taskCsc ('@' + $taskResponse)
if ($LASTEXITCODE -ne 0) { throw 'Environment runtime assembly compilation failed.' }
$taskExe = Join-Path $taskOutput 'SessionChecks.exe'
& $taskMono $taskCsc -nologo -target:exe "-out:$taskExe" (Join-Path $taskProject 'Assets\MindScapeEnvironment\AmbienceSession.cs') (Join-Path $taskRepo 'tools\tests\SessionChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Offline session tests could not compile.' }
$taskTestOutput = & $taskMono $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Offline session tests failed.' }
$taskTestOutput
$taskResult = [PSCustomObject]@{Status='passed'; RuntimeSourceCount=$taskRuntimeSources.Count; SessionTests=22; UnityEngineReferences=$taskData; UiReference=$taskUi; RunAt=[DateTimeOffset]::Now.ToString('o'); Scope='Complete environment runtime assembly compilation and shared consent/preferences logic. Not Unity Play Mode or headset validation.'}
$taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'results.json') -Encoding utf8
$taskResult | Format-List

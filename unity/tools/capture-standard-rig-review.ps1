param([string]$Unity = "$env:LOCALAPPDATA/Unity/bin/unity.exe", [switch]$Side)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../withme_frontend'))
$directory = Join-Path $project 'Temp/batch/review'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$clips = @('Idle','Walk','Trot','Greet','PetHead','PetChin','PetBelly','HeadTilt','SitDown','SitIdle','StandUp','LieDown','LieIdle','Sleep','GetUp','FallAsleep','WakeUp')
$prefix = ''
if ($Side) {
    $prefix='Side-';$clips=@('Walk','Greet','PetBelly','SitIdle','LieIdle','Sleep')
    $code='var p=UnityEngine.Object.FindFirstObjectByType<Withme.Character.StandardRigProbe>();for(var i=0;i<p.players.Length;i++){p.players[i].transform.rotation=UnityEngine.Quaternion.Euler(0,70,0);p.players[i].transform.position=new UnityEngine.Vector3((i-1)*1.6f,0,0);}UnityEngine.Camera.main.orthographicSize=1.2f;return "side view";'
    $result=(& $Unity command script eval --caller plugin --skill unity-cli --project-path $project --format json -- $code|ConvertFrom-Json)
    if(!$result.success -or !$result.data.result.success){throw 'Side view setup failed'}
}
$files = @()
foreach ($clip in $clips) {
    $code = 'var probe=UnityEngine.Object.FindFirstObjectByType<Withme.Character.StandardRigProbe>();if(probe==null||!probe.Ready)throw new System.Exception("Play ready standard probe first");probe.Play("__CLIP__");foreach(var player in probe.players){player.useShared=true;player.Paused=true;player.Play("__CLIP__",0);var c=System.Linq.Enumerable.First(player.sharedLibrary.clips,x=>x.name=="__CLIP__");player.Sample(c.length*.5f);}return "__CLIP__";'.Replace('__CLIP__',$clip)
    $sample = (& $Unity command script eval --caller plugin --skill unity-cli --project-path $project --format json -- $code | ConvertFrom-Json)
    if (!$sample.success -or !$sample.data.result.success) { throw "Pose selection failed: $clip" }
    $capture = (& $Unity command capture game_view --caller plugin --skill unity-cli --project-path $project --format json -- --source screen --width 1200 --height 900 --save_path "Temp/rig-review-$prefix$clip.png" | ConvertFrom-Json)
    if (!$capture.success -or $capture.data.result.width -ne 1200) { throw "Capture failed: $clip" }
    $path = Join-Path $directory "$prefix$clip.png"
    Copy-Item -LiteralPath (Join-Path $project "Assets/Temp/rig-review-$prefix$clip.png") -Destination $path
    $files += $path
    Write-Output "$clip captured"
}
@{ captures=$files.Count; files=$files; sample='clip midpoint'; models=@('m1_golden','m2_white','m3_cream'); qualityApproved=$false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory "$prefix`manifest.json") -Encoding utf8

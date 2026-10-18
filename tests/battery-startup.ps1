$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $root 'dist\PromptSticky.exe'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exePath))
$repairType = $assembly.GetType('PromptStickyTool.StartupTaskCompatibility')
$service = New-Object -ComObject 'Schedule.Service'
$service.Connect()
$taskRoot = $service.GetFolder('\')
$folderName = 'PromptStickyBatteryTest-' + [Guid]::NewGuid().ToString('N')
$folder = $taskRoot.CreateFolder($folderName, $null)
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskNames = @()

function Add-FixtureTask([string]$name, [string]$path, [bool]$safe = $false) {
    $definition = $service.NewTask(0)
    $definition.Principal.UserId = $userSid
    $definition.Principal.LogonType = 3 # TASK_LOGON_INTERACTIVE_TOKEN
    $definition.Settings.Enabled = $false # Never launch test applications.
    $definition.Settings.ExecutionTimeLimit = 'PT8H'
    if ($safe) {
        $definition.Settings.DisallowStartIfOnBatteries = $false
        $definition.Settings.StopIfGoingOnBatteries = $false
    }
    $action = $definition.Actions.Create(0)
    $action.Path = $path
    $action.Arguments = '--battery-test'
    $action.WorkingDirectory = $root
    $trigger = $definition.Triggers.Create(9)
    $trigger.UserId = $userSid
    $null = $folder.RegisterTaskDefinition($name, $definition, 2, $null, $null, 3, $null)
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Invoke-Repair {
    # Baseline versions have no repair and leave Windows' battery-stop default in place.
    if ($null -ne $repairType) {
        $arguments = New-Object object[] 1
        $arguments[0] = [string]$exePath
        $null = $repairType.GetMethod('RepairBatterySettings').Invoke($null, $arguments)
    }
}

try {
    foreach ($name in @('Legacy', 'AlreadySafe', 'Unrelated', 'OtherLocation', 'EnvironmentPath', 'MultipleActions')) {
        $taskNames += $name
        $path = $exePath
        if ($name -eq 'Unrelated') { $path = Join-Path $env:WINDIR 'System32\cmd.exe' }
        if ($name -eq 'OtherLocation') { $path = Join-Path $root 'other-location\PromptSticky.exe' }
        if ($name -eq 'EnvironmentPath') {
            $env:PROMPT_STICKY_BATTERY_TEST_ROOT = $root
            $path = '%PROMPT_STICKY_BATTERY_TEST_ROOT%\dist\PromptSticky.exe'
        }
        Add-FixtureTask $name $path ($name -eq 'AlreadySafe')
    }
    $multiple = $folder.GetTask('MultipleActions').Definition
    $extraAction = $multiple.Actions.Create(0)
    $extraAction.Path = Join-Path $env:WINDIR 'System32\cmd.exe'
    $null = $folder.RegisterTaskDefinition('MultipleActions', $multiple, 4, $null, $null, 3, $null)

    $before = $folder.GetTask('Legacy').Definition
    Assert-True $before.Settings.StopIfGoingOnBatteries 'Fixture must reproduce stopping the app on battery.'
    Assert-True $before.Settings.DisallowStartIfOnBatteries 'Fixture must reproduce blocked startup on battery.'
    $unchangedXml = @{}
    $originalUsers = @{}
    $originalXml = @{}
    foreach ($name in @('Legacy', 'EnvironmentPath')) {
        $originalUsers[$name] = $folder.GetTask($name).Definition.Principal.UserId
        $originalXml[$name] = $folder.GetTask($name).Xml
    }
    foreach ($name in @('AlreadySafe', 'Unrelated', 'OtherLocation', 'MultipleActions')) {
        $unchangedXml[$name] = $folder.GetTask($name).Xml
    }

    Invoke-Repair
    foreach ($name in @('Legacy', 'EnvironmentPath')) {
        $after = $folder.GetTask($name).Definition
        Assert-True (-not $after.Settings.StopIfGoingOnBatteries) "$name still stops the app when AC power is disconnected."
        Assert-True (-not $after.Settings.DisallowStartIfOnBatteries) "$name still prevents battery startup."
        Assert-True (-not $after.Settings.Enabled) "$name changed task enabled state."
        Assert-True ($after.Settings.ExecutionTimeLimit -eq 'PT8H') "$name changed execution time limit."
        Assert-True ($after.Actions.Item(1).Arguments -eq '--battery-test') "$name changed arguments."
        Assert-True ($after.Actions.Item(1).WorkingDirectory -eq $root) "$name changed working directory."
        Assert-True ($after.Triggers.Count -eq 1 -and $after.Triggers.Item(1).Type -eq 9) "$name changed logon trigger."
        Assert-True ($after.Principal.UserId -eq $originalUsers[$name] -and $after.Principal.LogonType -eq 3) "$name changed user identity."
        $expectedXml = $originalXml[$name].Replace('<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>', '<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>').Replace('<StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>', '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>')
        Assert-True ($folder.GetTask($name).Xml -eq $expectedXml) "$name changed settings other than the two battery conditions."
    }
    foreach ($name in $unchangedXml.Keys) {
        Assert-True ($folder.GetTask($name).Xml -eq $unchangedXml[$name]) "$name should remain untouched."
    }
    $repairedXml = $folder.GetTask('Legacy').Xml
    Invoke-Repair
    Assert-True ($folder.GetTask('Legacy').Xml -eq $repairedXml) 'Repeated startup should not register the task again.'
    Write-Host 'PASS: battery startup and continued running are enabled; unrelated tasks, triggers, arguments and settings are preserved; repeated repair is a no-op.'
}
finally {
    foreach ($name in $taskNames) {
        try { $folder.DeleteTask($name, 0) } catch { Write-Warning "Cannot remove fixture task $name" }
    }
    $taskRoot.DeleteFolder($folderName, 0)
    Remove-Item Env:\PROMPT_STICKY_BATTERY_TEST_ROOT -ErrorAction SilentlyContinue
}

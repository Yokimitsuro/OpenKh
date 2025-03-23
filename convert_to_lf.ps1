$filesToConvert = @(
    "OpenKh.Patcher\PatcherProcessor.cs",
    "OpenKh.Tools.ModsManager\Views\YamlGeneratorVM.cs",
    "OpenKh.Tools.ModsManager\Views\YamlGeneratorWindow.xaml",
    "OpenKh.Tools.ModsManager\Services\ConfigurationService.cs"
)

foreach ($file in $filesToConvert) {
    $fullPath = Join-Path $PSScriptRoot $file
    Write-Host "Converting $fullPath to LF line endings"
    
    # Read the content with current line endings
    $content = [System.IO.File]::ReadAllText($fullPath)
    
    # Replace CRLF with LF
    $content = $content -replace "`r`n", "`n"
    
    # Write back the content with LF line endings
    [System.IO.File]::WriteAllText($fullPath, $content)
}

Write-Host "Conversion completed"

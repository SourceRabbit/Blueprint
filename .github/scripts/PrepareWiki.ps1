<#
.SYNOPSIS
Προετοιμάζει τις σελίδες και τις εικόνες του .wiki για δημοσίευση στο GitHub Wiki.
.PARAMETER sourceDirectory
Ο φάκελος .wiki μέσα στο κύριο repository.
.PARAMETER destinationDirectory
Ο φάκελος εργασίας του ξεχωριστού Wiki repository.
.PARAMETER repository
Το GitHub repository σε μορφή owner/name.
.PARAMETER sourceBranch
Το branch στο οποίο παραπέμπουν οι σύνδεσμοι προς τον κώδικα.
#>
param
(
    [Parameter(Mandatory)][string] $sourceDirectory,
    [Parameter(Mandatory)][string] $destinationDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[\w.-]+/[\w.-]+$')][string] $repository,
    [string] $sourceBranch = 'main'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

[string] $sourceRoot = (Resolve-Path -LiteralPath $sourceDirectory).Path
[string] $destinationRoot = (Resolve-Path -LiteralPath $destinationDirectory).Path
[string] $repositoryRoot = Split-Path -Parent $sourceRoot
[string] $sourcePrefix = $sourceRoot + [System.IO.Path]::DirectorySeparatorChar
[string] $destinationPrefix = $destinationRoot + [System.IO.Path]::DirectorySeparatorChar

if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'README.md') -PathType Leaf))
{
    throw 'The source directory must contain README.md.'
}

if (-not (Test-Path -LiteralPath (Join-Path $destinationRoot '.git')) -or $destinationRoot.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -or $destinationRoot.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -or $sourceRoot.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase) -or $destinationRoot.Equals($sourceRoot, [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The destination must be a separate Wiki checkout outside the source directory.'
}

[string] $wikiUrl = "https://github.com/$repository/wiki"
[string] $assetUrl = "https://raw.githubusercontent.com/wiki/$repository"
[string] $sourceUrl = "https://github.com/$repository/blob/$([Uri]::EscapeDataString($sourceBranch))"
[System.Collections.Generic.Dictionary[string, byte[]]] $preparedFiles = [System.Collections.Generic.Dictionary[string, byte[]]]::new()

foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse)
{
    [string] $relativePath = [System.IO.Path]::GetRelativePath($sourceRoot, $sourceFile.FullName).Replace('\', '/')
    [string] $destinationPath = $relativePath
    if ($relativePath -ceq 'README.md')
    {
        $destinationPath = 'Home.md'
    }

    if ($sourceFile.Extension -eq '.md')
    {
        [string] $content = [System.IO.File]::ReadAllText($sourceFile.FullName)
        foreach ($link in [regex]::Matches($content, '\]\((?<target>[^)\r\n]+)\)'))
        {
            [string] $target = $link.Groups['target'].Value
            if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#|//)')
            {
                continue
            }

            [string[]] $parts = $target.Split('#', 2)
            [string] $targetPath = [System.IO.Path]::GetFullPath([Uri]::UnescapeDataString($parts[0]), $sourceFile.DirectoryName)
            if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf))
            {
                throw "Broken link in ${relativePath}: $target"
            }

            [string] $convertedTarget = ''
            if ($targetPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase))
            {
                [string] $wikiPath = [System.IO.Path]::GetRelativePath($sourceRoot, $targetPath).Replace('\', '/')
                if ($wikiPath.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase))
                {
                    $wikiPath = $wikiPath.Substring(0, $wikiPath.Length - 3)
                    if ($wikiPath -ceq 'README')
                    {
                        $wikiPath = 'Home'
                    }
                    $convertedTarget = "$wikiUrl/$([Uri]::EscapeDataString($wikiPath).Replace('%2F', '/'))"
                }
                else
                {
                    $convertedTarget = "$assetUrl/$([Uri]::EscapeDataString($wikiPath).Replace('%2F', '/'))"
                }
            }
            else
            {
                [string] $codePath = [System.IO.Path]::GetRelativePath($repositoryRoot, $targetPath).Replace('\', '/')
                if ($codePath.StartsWith('../') -or [System.IO.Path]::IsPathRooted($codePath))
                {
                    throw "Link outside the repository in ${relativePath}: $target"
                }
                $convertedTarget = "$sourceUrl/$([Uri]::EscapeDataString($codePath).Replace('%2F', '/'))"
            }

            if ($parts.Length -eq 2)
            {
                $convertedTarget += '#' + $parts[1]
            }
            $content = $content.Replace($link.Value, "]($convertedTarget)")
        }
        $preparedFiles.Add($destinationPath, [System.Text.Encoding]::UTF8.GetBytes($content))
    }
    else
    {
        $preparedFiles.Add($destinationPath, [System.IO.File]::ReadAllBytes($sourceFile.FullName))
    }
}

# Διαγράφονται μόνο παλιά αρχεία προηγούμενης δημοσίευσης, όχι ανεξάρτητες σελίδες του Wiki.
[string] $manifestPath = Join-Path $destinationRoot '.wiki-sync-files'
[System.Collections.Generic.List[string]] $obsoleteFiles = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $manifestPath -PathType Leaf)
{
    foreach ($previousPath in [System.IO.File]::ReadAllLines($manifestPath))
    {
        [string] $absolutePath = [System.IO.Path]::GetFullPath($previousPath, $destinationRoot)
        if (-not $absolutePath.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase) -or $previousPath -match '(^|[/\\])\.git([/\\]|$)')
        {
            throw "Invalid path in the Wiki synchronization manifest: $previousPath"
        }
        if (-not $preparedFiles.ContainsKey($previousPath) -and (Test-Path -LiteralPath $absolutePath -PathType Leaf))
        {
            $obsoleteFiles.Add($absolutePath)
        }
    }
}

foreach ($entry in $preparedFiles.GetEnumerator())
{
    [string] $outputPath = Join-Path $destinationRoot $entry.Key
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    [System.IO.File]::WriteAllBytes($outputPath, $entry.Value)
}
foreach ($obsoleteFile in $obsoleteFiles)
{
    Remove-Item -LiteralPath $obsoleteFile
}
[System.IO.File]::WriteAllLines($manifestPath, [string[]] ($preparedFiles.Keys | Sort-Object), [System.Text.UTF8Encoding]::new($false))
Write-Output "Prepared $($preparedFiles.Count) files for $wikiUrl."

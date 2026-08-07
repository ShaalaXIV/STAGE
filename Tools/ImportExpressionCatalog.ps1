param(
    [Parameter(Mandatory = $true)]
    [string]$WorkbookPath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptDirectory '..\Assets\Expressions'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$previewDirectory = Join-Path $output 'Previews'
New-Item -ItemType Directory -Force -Path $previewDirectory | Out-Null

$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($WorkbookPath))
try {
    function Read-ZipText([string]$name) {
        $entry = $zip.GetEntry($name)
        if ($null -eq $entry) { return $null }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    [xml]$sharedXml = Read-ZipText 'xl/sharedStrings.xml'
    $shared = @($sharedXml.sst.si | ForEach-Object {
        if ($null -ne $_.t) { [string]$_.t }
        else { ($_.r | ForEach-Object { [string]$_.t }) -join '' }
    })

    [xml]$sheet = Read-ZipText 'xl/worksheets/sheet1.xml'
    $sheetNs = [Xml.XmlNamespaceManager]::new($sheet.NameTable)
    $sheetNs.AddNamespace('s', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $rows = @{}
    foreach ($row in $sheet.SelectNodes('//s:sheetData/s:row', $sheetNs)) {
        $values = @{}
        foreach ($cell in $row.SelectNodes('s:c', $sheetNs)) {
            $value = if ($cell.t -eq 's') {
                $shared[[int]$cell.v]
            } elseif ($cell.t -eq 'inlineStr') {
                [string]$cell.is.InnerText
            } else {
                [string]$cell.v
            }
            $values[[string]$cell.r] = $value
        }
        $rows[[int]$row.r] = $values
    }

    [xml]$drawing = Read-ZipText 'xl/drawings/drawing1.xml'
    [xml]$relationships = Read-ZipText 'xl/drawings/_rels/drawing1.xml.rels'
    $targets = @{}
    foreach ($relationship in $relationships.Relationships.Relationship) {
        $targets[[string]$relationship.Id] = [IO.Path]::GetFileName([string]$relationship.Target)
    }

    $drawingNs = [Xml.XmlNamespaceManager]::new($drawing.NameTable)
    $drawingNs.AddNamespace('x', 'http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing')
    $drawingNs.AddNamespace('a', 'http://schemas.openxmlformats.org/drawingml/2006/main')
    $byExpression = [ordered]@{}

    foreach ($anchor in $drawing.SelectNodes('//x:twoCellAnchor | //x:oneCellAnchor', $drawingNs)) {
        $rowNumber = [int]$anchor.SelectSingleNode('x:from/x:row', $drawingNs).InnerText + 1
        $values = $rows[$rowNumber]
        if ($null -eq $values) { continue }

        $expression = [string]$values["A$rowNumber"]
        if ([string]::IsNullOrWhiteSpace($expression) -or !$expression.StartsWith('cfxf_')) { continue }

        $papName = [string]$values["B$rowNumber"]
        $blip = $anchor.SelectSingleNode('.//a:blip', $drawingNs)
        $relationshipId = $blip.GetAttribute(
            'embed',
            'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $mediaName = $targets[$relationshipId]
        if ([string]::IsNullOrWhiteSpace($mediaName)) { continue }

        if (!$byExpression.Contains($expression)) {
            $displayName = ($expression -replace '^cfxf_(emot_)?', '' -replace '_', ' ')
            $byExpression[$expression] = [ordered]@{
                key = $expression
                displayName = (Get-Culture).TextInfo.ToTitleCase($displayName)
                papName = $papName
                previews = [Collections.Generic.List[string]]::new()
            }
        }

        $index = $byExpression[$expression].previews.Count + 1
        $extension = [IO.Path]::GetExtension($mediaName).ToLowerInvariant()
        $outputName = "$expression-$index$extension"
        $mediaEntry = $zip.GetEntry("xl/media/$mediaName")
        if ($null -eq $mediaEntry) { continue }
        $destination = Join-Path $previewDirectory $outputName
        $inputStream = $mediaEntry.Open()
        $outputStream = [IO.File]::Create($destination)
        try { $inputStream.CopyTo($outputStream) } finally {
            $outputStream.Dispose()
            $inputStream.Dispose()
        }
        $byExpression[$expression].previews.Add("Previews/$outputName")
    }

    $catalog = [ordered]@{
        source = 'DT Facial Library Expressions'
        sourceUrl = 'https://docs.google.com/spreadsheets/d/1Ddck4jxeEoA5U2HI_jyplsAeOL7XUvYsaN_mqtxI8o4/edit?gid=0#gid=0'
        expressions = @($byExpression.Values)
    }
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $catalog | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'catalog.json') -Encoding UTF8
    Write-Host "Imported $($byExpression.Count) expressions and $(Get-ChildItem $previewDirectory -File | Measure-Object).Count previews."
}
finally {
    $zip.Dispose()
}

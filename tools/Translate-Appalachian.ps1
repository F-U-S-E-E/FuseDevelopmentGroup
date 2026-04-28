[CmdletBinding()]
param(
    [string]$RailwayModPath = 'C:\Steam\steamapps\common\Railroader\Mods\KingG.Appalachian-Railway',
    [string]$TilesModPath = 'C:\Steam\steamapps\common\Railroader\Mods\KingG.Appalachian.MapTiles',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Web.Extensions

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $scriptDirectory = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $PSScriptRoot
    }
    else {
        Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    $OutputRoot = Join-Path (Split-Path -Parent $scriptDirectory) 'translated'
}

function Resolve-AppalachianSourcePath {
    param(
        [string]$PreferredPath,
        [string]$FolderName
    )

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and (Test-Path -LiteralPath $PreferredPath)) {
        return (Resolve-Path -LiteralPath $PreferredPath).Path
    }

    $candidates = @(
        (Join-Path 'C:\Steam\steamapps\common\Railroader\Mods' $FolderName),
        (Join-Path 'C:\Steam\steamapps\common\Railroader\Mods.bck' $FolderName),
        (Join-Path 'C:\Hrogers_Railroader_mods_Projects\GearedSteam\.weatherinspect\Mods' $FolderName)
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Source mod folder not found for $FolderName. Checked: $PreferredPath"
}

$ValidRailIdPattern = '^[A-Za-z0-9][A-Za-z0-9._:-]*$'
$JsonParser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$JsonParser.MaxJsonLength = 67108864

function Read-LegacyJson {
    param([string]$Path)
    return $JsonParser.DeserializeObject((Get-Content -LiteralPath $Path -Raw))
}

function To-PlainJsonValue {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $ordered[[string]$key] = To-PlainJsonValue $Value[$key]
        }
        return $ordered
    }

    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        $items = @()
        foreach ($item in $Value) {
            $items += ,(To-PlainJsonValue $item)
        }
        return $items
    }

    return $Value
}

function Convert-Vec3 {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    return [ordered]@{
        x = [double]$Value['x']
        y = [double]$Value['y']
        z = [double]$Value['z']
    }
}

function Split-WhitespaceIds {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return ($Value -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Resolve-TrackSpanIds {
    param(
        $Value,
        [hashtable]$SpanIdMap
    )

    $resolved = @()
    if ($null -eq $Value) {
        return $resolved
    }

    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        foreach ($item in $Value) {
            $rawId = [string]$item
            if ($SpanIdMap.ContainsKey($rawId)) {
                $resolved += $SpanIdMap[$rawId]
            }
        }

        return $resolved
    }

    foreach ($rawId in (Split-WhitespaceIds ([string]$Value))) {
        if ($SpanIdMap.ContainsKey($rawId)) {
            $resolved += $SpanIdMap[$rawId]
        }
    }

    return $resolved
}

function Map-LoadId {
    param(
        [string]$LoadId,
        [hashtable]$LoadIdMap
    )

    if ([string]::IsNullOrWhiteSpace($LoadId)) {
        return $LoadId
    }

    if ($LoadIdMap.ContainsKey($LoadId)) {
        return $LoadIdMap[$LoadId]
    }

    return $LoadId
}

function Normalize-TurntablePrefabUri {
    param(
        [string]$Value,
        [string]$DefaultValue
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $DefaultValue
    }

    if ($Value -eq 'vanilla') {
        return $DefaultValue
    }

    return $Value
}

function New-UsedIdSet {
    return New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
}

function Convert-ToRailId {
    param(
        [string]$Source,
        [string]$Prefix,
        [System.Collections.Generic.HashSet[string]]$UsedIds
    )

    if ([string]::IsNullOrWhiteSpace($Source)) {
        $Source = 'item'
    }

    if ($Source -match $ValidRailIdPattern) {
        $candidate = $Source
    }
    else {
        $slug = $Source.ToLowerInvariant()
        $slug = [regex]::Replace($slug, '[^A-Za-z0-9._:-]+', '-')
        $slug = $slug.Trim('-')
        if ([string]::IsNullOrWhiteSpace($slug)) {
            $slug = 'item'
        }
        if ($slug -notmatch '^[A-Za-z0-9]') {
            $slug = 'id-' + $slug
        }
        $candidate = $Prefix + '.' + $slug
    }

    $unique = $candidate
    $suffix = 2
    while ($UsedIds.Contains($unique)) {
        $unique = $candidate + '.' + $suffix
        $suffix++
    }

    [void]$UsedIds.Add($unique)
    return $unique
}

function Merge-LegacyRoot {
    param(
        [hashtable]$Target,
        [hashtable]$Source,
        [string[]]$Keys
    )

    foreach ($key in $Keys) {
        if ($Source.ContainsKey($key) -and $Source[$key] -is [System.Collections.IDictionary]) {
            foreach ($entryKey in $Source[$key].Keys) {
                $Target[$key][$entryKey] = $Source[$key][$entryKey]
            }
        }
    }
}

function Get-SegmentEndpoints {
    param($Segment)

    $startId = if ($Segment.ContainsKey('startId')) { [string]$Segment['startId'] } else { [string]$Segment['startNodeId'] }
    $endId = if ($Segment.ContainsKey('endId')) { [string]$Segment['endId'] } else { [string]$Segment['endNodeId'] }
    return @($startId, $endId)
}

function Resolve-MappedId {
    param(
        [string]$RawId,
        [hashtable]$IdMap
    )

    if ([string]::IsNullOrWhiteSpace($RawId)) {
        return $RawId
    }

    if ($null -ne $IdMap -and $IdMap.ContainsKey($RawId)) {
        $mappedId = [string]$IdMap[$RawId]
        if (-not [string]::IsNullOrWhiteSpace($mappedId)) {
            return $mappedId
        }
    }

    return $RawId
}

function Get-SegmentLength {
    param(
        [hashtable]$Nodes,
        $Segment
    )

    $endpointIds = Get-SegmentEndpoints $Segment
    $startNode = $Nodes[$endpointIds[0]]
    $endNode = $Nodes[$endpointIds[1]]
    if ($null -eq $startNode -or $null -eq $endNode) {
        return 0.0
    }

    $start = $startNode['position']
    $end = $endNode['position']
    $dx = [double]$end['x'] - [double]$start['x']
    $dy = [double]$end['y'] - [double]$start['y']
    $dz = [double]$end['z'] - [double]$start['z']
    return [Math]::Sqrt(($dx * $dx) + ($dy * $dy) + ($dz * $dz))
}

function Convert-TrackLocation {
    param(
        $Location,
        [hashtable]$SegmentIdMap
    )

    $rawSegmentId = [string]$Location['segmentId']
    $distance = [double]$Location['distance']
    $locationEnd = if ($Location.ContainsKey('end') -and -not [string]::IsNullOrWhiteSpace([string]$Location['end'])) {
        switch ([string]$Location['end']) {
            'End' { 'B' }
            'B' { 'B' }
            default { 'A' }
        }
    }
    else {
        $null
    }

    $converted = [ordered]@{
        segmentId = Resolve-MappedId -RawId $rawSegmentId -IdMap $SegmentIdMap
        distance = [Math]::Round($distance, 6)
    }

    if (-not [string]::IsNullOrWhiteSpace($locationEnd)) {
        $converted['end'] = $locationEnd
    }

    return $converted
}

function New-EmptyRailDefinition {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Author,
        [string]$Version,
        [string]$Description
    )

    return [ordered]@{
        '$schema' = '.\schemas\rail-mod.schema.json'
        schemaVersion = 1
        id = $Id
        name = $Name
        author = $Author
        modVersion = $Version
        description = $Description
        coordinateSpace = 'world'
        tracks = [ordered]@{
            nodes = [ordered]@{}
            segments = [ordered]@{}
            spans = [ordered]@{}
            areas = [ordered]@{}
            removals = [ordered]@{
                nodes = @()
                segments = @()
                spans = @()
            }
        }
        operations = [ordered]@{
            loads = [ordered]@{}
            industries = [ordered]@{}
            loaders = [ordered]@{}
            turntables = [ordered]@{}
            stations = [ordered]@{}
        }
        world = [ordered]@{
            scenery = [ordered]@{}
            splineys = [ordered]@{}
            telegraphPoles = [ordered]@{}
            mapLabels = [ordered]@{}
            mapMasks = [ordered]@{}
            mapTiles = [ordered]@{}
            sceneClones = [ordered]@{}
        }
        progression = [ordered]@{
            progressions = [ordered]@{}
            mapFeatures = [ordered]@{}
        }
        editor = $null
        extensions = [ordered]@{}
    }
}

function Write-JsonFile {
    param(
        [string]$Path,
        $Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.Encoding]::UTF8)
}

$RailwayModPath = Resolve-AppalachianSourcePath -PreferredPath $RailwayModPath -FolderName 'KingG.Appalachian-Railway'
$TilesModPath = Resolve-AppalachianSourcePath -PreferredPath $TilesModPath -FolderName 'KingG.Appalachian.MapTiles'

$railwayDefinition = Read-LegacyJson (Join-Path $RailwayModPath 'Definition.json')
$tilesDefinition = Read-LegacyJson (Join-Path $TilesModPath 'Definition.json')

$graphFiles = @(
    'KG.BRANCH-game-graph.json',
    'Geep-Game-Graph.json',
    'Bacon-Game-Graph.json',
    'Copper-Game-Graph.json',
    'roads.json',
    'scenery.json',
    'splineys.json',
    'rivers.json',
    'mandelas.json',
    'loads.json',
    'industry.json'
)

$merged = @{
    tracks = @{
        nodes = @{}
        segments = @{}
        spans = @{}
    }
    areas = @{}
    loads = @{}
    texts = @{}
    scenery = @{}
    splineys = @{}
    simpleGraphs = @{}
    mandelas = @{}
}

foreach ($file in $graphFiles) {
    $path = Join-Path $RailwayModPath $file
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    $json = Read-LegacyJson $path
    Merge-LegacyRoot -Target $merged -Source $json -Keys @('areas', 'loads', 'texts', 'scenery', 'splineys', 'simpleGraphs', 'mandelas')
    if ($json.ContainsKey('tracks')) {
        Merge-LegacyRoot -Target $merged['tracks'] -Source $json['tracks'] -Keys @('nodes', 'segments', 'spans')
    }
}

$railId = 'KingG.Appalachian-Railway.RAIL'
$railName = 'Appalachian Railway (RAIL Translation)'
$railDescription = 'Automated RAIL translation of KingG.Appalachian-Railway. Unsupported legacy constructs are preserved in extensions.'
$rail = New-EmptyRailDefinition -Id $railId -Name $railName -Author 'KingG' -Version ([string]$railwayDefinition['version']) -Description $railDescription

$nodeIds = @{}
$segmentIds = @{}
$spanIds = @{}
$loadIds = @{}
$industryIds = @{}
$componentIds = @{}
$splineyIds = @{}
$sceneryIds = @{}
$loaderIds = @{}
$labelIds = @{}
$stationIds = @{}
$sceneCloneIds = @{}

$nodeUsed = New-UsedIdSet
$segmentUsed = New-UsedIdSet
$spanUsed = New-UsedIdSet
$loadUsed = New-UsedIdSet
$industryUsed = New-UsedIdSet
$componentUsed = New-UsedIdSet
$splineyUsed = New-UsedIdSet
$sceneryUsed = New-UsedIdSet
$loaderUsed = New-UsedIdSet
$labelUsed = New-UsedIdSet
$stationUsed = New-UsedIdSet
$sceneCloneUsed = New-UsedIdSet

$nullNodeCount = 0
foreach ($rawId in ($merged['tracks']['nodes'].Keys | Sort-Object)) {
    $node = $merged['tracks']['nodes'][$rawId]
    if ($null -eq $node) {
        $nullNodeCount++
        $rail['tracks']['removals']['nodes'] += $rawId
        continue
    }

    $nodeIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.track.node' -UsedIds $nodeUsed
}

$nullSegmentCount = 0
$speedLimitZeroCount = 0
foreach ($rawId in ($merged['tracks']['segments'].Keys | Sort-Object)) {
    $segment = $merged['tracks']['segments'][$rawId]
    if ($null -eq $segment) {
        $nullSegmentCount++
        $rail['tracks']['removals']['segments'] += $rawId
        continue
    }

    if ([int]$segment['speedLimit'] -eq 0) {
        $speedLimitZeroCount++
    }

    $segmentIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.track.segment' -UsedIds $segmentUsed
}

$nullSpanCount = 0
foreach ($rawId in ($merged['tracks']['spans'].Keys | Sort-Object)) {
    $span = $merged['tracks']['spans'][$rawId]
    if ($null -eq $span) {
        $nullSpanCount++
        $rail['tracks']['removals']['spans'] += $rawId
        continue
    }

    $spanIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.track.span' -UsedIds $spanUsed
}

foreach ($rawId in ($merged['loads'].Keys | Sort-Object)) {
    $load = $merged['loads'][$rawId]
    if ($null -eq $load) {
        continue
    }

    $loadIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.load' -UsedIds $loadUsed
}

foreach ($rawId in ($merged['areas'].Keys | Sort-Object)) {
    $area = $merged['areas'][$rawId]
    if ($null -eq $area -or -not ($area.ContainsKey('industries'))) {
        continue
    }

    foreach ($industryRawId in ($area['industries'].Keys | Sort-Object)) {
        if (-not $industryIds.ContainsKey($industryRawId)) {
            $industryIds[$industryRawId] = Convert-ToRailId -Source $industryRawId -Prefix 'kg.appalachian.industry' -UsedIds $industryUsed
        }

        $industry = $area['industries'][$industryRawId]
        if ($null -eq $industry -or -not ($industry.ContainsKey('components'))) {
            continue
        }

        $componentIds[$industryRawId] = @{}
        foreach ($componentRawId in ($industry['components'].Keys | Sort-Object)) {
            $componentIds[$industryRawId][$componentRawId] = Convert-ToRailId -Source $componentRawId -Prefix ($industryIds[$industryRawId] + '.component') -UsedIds $componentUsed
        }
    }
}

foreach ($rawId in ($merged['scenery'].Keys | Sort-Object)) {
    if ($null -ne $merged['scenery'][$rawId]) {
        $sceneryIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.scenery' -UsedIds $sceneryUsed
    }
}

foreach ($rawId in ($merged['mandelas'].Keys | Sort-Object)) {
    if ($null -ne $merged['mandelas'][$rawId]) {
        $sceneCloneIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.sceneClone' -UsedIds $sceneCloneUsed
    }
}

foreach ($rawId in ($merged['splineys'].Keys | Sort-Object)) {
    $item = $merged['splineys'][$rawId]
    if ($null -eq $item) {
        continue
    }

    $handler = [string]$item['handler']
    switch ($handler) {
        'AlinasMapMod.Loaders.LoaderBuilder' {
            $loaderIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.loader' -UsedIds $loaderUsed
        }
        'AlinasMapMod.MapLabelBuilder' {
            $labelIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.mapLabel' -UsedIds $labelUsed
        }
        'AlinasMapMod.Stations.StationAgentBuilder' {
            $stationIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.station' -UsedIds $stationUsed
        }
        'AlinasMapMod.Turntable.TurntableBuilder' {
            continue
        }
        default {
            $splineyIds[$rawId] = Convert-ToRailId -Source $rawId -Prefix 'kg.appalachian.spliney' -UsedIds $splineyUsed
        }
    }
}

foreach ($rawId in ($nodeIds.Keys | Sort-Object)) {
    $node = $merged['tracks']['nodes'][$rawId]
    $rail['tracks']['nodes'][$nodeIds[$rawId]] = [ordered]@{
        position = Convert-Vec3 $node['position']
        rotation = Convert-Vec3 $node['rotation']
        flipSwitchStand = [bool]$node['flipSwitchStand']
    }
}

foreach ($rawId in ($segmentIds.Keys | Sort-Object)) {
    $segment = $merged['tracks']['segments'][$rawId]
    $endpointIds = Get-SegmentEndpoints $segment
    $rail['tracks']['segments'][$segmentIds[$rawId]] = [ordered]@{
        startNodeId = Resolve-MappedId -RawId $endpointIds[0] -IdMap $nodeIds
        endNodeId = Resolve-MappedId -RawId $endpointIds[1] -IdMap $nodeIds
        style = [string]$segment['Style']
        trackClass = [string]$segment['trackClass']
        speedLimit = [int]$segment['speedLimit']
        priority = [int]$segment['priority']
    }

    if ($segment.ContainsKey('groupId') -and -not [string]::IsNullOrWhiteSpace([string]$segment['groupId'])) {
        $rail['tracks']['segments'][$segmentIds[$rawId]]['groupId'] = [string]$segment['groupId']
    }
}

foreach ($rawId in ($spanIds.Keys | Sort-Object)) {
    $span = $merged['tracks']['spans'][$rawId]
    $rail['tracks']['spans'][$spanIds[$rawId]] = [ordered]@{
        upper = Convert-TrackLocation -Location $span['upper'] -SegmentIdMap $segmentIds
        lower = Convert-TrackLocation -Location $span['lower'] -SegmentIdMap $segmentIds
        normalize = $true
    }
}

$industryAreaMap = [ordered]@{}
$unsupportedComponents = [ordered]@{}

$areaOrder = 0
foreach ($areaRawId in ($merged['areas'].Keys | Sort-Object)) {
    $area = $merged['areas'][$areaRawId]
    if ($null -eq $area -or -not ($area.ContainsKey('industries'))) {
        continue
    }

    $railArea = [ordered]@{
        name = if ($area.ContainsKey('name')) { [string]$area['name'] } else { $areaRawId }
        order = $areaOrder
    }
    if ($area.ContainsKey('position')) {
        $railArea['position'] = Convert-Vec3 $area['position']
    }
    if ($area.ContainsKey('radius')) {
        $railArea['radius'] = [double]$area['radius']
    }
    if ($area.ContainsKey('tagColor')) {
        $railArea['tagColor'] = @($area['tagColor'])
    }
    $rail['tracks']['areas'][$areaRawId] = $railArea

    $industryOrder = 0
    foreach ($industryRawId in ($area['industries'].Keys | Sort-Object)) {
        $industry = $area['industries'][$industryRawId]
        if ($null -eq $industry) {
            continue
        }

        $industryId = $industryIds[$industryRawId]
        $industryAreaMap[$industryId] = [ordered]@{
            areaId = $areaRawId
            areaName = if ($area.ContainsKey('name')) { [string]$area['name'] } else { $areaRawId }
        }

        $railIndustry = [ordered]@{
            name = if ($industry.ContainsKey('name')) { [string]$industry['name'] } else { $industryId }
            areaId = $areaRawId
            order = $industryOrder
            position = Convert-Vec3 $industry['localPosition']
            rotation = if ($industry.ContainsKey('rotation')) { Convert-Vec3 $industry['rotation'] } else { [ordered]@{ x = 0.0; y = 0.0; z = 0.0 } }
            usesContract = if ($industry.ContainsKey('usesContract')) { [bool]$industry['usesContract'] } else { $false }
            components = [ordered]@{}
        }

        foreach ($componentRawId in ($industry['components'].Keys | Sort-Object)) {
            $component = $industry['components'][$componentRawId]
            if ($null -eq $component) {
                continue
            }

            $componentId = $componentIds[$industryRawId][$componentRawId]
            $componentType = [string]$component['type']
            $railComponentType = switch ($componentType) {
                'Model.Ops.IndustryLoader' { 'loader' }
                'Model.Ops.IndustryUnloader' { 'unloader' }
                'Model.Ops.FormulaicIndustryComponent' { 'formulaic' }
                'Model.Ops.RepairTrack' { 'repairTrack' }
                'Model.Ops.TeamTrack' { 'teamTrack' }
                'Model.Ops.Interchange' { 'interchange' }
                'Model.Ops.InterchangedIndustryLoader' { 'interchangedLoader' }
                'AlinasMapMod.PaxStationComponent' { 'passengerStop' }
                default { 'custom' }
            }

            $trackSpanIds = @(Resolve-TrackSpanIds -Value $component['trackSpans'] -SpanIdMap $spanIds)

            $railComponent = [ordered]@{
                type = $railComponentType
                name = [string]$component['name']
                trackSpanIds = $trackSpanIds
                carTypeFilter = if ($component.ContainsKey('carTypeFilter')) { [string]$component['carTypeFilter'] } else { '' }
                loadId = if ($component.ContainsKey('loadId')) { Map-LoadId -LoadId ([string]$component['loadId']) -LoadIdMap $loadIds } else { '' }
                sharedStorage = if ($component.ContainsKey('sharedStorage')) { [bool]$component['sharedStorage'] } else { $true }
            }

            if ($component.ContainsKey('storageChangeRate')) { $railComponent['storageChangeRate'] = [double]$component['storageChangeRate'] }
            if ($component.ContainsKey('maxStorage')) { $railComponent['maxStorage'] = [double]$component['maxStorage'] }
            if ($component.ContainsKey('carTransferRate')) { $railComponent['carTransferRate'] = [double]$component['carTransferRate'] }
            if ($component.ContainsKey('orderAroundEmpties')) { $railComponent['orderAroundEmpties'] = [bool]$component['orderAroundEmpties'] }
            if ($component.ContainsKey('orderAroundLoaded')) { $railComponent['orderAroundLoaded'] = [bool]$component['orderAroundLoaded'] }

            switch ($railComponentType) {
                'passengerStop' {
                    $railComponent['passengerStopId'] = $componentId
                    $railComponent['timetableCode'] = [string]$component['timetableCode']
                    $railComponent['basePopulation'] = if ($component.ContainsKey('basePopulation')) { [int]$component['basePopulation'] } else { 40 }
                    $railComponent['neighborIds'] = if ($component.ContainsKey('neighborIds')) { @($component['neighborIds']) } else { @() }
                    if ($component.ContainsKey('branch') -and -not [string]::IsNullOrWhiteSpace([string]$component['branch'])) {
                        $railComponent['branch'] = [string]$component['branch']
                    }

                    if ($component.ContainsKey('branches') -and $component['branches']) {
                        $branchDefinitions = @()
                        foreach ($branchDefinition in $component['branches']) {
                            $branchObject = [ordered]@{
                                branch = [string]$branchDefinition['Branch']
                                traverseTimeToNext = if ($branchDefinition.ContainsKey('TraverseTimeToNext')) { [int]$branchDefinition['TraverseTimeToNext'] } else { 0 }
                            }

                            if ($branchDefinition.ContainsKey('MapFeature') -and -not [string]::IsNullOrWhiteSpace([string]$branchDefinition['MapFeature'])) {
                                $branchObject['mapFeature'] = [string]$branchDefinition['MapFeature']
                            }

                            if ($branchDefinition.ContainsKey('Intermediates') -and $branchDefinition['Intermediates']) {
                                $intermediates = [ordered]@{}
                                foreach ($intermediateName in $branchDefinition['Intermediates'].Keys) {
                                    $intermediate = $branchDefinition['Intermediates'][$intermediateName]
                                    $intermediates[[string]$intermediateName] = [ordered]@{
                                        code = [string]$intermediate['Code']
                                        traverseTimeToNext = if ($intermediate.ContainsKey('TraverseTimeToNext')) { [int]$intermediate['TraverseTimeToNext'] } else { 0 }
                                    }
                                }

                                $branchObject['intermediates'] = $intermediates
                            }

                            $branchDefinitions += ,$branchObject
                        }

                        $railComponent['branchDefinitions'] = $branchDefinitions
                    }
                }
                'formulaic' {
                    $inputTerms = [ordered]@{}
                    if ($component.ContainsKey('inputTermsPerDay') -and $component['inputTermsPerDay']) {
                        foreach ($loadRawId in $component['inputTermsPerDay'].Keys) {
                            $inputTerms[(Map-LoadId -LoadId ([string]$loadRawId) -LoadIdMap $loadIds)] = [double]$component['inputTermsPerDay'][$loadRawId]
                        }
                    }

                    $outputTerms = [ordered]@{}
                    if ($component.ContainsKey('outputTermsPerDay') -and $component['outputTermsPerDay']) {
                        foreach ($loadRawId in $component['outputTermsPerDay'].Keys) {
                            $outputTerms[(Map-LoadId -LoadId ([string]$loadRawId) -LoadIdMap $loadIds)] = [double]$component['outputTermsPerDay'][$loadRawId]
                        }
                    }

                    $railComponent['inputTermsPerDay'] = $inputTerms
                    $railComponent['outputTermsPerDay'] = $outputTerms
                }
                'repairTrack' {
                    if ($component.ContainsKey('canOverhaul')) {
                        $railComponent['canOverhaul'] = [bool]$component['canOverhaul']
                    }
                }
                'teamTrack' {
                    if ($component.ContainsKey('idealCars')) {
                        $railComponent['idealCars'] = [double]$component['idealCars']
                    }

                    $profiles = [ordered]@{}
                    if ($component.ContainsKey('teamProfiles') -and $component['teamProfiles']) {
                        foreach ($profileKey in ($component['teamProfiles'].Keys | Sort-Object)) {
                            $profile = $component['teamProfiles'][$profileKey]
                            $profiles[[string]$profileKey] = [ordered]@{
                                isExport = [bool]$profile['isExport']
                                loadId = Map-LoadId -LoadId ([string]$profile['loadId']) -LoadIdMap $loadIds
                                loadingTimeDays = [double]$profile['loadingTimeDays']
                                carTypeFilter = [string]$profile['carTypeFilter']
                            }
                        }
                    }

                    $railComponent['teamProfiles'] = $profiles
                }
            }

            $railIndustry['components'][$componentId] = $railComponent

            if ($railComponentType -eq 'custom') {
                $unsupportedComponents[$industryId + '.' + $componentId] = [ordered]@{
                    originalType = $componentType
                    areaId = $areaRawId
                    data = To-PlainJsonValue $component
                }
            }
        }

        $rail['operations']['industries'][$industryId] = $railIndustry
        $industryOrder++
    }

    $areaOrder++
}

foreach ($rawId in ($loadIds.Keys | Sort-Object)) {
    $load = $merged['loads'][$rawId]
    $rail['operations']['loads'][$loadIds[$rawId]] = [ordered]@{
        name = [string]$load['description']
        units = if ($load.ContainsKey('units')) { [string]$load['units'] } else { 'Pounds' }
        density = if ($load.ContainsKey('density')) { [double]$load['density'] } else { 62.4 }
        unitWeightInPounds = if ($load.ContainsKey('unitWeightInPounds')) { [double]$load['unitWeightInPounds'] } else { 0.0 }
        importable = if ($load.ContainsKey('importable')) { [bool]$load['importable'] } else { $true }
        payPerQuantity = if ($load.ContainsKey('payPerQuantity')) { [double]$load['payPerQuantity'] } else { 0.0 }
        costPerUnit = if ($load.ContainsKey('costPerUnit')) { [double]$load['costPerUnit'] } else { 0.0 }
    }
}

foreach ($rawId in ($merged['splineys'].Keys | Sort-Object)) {
    $item = $merged['splineys'][$rawId]
    if ($null -eq $item) {
        continue
    }

    $handler = [string]$item['handler']
    switch ($handler) {
        'StrangeCustoms.FlowyThingBuilder' {
            $points = @()
            foreach ($point in $item['points']) {
                $pointObject = [ordered]@{
                    position = Convert-Vec3 $point['position']
                }
                if ($point.ContainsKey('rotation')) {
                    $pointObject['rotation'] = Convert-Vec3 $point['rotation']
                }
                if ($point.ContainsKey('width')) {
                    $pointObject['width'] = [double]$point['width']
                }
                $points += ,$pointObject
            }

            $type = if ([string]$item['style'] -match 'river') { 'river' } else { 'road' }
            $rail['world']['splineys'][$splineyIds[$rawId]] = [ordered]@{
                type = $type
                profile = [string]$item['profile']
                style = [string]$item['style']
                points = $points
            }
        }
        'StrangeCustoms.AutoTrestleBuilder' {
            $points = @()
            foreach ($point in $item['points']) {
                $pointObject = [ordered]@{
                    position = Convert-Vec3 $point['position']
                }
                if ($point.ContainsKey('rotation')) {
                    $pointObject['rotation'] = Convert-Vec3 $point['rotation']
                }
                $points += ,$pointObject
            }

            $rail['world']['splineys'][$splineyIds[$rawId]] = [ordered]@{
                type = 'trestle'
                headStyle = if ($item.ContainsKey('headstyle')) { [string]$item['headstyle'] } else { [string]$item['headStyle'] }
                tailStyle = if ($item.ContainsKey('tailstyle')) { [string]$item['tailstyle'] } else { [string]$item['tailStyle'] }
                points = $points
            }
        }
        'AlinasMapMod.Loaders.LoaderBuilder' {
            $loaderId = $loaderIds[$rawId]
            $industryRawId = [string]$item['industry']
            $rail['operations']['loaders'][$loaderId] = [ordered]@{
                position = Convert-Vec3 $item['position']
                rotation = Convert-Vec3 $item['rotation']
                prefab = [string]$item['prefab']
                industryId = if ($industryIds.ContainsKey($industryRawId)) { $industryIds[$industryRawId] } else { $industryRawId }
            }
        }
        'AlinasMapMod.MapLabelBuilder' {
            $labelText = [string]$item['text']
            $railLabel = [ordered]@{
                text = $labelText
                position = Convert-Vec3 $item['position']
            }

            if ($labelText -match '^\s*(\d{1,3})\s*MPH\.?\s*$') {
                $railLabel['text'] = [string][int]$Matches[1]
                $railLabel['style'] = 'speedLimit'
                $railLabel['speedLimitMph'] = [int]$Matches[1]
            }

            $rail['world']['mapLabels'][$labelIds[$rawId]] = $railLabel
        }
        'AlinasMapMod.Stations.StationAgentBuilder' {
            $rail['operations']['stations'][$stationIds[$rawId]] = [ordered]@{
                position = Convert-Vec3 $item['position']
                rotation = Convert-Vec3 $item['rotation']
                prefab = [string]$item['prefab']
                passengerStopId = [string]$item['passengerStop']
            }
        }
        'AlinasMapMod.Turntable.TurntableBuilder' {
            $roundhouseStalls = if ($item.ContainsKey('roundhouseStalls')) { [int]$item['roundhouseStalls'] } else { 0 }
            $turntable = [ordered]@{
                position = Convert-Vec3 $item['position']
                rotation = Convert-Vec3 $item['rotation']
                radius = if ($item.ContainsKey('radius')) { [double]$item['radius'] } else { 15.0 }
                subdivisions = if ($item.ContainsKey('subdivisions')) { [int]$item['subdivisions'] } else { 32 }
                legacyIdentifier = [string]$rawId
            }

            if ($roundhouseStalls -gt 0) {
                $turntable['roundhouse'] = [ordered]@{
                    stalls = $roundhouseStalls
                    trackLength = if ($item.ContainsKey('roundhouseTrackLength')) { [double]$item['roundhouseTrackLength'] } else { 46.0 }
                    startPrefab = Normalize-TurntablePrefabUri -Value ([string]$item['startPrefab']) -DefaultValue 'vanilla://roundhouseStart'
                    endPrefab = Normalize-TurntablePrefabUri -Value ([string]$item['endPrefab']) -DefaultValue 'vanilla://roundhouseEnd'
                    stallPrefab = Normalize-TurntablePrefabUri -Value ([string]$item['stallPrefab']) -DefaultValue 'vanilla://roundhouseStall'
                }
            }

            $rail['operations']['turntables'][$rawId] = $turntable
        }
    }
}

foreach ($rawId in ($sceneryIds.Keys | Sort-Object)) {
    $item = $merged['scenery'][$rawId]
    $rail['world']['scenery'][$sceneryIds[$rawId]] = [ordered]@{
        model = 'scenery://' + [string]$item['modelIdentifier']
        position = Convert-Vec3 $item['position']
        rotation = Convert-Vec3 $item['rotation']
        scale = Convert-Vec3 $item['scale']
    }
}

foreach ($rawId in ($sceneCloneIds.Keys | Sort-Object)) {
    $item = $merged['mandelas'][$rawId]
    $sceneClone = [ordered]@{
        targetPath = [string]$rawId
        localPosition = Convert-Vec3 $item['localPosition']
        localRotation = Convert-Vec3 $item['localRotation']
        localScale = Convert-Vec3 $item['localScale']
    }

    if ($item.ContainsKey('instantiateFrom') -and -not [string]::IsNullOrWhiteSpace([string]$item['instantiateFrom'])) {
        $sceneClone['source'] = 'path://scene/' + [string]$item['instantiateFrom']
    }

    if ($item.ContainsKey('enabled')) {
        $sceneClone['enabled'] = [bool]$item['enabled']
    }

    $rail['world']['sceneClones'][$sceneCloneIds[$rawId]] = $sceneClone
}

$rail['extensions']['dev.hunterr.translation'] = [ordered]@{
    sourceModId = [string]$railwayDefinition['id']
    sourceTilesModId = [string]$tilesDefinition['id']
    generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    notes = @(
        'Legacy passenger stop components were translated into native RAIL passengerStop components.',
        'Legacy turntable builders were translated into operations.turntables with legacy track identifiers so generated roundhouse helpers still line up.',
        'Strange Customs mandelas were translated into world.sceneClones so their hierarchy-local transforms survive the migration.',
        'Legacy null track entries are preserved as tracks.removals so base-game nodes, segments, and spans can be deleted at load time.',
        'Any remaining unsupported components are preserved here instead of being silently dropped.'
    )
    counts = [ordered]@{
        translatedTrackNodes = $rail['tracks']['nodes'].Count
        translatedTrackSegments = $rail['tracks']['segments'].Count
        translatedTrackSpans = $rail['tracks']['spans'].Count
        trackNodeRemovals = $rail['tracks']['removals']['nodes'].Count
        trackSegmentRemovals = $rail['tracks']['removals']['segments'].Count
        trackSpanRemovals = $rail['tracks']['removals']['spans'].Count
        translatedIndustries = $rail['operations']['industries'].Count
        translatedIndustryLoads = $rail['operations']['loads'].Count
        translatedWorldScenery = $rail['world']['scenery'].Count
        translatedWorldSplineys = $rail['world']['splineys'].Count
        translatedLoaders = $rail['operations']['loaders'].Count
        translatedMapLabels = $rail['world']['mapLabels'].Count
        translatedStations = $rail['operations']['stations'].Count
        translatedTurntables = $rail['operations']['turntables'].Count
        translatedSceneClones = $rail['world']['sceneClones'].Count
        preservedUnsupportedComponents = $unsupportedComponents.Count
        filteredNullTrackNodes = $nullNodeCount
        filteredNullTrackSegments = $nullSegmentCount
        filteredNullTrackSpans = $nullSpanCount
        preservedZeroSpeedSegments = $speedLimitZeroCount
    }
    industryAreas = $industryAreaMap
    unsupportedIndustryComponents = $unsupportedComponents
}

$tilesRail = New-EmptyRailDefinition -Id 'KingG.Appalachian.MapTiles.RAIL' -Name 'Appalachian Map Tiles (RAIL Translation)' -Author 'KingG' -Version ([string]$tilesDefinition['version']) -Description 'RAIL tile overlay package that points at the original KingG Appalachian map tile folder.'
$tilesRail['world']['mapTiles']['kingg.appalachian.tiles'] = [ordered]@{
    directory = 'BushnellWhittier'
    sourceFolder = 'Maps/BushnellWhittier'
    priority = 100
}
$tilesRail['extensions']['dev.hunterr.translation'] = [ordered]@{
    sourceModId = [string]$tilesDefinition['id']
    generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    tileCount = (Get-ChildItem -LiteralPath (Join-Path $TilesModPath 'Maps\BushnellWhittier') -Filter '*.data' | Measure-Object).Count
    notes = @(
        'This translation copies BushnellWhittier tile data into the RAIL package so the package is self-contained.',
        'The sourceFolder is relative to the package root for direct in-game deployment.'
    )
}

$railPackageFolder = Join-Path $OutputRoot 'KingG.Appalachian-Railway.RAIL'
$tilePackageFolder = Join-Path $OutputRoot 'KingG.Appalachian.MapTiles.RAIL'
$railDataFile = 'KingG.Appalachian-Railway.rail.json'
$tileDataFile = 'KingG.Appalachian.MapTiles.rail.json'

$railInfo = [ordered]@{
    '$schema' = '.\schemas\umm-info.schema.json'
    Id = 'KingG.Appalachian-Railway.RAIL'
    DisplayName = 'Appalachian Railway (RAIL Translation)'
    Author = 'KingG'
    Version = [string]$railwayDefinition['version']
    ManagerVersion = '0.27.10'
    Requirements = @('RAIL')
    LoadAfter = @('RAIL')
    RailDataFile = $railDataFile
}

$tileInfo = [ordered]@{
    '$schema' = '.\schemas\umm-info.schema.json'
    Id = 'KingG.Appalachian.MapTiles.RAIL'
    DisplayName = 'Appalachian Map Tiles (RAIL Translation)'
    Author = 'KingG'
    Version = [string]$tilesDefinition['version']
    ManagerVersion = '0.27.10'
    Requirements = @('RAIL')
    LoadAfter = @('RAIL')
    RailDataFile = $tileDataFile
}

Write-JsonFile -Path (Join-Path $railPackageFolder 'Info.json') -Value $railInfo
Write-JsonFile -Path (Join-Path $railPackageFolder $railDataFile) -Value $rail
Write-JsonFile -Path (Join-Path $tilePackageFolder 'Info.json') -Value $tileInfo
Write-JsonFile -Path (Join-Path $tilePackageFolder $tileDataFile) -Value $tilesRail

$packagedTileFolder = Join-Path $tilePackageFolder 'Maps\BushnellWhittier'
New-Item -ItemType Directory -Force -Path $packagedTileFolder | Out-Null
Get-ChildItem -LiteralPath (Join-Path $TilesModPath 'Maps\BushnellWhittier') -File | Copy-Item -Destination $packagedTileFolder -Force

Write-Host "Translated railway package: $railPackageFolder"
Write-Host "Translated tile package: $tilePackageFolder"
Write-Host "Track nodes: $($rail['tracks']['nodes'].Count)"
Write-Host "Track segments: $($rail['tracks']['segments'].Count)"
Write-Host "Track spans: $($rail['tracks']['spans'].Count)"
Write-Host "Industries: $($rail['operations']['industries'].Count)"
Write-Host "Turntables: $($rail['operations']['turntables'].Count)"
Write-Host "Scene clones: $($rail['world']['sceneClones'].Count)"
Write-Host "Industry components preserved in extensions: $($unsupportedComponents.Count)"
Write-Host "Tile overlays: $($tilesRail['world']['mapTiles'].Count)"

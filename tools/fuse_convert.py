#!/usr/bin/env python3
import argparse
import json
import re
import sys
from pathlib import Path

import legacy_json

FUSE_SCHEMA_VERSION = "1.0"

# Conversion-time validation messages collected per-run.
# fuse_converter.py drains these between conversions and folds them into the
# ConversionReport. Strictly additive - readers/writers must never assume
# legacy data is well-formed; we only annotate, never block.
_PENDING_WARNINGS: list[dict] = []
_GROUP_IDS_REFERENCED: set[str] = set()


def reset_validation_state() -> None:
    _PENDING_WARNINGS.clear()
    _GROUP_IDS_REFERENCED.clear()


def drain_validation_warnings() -> list[dict]:
    drained = list(_PENDING_WARNINGS)
    _PENDING_WARNINGS.clear()
    return drained


def referenced_group_ids() -> set[str]:
    return set(_GROUP_IDS_REFERENCED)


def _warn(message: str, *, file: str = "", concept: str = "") -> None:
    _PENDING_WARNINGS.append({
        "level": "WARN",
        "message": message,
        "file": file,
        "concept": concept,
    })

HANDLER_MAP = {
    "StrangeCustoms.FlowyThingBuilder": "road",
    "StrangeCustoms.AutoTrestleBuilder": "trestle",
    "StrangeCustoms.RiverBuilder": "river",
    "StrangeCustoms.WaterfallBuilder": "waterfall",
    "StrangeCustoms.TerrainRoadBuilder": "terrainRoad",
}

TURNTABLE_HANDLER = "AlinasMapMod.Turntable.TurntableBuilder"
LOADER_HANDLERS = {
    "AlinasMapMod.Loaders.LoaderBuilder",
    "AlinasMapMod.LoaderBuilder",
}
LOADER_HANDLER = "AlinasMapMod.Loaders.LoaderBuilder"
STATION_HANDLERS = {
    "AlinasMapMod.Stations.StationAgentBuilder",
    "AlinasMapMod.StationAgentBuilder",
}
STATION_HANDLER = "AlinasMapMod.Stations.StationAgentBuilder"
MAP_LABEL_HANDLER = "AlinasMapMod.MapLabelBuilder"
TELEGRAPH_POLE_MOVER_HANDLERS = {
    "AlinasMapMod.TelegraphPoleMover",
    "AlinasMapMod.TelegraphPoles.TelegraphPoleMover",
}
RR_CROSSING_HANDLERS = {
    "cutil.rrcrossing",
    "cutil.railroadcrossing",
}


def load_json(path):
    try:
        return legacy_json.read_json(path)
    except Exception as exc:
        print(f"[WARN] Could not parse {path}: {exc}", file=sys.stderr)
        return {}


def save_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def slug(value):
    value = re.sub(r"[^A-Za-z0-9]+", "-", value).strip("-").lower()
    return value or "fragment"


def vector(value, default_scale=False):
    if isinstance(value, (list, tuple)):
        return {
            "x": round(float(value[0] if len(value) > 0 else (1 if default_scale else 0)), 6),
            "y": round(float(value[1] if len(value) > 1 else (1 if default_scale else 0)), 6),
            "z": round(float(value[2] if len(value) > 2 else (1 if default_scale else 0)), 6),
        }

    if not isinstance(value, dict):
        return {"x": 1, "y": 1, "z": 1} if default_scale else {"x": 0, "y": 0, "z": 0}

    return {
        "x": round(float(value.get("x", 0)), 6),
        "y": round(float(value.get("y", 0)), 6),
        "z": round(float(value.get("z", 0)), 6),
    }


def optional_vector(value):
    return vector(value) if isinstance(value, (dict, list, tuple)) else None


def string_ids(value):
    if value is None:
        return []
    if isinstance(value, str):
        return [value] if value.strip() else []
    if isinstance(value, dict):
        for key in ("id", "spanId", "trackSpanId", "trackSpan"):
            if value.get(key):
                return string_ids(value.get(key))
        return []
    if isinstance(value, (list, tuple)):
        result = []
        for item in value:
            result.extend(string_ids(item))
        return result
    return [str(value)] if str(value).strip() else []


def skeleton(mod_id, mod_name, mod_version, author, fragment_name):
    return {
        "$schema": ".\\schemas\\fuse-mod.schema.json",
        "schemaVersion": FUSE_SCHEMA_VERSION,
        "id": f"{mod_id}.{fragment_name}",
        "name": f"{mod_name} ({fragment_name})",
        "author": author,
        "modVersion": mod_version,
        "coordinateSpace": "world",
        "tracks": {
            "nodes": {},
            "segments": {},
            "spans": {},
            "areas": {},
            "removals": {"nodes": [], "segments": [], "spans": []},
        },
        "operations": {
            "loads": {},
            "industries": {},
            "loaders": {},
            "turntables": {},
            "stations": {},
        },
        "world": {
            "scenery": {},
            "spawnPoints": [],
            "splineys": {},
            "telegraphPoles": {},
            "telegraphPoleMovements": [],
            "mapLabels": {},
            "mapMasks": {},
            "mapTiles": {},
            "sceneClones": {},
            "removals": {
                "scenery": [],
                "splineys": [],
                "telegraphPoles": [],
                "mapLabels": [],
                "mapMasks": [],
                "sceneClones": [],
            },
        },
        "progression": {"sections": [], "progressions": {}, "mapFeatures": {}},
        "extensions": {},
    }


def meta(mod_folder):
    for name in ("Definition.json", "Info.json"):
        path = mod_folder / name
        if path.exists():
            data = load_json(path)
            mod_id = data.get("id") or data.get("Id") or mod_folder.name
            mod_name = data.get("name") or data.get("DisplayName") or mod_id
            version = data.get("version") or data.get("Version") or "1.0.0"
            author = data.get("author") or data.get("Author") or ""
            return mod_id, mod_name, version, author

    return mod_folder.name, mod_folder.name, "1.0.0", ""


CORE_LEGACY_REQUIREMENTS = {
    "railroader",
    "railloader",
    "zamu.strangecustoms",
    "fuse",
}


def extract_file_reference(value):
    if not isinstance(value, str):
        return ""
    match = re.match(r"^\s*file\((.+)\)\s*$", value, re.IGNORECASE)
    if not match:
        return ""
    return match.group(1).strip().strip("\"'")


def convert_requirement(item):
    if isinstance(item, str):
        requirement_id = item.strip()
        if not requirement_id or requirement_id.lower() in CORE_LEGACY_REQUIREMENTS:
            return None
        return {"id": requirement_id}

    if not isinstance(item, dict):
        return None

    requirement_id = str(item.get("id") or item.get("Id") or "").strip()
    if not requirement_id or requirement_id.lower() in CORE_LEGACY_REQUIREMENTS:
        return None

    result = {
        "id": requirement_id,
        "notBefore": item.get("notBefore") or item.get("NotBefore"),
        "notAfter": item.get("notAfter") or item.get("NotAfter"),
    }
    return clean(result)


def convert_requirements(value):
    if not isinstance(value, list):
        return []

    result = []
    for item in value:
        converted = convert_requirement(item)
        if converted:
            result.append(converted)
    return result


def mixinto_metadata(mod_folder):
    definition_path = mod_folder / "Definition.json"
    if not definition_path.exists():
        return {}, []

    definition = load_json(definition_path)
    metadata = {}
    ordered_files = []

    def record(target, reference, requirements):
        referenced_file = extract_file_reference(reference)
        if not referenced_file:
            return

        source_file = Path(referenced_file).name
        key = source_file.lower()
        if key not in metadata:
            ordered_files.append(key)
        metadata[key] = clean({
            "target": str(target or "").strip(),
            "sourceFile": source_file,
            "requires": requirements or [],
        })

    def visit_target(target, value):
        if isinstance(value, str):
            record(target, value, [])
            return

        if isinstance(value, list):
            for item in value:
                visit_target(target, item)
            return

        if not isinstance(value, dict):
            return

        requirements = convert_requirements(value.get("requires") or value.get("Requires"))
        reference = value.get("mixinto") or value.get("Mixinto")
        record(target, reference, requirements)

    mixintos = definition.get("mixintos") or definition.get("Mixintos") or {}
    if isinstance(mixintos, dict):
        for target, value in mixintos.items():
            visit_target(target, value)

    return metadata, ordered_files


def convert_node(item):
    return {
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "flipSwitchStand": bool(item.get("flipSwitchStand", False)),
    }


def convert_segment(item, segment_id=None):
    group_id = item.get("groupId") or item.get("GroupId") or None
    if group_id:
        _GROUP_IDS_REFERENCED.add(str(group_id))
    return {
        "startNodeId": item.get("startId") or item.get("startNodeId") or item.get("nodeA") or item.get("a") or "",
        "endNodeId": item.get("endId") or item.get("endNodeId") or item.get("nodeB") or item.get("b") or "",
        "style": item.get("Style") or item.get("style") or "standard",
        "trackClass": item.get("trackClass") or item.get("TrackClass") or "main",
        "speedLimit": int(item.get("speedLimit", item.get("SpeedLimit", 45))),
        "priority": int(item.get("priority", 0)),
        "groupId": group_id,
    }


def normalize_end(value):
    if value is None:
        return None

    text = str(value).strip().lower()
    if text in ("start", "a"):
        return "A"
    if text in ("end", "b"):
        return "B"
    return value


def convert_location(item):
    if not isinstance(item, dict):
        return {"segmentId": "", "distance": 0, "end": "A"}

    result = {
        "segmentId": (
            item.get("segmentId")
            or item.get("segmentID")
            or item.get("SegmentId")
            or item.get("SegmentID")
            or item.get("segment")
            or ""
        ),
        "end": normalize_end(item.get("end")) or "A",
    }
    if "normalized" in item:
        result["normalized"] = float(item.get("normalized") or 0)
    else:
        result["distance"] = float(item.get("distance") or 0)
    if "offset" in item:
        result["offset"] = float(item.get("offset") or 0)
    return result


def convert_span(item, span_id=None):
    upper = convert_location(item.get("upper"))
    lower = convert_location(item.get("lower"))
    _validate_span_geometry(span_id, upper, lower)
    return {
        "upper": upper,
        "lower": lower,
    }


def _validate_span_geometry(span_id, upper, lower):
    # Detect spans with crossed endpoints when both endpoints are on the same
    # segment AND share the same anchor end ("A" or "B"). The general case
    # (different ends) needs the segment's physical length, which the
    # converter doesn't have at this point - the FUSE runtime catches those
    # at preflight. This warning catches the easy half: legacy modders who
    # accidentally swapped distances within one anchor side.
    if not span_id or not isinstance(upper, dict) or not isinstance(lower, dict):
        return
    if upper.get("segmentId") != lower.get("segmentId"):
        return
    if not upper.get("segmentId"):
        return
    if upper.get("end") != lower.get("end"):
        return

    upper_d = upper.get("distance")
    lower_d = lower.get("distance")
    if upper_d is None or lower_d is None:
        return

    # Both endpoints anchored to the same end. "Upper" should be farther
    # from anchor A and closer to anchor B; for end="A" that means
    # upper_d > lower_d, for end="B" the reverse.
    end = upper.get("end")
    if end == "A" and upper_d < lower_d:
        _warn(
            f"Span '{span_id}' on segment '{upper.get('segmentId')}': both endpoints "
            f"anchored to A but upper.distance ({upper_d}) < lower.distance ({lower_d}). "
            "FUSE will reject as crossed; check legacy 'Start'/'End' mapping.",
            concept="span-geometry-crossed",
        )
    elif end == "B" and upper_d > lower_d:
        _warn(
            f"Span '{span_id}' on segment '{upper.get('segmentId')}': both endpoints "
            f"anchored to B but upper.distance ({upper_d}) > lower.distance ({lower_d}). "
            "FUSE will reject as crossed; check legacy 'Start'/'End' mapping.",
            concept="span-geometry-crossed",
        )


def convert_load(load_id, item):
    return {
        "name": item.get("name") or item.get("description") or load_id,
        "units": item.get("units") or "Quantity",
        "density": item.get("density"),
        "unitWeightInPounds": item.get("unitWeightInPounds"),
        "importable": item.get("importable"),
        "payPerQuantity": item.get("payPerQuantity"),
        "costPerUnit": item.get("costPerUnit"),
        "carTypeFilter": item.get("carTypeFilter"),
    }


def convert_area(area_id, item, order=None):
    radius = item.get("radius")
    tag_color = _normalize_tag_color(area_id, item.get("tagColor"))
    return clean({
        "name": item.get("name") or area_id,
        "position": optional_vector(item.get("position") or item.get("localPosition")),
        "radius": float(radius) if radius is not None else None,
        "tagColor": tag_color,
        "order": order,
        "spanIds": item.get("spanIds") or item.get("spans"),
        "groupId": item.get("groupId") or item.get("GroupId"),
    })


def _normalize_tag_color(area_id, value):
    # FUSE schema requires 3 (RGB) or 4 (RGBA) numbers in [0,1]. A small
    # number of legacy mods (e.g. Graham County) shipped 6-element tagColor
    # arrays - the modder concatenated two RGB triples. Truncate to the
    # first 3 and warn so the package can still load instead of failing
    # the whole sub-package at deserialization.
    if not isinstance(value, list):
        return value
    if 3 <= len(value) <= 4:
        return value
    if len(value) > 4:
        _warn(
            f"Area '{area_id}' tagColor has {len(value)} values; FUSE accepts 3 or 4. "
            "Truncated to the first 3 values to keep the package loadable.",
            concept="area-tagColor-overflow",
        )
        return value[:3]
    if len(value) > 0:
        _warn(
            f"Area '{area_id}' tagColor has only {len(value)} value(s); FUSE requires 3 or 4. "
            "Padded with zeros to length 3 to keep the package loadable.",
            concept="area-tagColor-underflow",
        )
        padded = list(value) + [0.0] * (3 - len(value))
        return padded[:3]
    return value


def convert_component(component_id, item):
    component_type = normalize_component_type(item.get("type") or component_id)
    is_passenger = component_type == "passengerStop"
    result = {
        "type": component_type,
        "name": item.get("name") or component_id,
        "trackSpanIds": item.get("trackSpanIds") or item.get("trackSpans") or item.get("spans") or [],
        "carTypeFilter": item.get("carTypeFilter"),
        "loadId": item.get("loadId"),
        "sharedStorage": item.get("sharedStorage", True),
        "storageChangeRate": item.get("storageChangeRate"),
        "maxStorage": item.get("maxStorage"),
        "carTransferRate": item.get("carTransferRate"),
        "orderAroundEmpties": item.get("orderAroundEmpties"),
        "orderAroundLoaded": item.get("orderAroundLoaded"),
        "inputSpanIds": item.get("inputSpanIds"),
        "outputSpanIds": item.get("outputSpanIds"),
        "inputTermsPerDay": item.get("inputTermsPerDay") or {},
        "outputTermsPerDay": item.get("outputTermsPerDay") or {},
        "idealCars": item.get("idealCars"),
        "teamProfiles": item.get("teamProfiles") or {},
        "canOverhaul": item.get("canOverhaul"),
        "passengerStopId": item.get("passengerStopId") or (component_id if is_passenger else None),
        "timetableCode": item.get("timetableCode"),
        "basePopulation": item.get("basePopulation"),
        "neighborIds": item.get("neighborIds"),
        "branch": item.get("branch"),
        "branchDefinitions": item.get("branchDefinitions"),
        "carLoadPeriod": item.get("carLoadPeriod"),
        "carLengthFeet": item.get("carLengthFeet"),
    }
    return clean(result)


def normalize_component_type(component_type):
    value = str(component_type or "").strip()
    normalized = value.lower()
    aliases = {
        "model.ops.industryloader": "loader",
        "model.opsnew.industryloader": "loader",
        "industryloader": "loader",
        "model.ops.industryunloader": "unloader",
        "model.opsnew.industryunloader": "unloader",
        "industryunloader": "unloader",
        "model.ops.formulaicindustrycomponent": "formulaic",
        "model.opsnew.formulaicindustrycomponent": "formulaic",
        "formulaicindustrycomponent": "formulaic",
        "model.ops.repairtrack": "repairTrack",
        "model.opsnew.repairtrack": "repairTrack",
        "repair-track": "repairTrack",
        "model.ops.teamtrack": "teamTrack",
        "model.opsnew.teamtrack": "teamTrack",
        "team-track": "teamTrack",
        "model.ops.interchange": "interchange",
        "model.opsnew.interchange": "interchange",
        "model.ops.interchangedindustryloader": "interchangedLoader",
        "model.opsnew.interchangedindustryloader": "interchangedLoader",
        "interchanged-loader": "interchangedLoader",
        "model.ops.interchangedindustryunloader": "interchangedUnloader",
        "model.opsnew.interchangedindustryunloader": "interchangedUnloader",
        "interchanged-unloader": "interchangedUnloader",
        "interchangedunloader": "interchangedUnloader",
        "model.ops.teleportloadingindustry": "teleportLoading",
        "model.opsnew.teleportloadingindustry": "teleportLoading",
        "teleport-loading": "teleportLoading",
        "teleportloadingindustry": "teleportLoading",
        "model.ops.progressionindustrycomponent": "progression",
        "model.opsnew.progressionindustrycomponent": "progression",
        "progression-industry": "progression",
        "progressionindustry": "progression",
        "progressionindustrycomponent": "progression",
        "alinasmapmod.paxstationcomponent": "passengerStop",
        "alinasmapmod.stations.paxstationcomponent": "passengerStop",
        "paxstationcomponent": "passengerStop",
        "passenger-stop": "passengerStop",
        "passengerstop": "passengerStop",
    }
    return aliases.get(normalized, value)


def convert_industry(industry_id, item, area_id=None, order=None):
    components = {}
    for component_id, component in (item.get("components") or {}).items():
        if isinstance(component, dict):
            components[component_id] = convert_component(component_id, component)

    return clean({
        "name": item.get("name") or industry_id,
        "areaId": area_id or item.get("areaId") or item.get("area"),
        "order": order,
        "position": vector(item.get("localPosition") or item.get("position")),
        "rotation": vector(item.get("localRotation") or item.get("rotation")),
        "usesContract": bool(item.get("usesContract", False)),
        "components": components,
    })


def convert_turntable(table_id, item):
    result = {
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "radius": float(item.get("radius", item.get("Radius", 15))),
        "subdivisions": int(item.get("subdivisions", item.get("Subdivisions", 32))),
        "legacyIdentifier": item.get("legacyIdentifier") or (table_id if item.get("handler") == TURNTABLE_HANDLER else None),
    }
    roundhouse = item.get("roundhouse")
    roundhouse_stalls = item.get("roundhouseStalls", item.get("RoundhouseStalls"))
    if isinstance(roundhouse, dict):
        result["roundhouse"] = {
            "stalls": int(roundhouse.get("stalls", 0)),
            "startAngle": float(roundhouse.get("startAngle", 0)),
            "stallAngle": roundhouse.get("stallAngle"),
            "trackLength": float(roundhouse.get("trackLength", 46)),
            "startPrefab": roundhouse.get("startPrefab"),
            "endPrefab": roundhouse.get("endPrefab"),
            "stallPrefab": roundhouse.get("stallPrefab"),
        }
    elif roundhouse_stalls:
        result["roundhouse"] = {
            "stalls": int(roundhouse_stalls),
            "trackLength": float(item.get("roundhouseTrackLength", item.get("RoundhouseTrackLength", 46))),
            "startPrefab": item.get("startPrefab", item.get("StartPrefab", "vanilla://roundhouseStart")),
            "endPrefab": item.get("endPrefab", item.get("EndPrefab", "vanilla://roundhouseEnd")),
            "stallPrefab": item.get("stallPrefab", item.get("StallPrefab", "vanilla://roundhouseStall")),
        }
    return clean(result)


def convert_scenery(item):
    model = (
        item.get("assetIdentifier")
        or item.get("model")
        or item.get("modelIdentifier")
        or item.get("prefabIdentifier")
        or item.get("prefab")
        or ""
    )
    if model and "://" not in model:
        model = f"scenery://{model}"
    result = {
        "assetIdentifier": model,
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "scale": vector(item.get("scale") or item.get("localScale"), default_scale=True),
    }
    anchor_span_ids = string_ids(
        item.get("anchorSpanIds")
        or item.get("spanIds")
        or item.get("spans")
        or item.get("trackSpanIds")
        or item.get("trackSpans")
    )
    if anchor_span_ids:
        result["anchorSpanIds"] = anchor_span_ids
    return clean(result)


def convert_spliney(item):
    handler = item.get("handler") or ""
    spliney_type = infer_spliney_type(item, handler)
    points = []
    for point in item.get("points") or []:
        if not isinstance(point, dict):
            continue
        converted = {
            "position": vector(point.get("position") or point.get("localPosition")),
            "rotation": vector(point.get("rotation") or point.get("localRotation")),
        }
        if "width" in point:
            converted["width"] = float(point.get("width") or 0)
        points.append(converted)

    result = {
        "type": spliney_type,
        "profile": item.get("profile"),
        "style": item.get("style"),
        "offsetY": item.get("offsetY", item.get("offsety")),
        "headStyle": item.get("headStyle") or item.get("headstyle"),
        "tailStyle": item.get("tailStyle") or item.get("tailstyle"),
        "points": points,
    }
    if handler and handler not in HANDLER_MAP:
        result["extensions"] = {"originalHandler": handler}
    return clean(result)


def infer_spliney_type(item, handler):
    style = str(item.get("style") or "")
    profile = str(item.get("profile") or "")
    explicit_type = item.get("type")

    # Strange Customs FlowyThingBuilder is shared by roads and rivers. The
    # style/profile tells us which physical spline family the runtime needs.
    if handler == "StrangeCustoms.FlowyThingBuilder" and (
        style.lower() == "river" or "river" in profile.lower()
    ):
        return "river"

    if handler in HANDLER_MAP:
        return HANDLER_MAP[handler]

    if explicit_type:
        return explicit_type

    return "unknown"


def convert_scene_clone(key, item):
    source = item.get("source") or item.get("instantiateFrom")
    if source and "://" not in source:
        source = f"path://scene/{source}"
    return clean({
        "targetPath": item.get("targetPath") or key,
        "source": source,
        "enabled": item.get("enabled"),
        "localPosition": vector(item.get("localPosition") or item.get("position")),
        "localRotation": vector(item.get("localRotation") or item.get("rotation")),
        "localScale": vector(item.get("localScale") or item.get("scale"), default_scale=True),
    })


def convert_label(key, item):
    text = item.get("text") or key
    result = {
        "text": text,
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "size": item.get("size") or item.get("fontSize"),
        "color": item.get("color"),
    }

    match = re.match(r"^\s*(\d{1,3})\s*MPH\.?\s*$", str(text), re.IGNORECASE)
    if match:
        speed_limit = int(match.group(1))
        result["text"] = str(speed_limit)
        result["style"] = "speedLimit"
        result["speedLimitMph"] = speed_limit

    return clean(result)


def convert_loader(item):
    return clean({
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "prefab": item.get("prefab") or "empty://",
        "industryId": item.get("industry"),
    })


def convert_station(item):
    return clean({
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "prefab": item.get("prefab") or "empty://",
        "passengerStopId": item.get("passengerStop"),
    })


def convert_telegraph_pole_movements(item):
    poles = item.get("polesToMove") or item.get("PolesToMove") or []
    raw_movements = item.get("poleMovement") or item.get("PoleMovement") or []
    grouped = {}
    for index, pole in enumerate(poles):
        if pole is None:
            continue
        movement = raw_movements[index] if index < len(raw_movements) else [0, 0, 0]
        if isinstance(movement, dict):
            offset = vector(movement)
        elif isinstance(movement, (list, tuple)):
            offset = {
                "x": round(float(movement[0] if len(movement) > 0 else 0), 6),
                "y": round(float(movement[1] if len(movement) > 1 else 0), 6),
                "z": round(float(movement[2] if len(movement) > 2 else 0), 6),
            }
        else:
            offset = {
                "x": 0,
                "y": 0,
                "z": 0,
            }
        key = (offset["x"], offset["y"], offset["z"])
        grouped.setdefault(key, {"poleIndices": [], "offset": offset})["poleIndices"].append(int(pole))
    return list(grouped.values())


def convert_legacy_start(source):
    spawn = source.get("spawnPoint")
    if not isinstance(spawn, dict):
        return None

    return clean({
        "name": source.get("name") or source.get("identifier") or "Legacy Start",
        "position": vector(spawn.get("position") or spawn.get("location")),
        "rotation": vector(spawn.get("rotation")),
        "radius": spawn.get("range") or spawn.get("radius"),
    })


def clean(value):
    if isinstance(value, dict):
        return {
            key: clean(item)
            for key, item in value.items()
            if item is not None and clean(item) not in ({}, [])
        }
    if isinstance(value, list):
        return [clean(item) for item in value if item is not None]
    return value


def normalize_delivery_direction(value):
    if value is None:
        return value
    text = str(value).strip().lower()
    if text in ("0", "loadtoindustry", "toindustry", "to", "import"):
        return "loadToIndustry"
    if text in ("1", "loadfromindustry", "fromindustry", "from", "export"):
        return "loadFromIndustry"
    return value


BOOL_DICTIONARY_ARRAY_FIELDS = {
    "prerequisiteFeatureIds",
    "prerequisiteSections",
    "prerequisiteSectionIds",
    "enableFeaturesOnUnlock",
    "disableFeaturesOnUnlock",
    "enableFeaturesOnAvailable",
    "unlockIncludeIndustries",
    "unlockExcludeIndustries",
    "unlockIncludeIndustryComponents",
    "areasEnableOnUnlock",
    "gameObjectsEnableOnUnlock",
    "trackGroupsEnableOnUnlock",
    "trackGroupsAvailableOnUnlock",
}


def bool_dictionary_to_array(value):
    if not isinstance(value, dict):
        return None

    result = []
    for key, item in value.items():
        if item is False or item is None:
            continue
        text = str(key).strip()
        if text:
            result.append(text)
    return result


def normalize_progression_value(value):
    if isinstance(value, list):
        return [normalize_progression_value(item) for item in value if item is not None]
    if not isinstance(value, dict):
        return value

    result = {}
    for key, item in value.items():
        target_key = key
        lower_key = str(key).lower()
        if lower_key == "displayname":
            target_key = "displayName"
        elif lower_key == "name":
            target_key = "displayName"
        elif lower_key == "defaultenableinsandbox":
            target_key = "initiallyEnabled"
        elif lower_key == "prerequisites":
            target_key = "prerequisiteFeatureIds"
        elif lower_key == "industrycomponent":
            target_key = "industryComponentId"
        elif lower_key == "load":
            target_key = "loadId"

        if lower_key == "direction":
            result[target_key] = normalize_delivery_direction(item)
            continue

        if target_key == "industryComponentId" and not str(item or "").strip():
            result[target_key] = None
            continue

        if target_key in BOOL_DICTIONARY_ARRAY_FIELDS:
            normalized_array = bool_dictionary_to_array(item)
            if normalized_array is not None:
                result[target_key] = normalized_array
                continue

        if target_key in result and key != target_key:
            continue

        result[target_key] = normalize_progression_value(item)

    return clean(result)


def next_area_order(order_state, area_id):
    if order_state is None:
        return None

    area_orders = order_state.setdefault("area_orders", {})
    key = str(area_id or "").lower()
    if key in area_orders:
        return area_orders[key]

    order = order_state.get("next_area_order", 0)
    order_state["next_area_order"] = order + 1
    area_orders[key] = order
    return order


def next_industry_order(order_state, area_id, industry_id):
    if order_state is None:
        return None

    area_key = str(area_id or "__unassigned__").lower()
    industry_orders_by_area = order_state.setdefault("industry_orders_by_area", {})
    next_by_area = order_state.setdefault("next_industry_order_by_area", {})
    industry_orders = industry_orders_by_area.setdefault(area_key, {})
    industry_key = str(industry_id or "").lower()
    if industry_key in industry_orders:
        return industry_orders[industry_key]

    order = next_by_area.get(area_key, 0)
    next_by_area[area_key] = order + 1
    industry_orders[industry_key] = order
    return order


def convert_source(source, rail, source_name=None, order_state=None):
    order_state = order_state if order_state is not None else {}

    tracks = source.get("tracks") or {}
    for node_id, node in (tracks.get("nodes") or {}).items():
        if node is None:
            rail["tracks"]["removals"]["nodes"].append(node_id)
        elif isinstance(node, dict):
            rail["tracks"]["nodes"][node_id] = convert_node(node)

    for segment_id, segment in (tracks.get("segments") or {}).items():
        if segment is None:
            rail["tracks"]["removals"]["segments"].append(segment_id)
        elif isinstance(segment, dict):
            rail["tracks"]["segments"][segment_id] = clean(convert_segment(segment))

    for span_id, span in (tracks.get("spans") or {}).items():
        if span is None:
            rail["tracks"]["removals"]["spans"].append(span_id)
        elif isinstance(span, dict):
            rail["tracks"]["spans"][span_id] = convert_span(span, span_id)

    for load_id, load in (source.get("loads") or {}).items():
        if isinstance(load, dict):
            rail["operations"]["loads"][load_id] = clean(convert_load(load_id, load))

    for area_id, area in (source.get("areas") or {}).items():
        if not isinstance(area, dict):
            continue
        area_order = next_area_order(order_state, area_id)
        rail["tracks"]["areas"][area_id] = convert_area(area_id, area, area_order)
        for industry_id, industry in (area.get("industries") or {}).items():
            if isinstance(industry, dict):
                industry_order = next_industry_order(order_state, area_id, industry_id)
                rail["operations"]["industries"][industry_id] = convert_industry(industry_id, industry, area_id, industry_order)

    for industry_id, industry in (source.get("industries") or {}).items():
        if isinstance(industry, dict):
            area_id = industry.get("areaId") or industry.get("area")
            industry_order = next_industry_order(order_state, area_id, industry_id)
            rail["operations"]["industries"][industry_id] = convert_industry(industry_id, industry, order=industry_order)

    for table_id, table in (source.get("turntables") or {}).items():
        if isinstance(table, dict):
            rail["operations"]["turntables"][table_id] = convert_turntable(table_id, table)

    for scenery_id, scenery in (source.get("scenery") or {}).items():
        if scenery is None:
            rail["world"]["removals"]["scenery"].append(scenery_id)
        elif isinstance(scenery, dict):
            rail["world"]["scenery"][scenery_id] = convert_scenery(scenery)

    for spliney_id, spliney in (source.get("splineys") or {}).items():
        if spliney is None:
            rail["world"]["removals"]["splineys"].append(spliney_id)
        elif isinstance(spliney, dict):
            handler = spliney.get("handler")
            if handler == TURNTABLE_HANDLER:
                rail["operations"]["turntables"][spliney_id] = convert_turntable(spliney_id, spliney)
            elif handler in LOADER_HANDLERS:
                rail["operations"]["loaders"][spliney_id] = convert_loader(spliney)
            elif handler in STATION_HANDLERS:
                rail["operations"]["stations"][spliney_id] = convert_station(spliney)
            elif handler == MAP_LABEL_HANDLER:
                rail["world"]["mapLabels"][spliney_id] = convert_label(spliney_id, spliney)
            elif handler in TELEGRAPH_POLE_MOVER_HANDLERS:
                rail["world"]["telegraphPoleMovements"].extend(convert_telegraph_pole_movements(spliney))
            elif (handler or "").lower() in RR_CROSSING_HANDLERS:
                rail["world"]["scenery"][spliney_id] = convert_scenery(spliney)
            elif len(spliney.get("points") or []) < 2:
                rail["extensions"].setdefault("legacySplineyObjects", {})[spliney_id] = spliney
            else:
                rail["world"]["splineys"][spliney_id] = convert_spliney(spliney)

    for clone_id, clone in (source.get("mandelas") or {}).items():
        if clone is None:
            rail["world"]["removals"]["sceneClones"].append(clone_id)
        elif isinstance(clone, dict):
            rail["world"]["sceneClones"][clone_id] = convert_scene_clone(clone_id, clone)

    for label_id, label in (source.get("texts") or {}).items():
        if label is None:
            rail["world"]["removals"]["mapLabels"].append(label_id)
        elif isinstance(label, dict):
            rail["world"]["mapLabels"][label_id] = convert_label(label_id, label)

    simple_graphs = source.get("simpleGraphs") or {}
    if simple_graphs:
        rail["extensions"]["simpleGraphs"] = simple_graphs

    legacy_start = convert_legacy_start(source)
    if legacy_start:
        rail["world"]["spawnPoints"].append(legacy_start)
        rail["extensions"]["legacyStartOption"] = clean({
            "identifier": source.get("identifier"),
            "name": source.get("name"),
            "progressionId": source.get("progressionId"),
            "showTutorial": source.get("showTutorial"),
            "initialMoney": source.get("initialMoney"),
            "enabledFeatures": source.get("enabledFeatures"),
            "carPlacements": source.get("carPlacements"),
        })

    convert_progression(source, rail)


def convert_progression(source, rail):
    progression = source.get("progression")
    if isinstance(progression, dict):
        if progression.get("progressionId"):
            rail["progression"]["progressionId"] = progression.get("progressionId")
        if isinstance(progression.get("sections"), list):
            rail["progression"]["sections"].extend(normalize_progression_value(progression.get("sections")))
        if isinstance(progression.get("progressions"), dict):
            rail["progression"]["progressions"].update(normalize_progression_value(progression.get("progressions")))
        if isinstance(progression.get("mapFeatures"), dict):
            rail["progression"]["mapFeatures"].update(normalize_progression_value(progression.get("mapFeatures")))

    if isinstance(source.get("progressions"), dict):
        rail["progression"]["progressions"].update(normalize_progression_value(source.get("progressions")))

    if isinstance(source.get("mapFeatures"), dict):
        rail["progression"]["mapFeatures"].update(normalize_progression_value(source.get("mapFeatures")))


def count_content(rail):
    counts = {}
    for section in ("tracks", "operations", "world", "progression"):
        for key, value in rail.get(section, {}).items():
            if section == "tracks" and key == "removals":
                continue
            if section == "world" and key == "removals":
                continue
            if isinstance(value, dict):
                counts[f"{section}.{key}"] = len(value)
            elif isinstance(value, list):
                counts[f"{section}.{key}"] = len(value)
    removals = rail["tracks"].get("removals") or {}
    for key, value in removals.items():
        counts[f"tracks.removals.{key}"] = len(value or [])
    world_removals = rail["world"].get("removals") or {}
    for key, value in world_removals.items():
        counts[f"world.removals.{key}"] = len(value or [])
    return counts


def has_content(rail):
    counts = count_content(rail)
    return any(counts.values())


def _collect_initially_enabled_groups(rail, sink: set[str]) -> None:
    # Walk this fragment's progression payload looking for sections /
    # mapFeatures that are marked initiallyEnabled (or the legacy
    # defaultEnableInSandbox=true synonym) and harvest their
    # trackGroupsEnableOnUnlock entries. Used at end-of-mod to flag
    # segment groupIds that no progression auto-enables.
    progression = rail.get("progression") or {}
    for container_key in ("sections", "mapFeatures", "progressions"):
        container = progression.get(container_key) or {}
        if isinstance(container, dict):
            for value in container.values():
                _harvest_enable_groups(value, sink)
        elif isinstance(container, list):
            for value in container:
                _harvest_enable_groups(value, sink)


def _harvest_enable_groups(node, sink: set[str]) -> None:
    if not isinstance(node, dict):
        return
    if node.get("initiallyEnabled") or node.get("defaultEnableInSandbox"):
        for group_id in node.get("trackGroupsEnableOnUnlock") or []:
            if group_id:
                sink.add(str(group_id))
    for value in node.values():
        if isinstance(value, dict):
            _harvest_enable_groups(value, sink)
        elif isinstance(value, list):
            for item in value:
                _harvest_enable_groups(item, sink)


def _emit_track_group_coverage_warning(declared_initial_groups: set[str]) -> None:
    referenced = referenced_group_ids()
    if not referenced:
        return
    uncovered = sorted(referenced - declared_initial_groups)
    if not uncovered:
        return

    sample = ", ".join(uncovered[:8])
    suffix = ", ..." if len(uncovered) > 8 else ""
    _warn(
        f"{len(uncovered)} track group(s) referenced by segments are not auto-enabled by any "
        f"progression with initiallyEnabled=true (e.g. {sample}{suffix}). FUSE rebuilds the "
        "graph after apply-segments, before apply-progression - any segment whose group has "
        "not been enabled by then is culled and shows up as 'missing after apply'. Either add "
        "an initiallyEnabled progression with these in trackGroupsEnableOnUnlock, or rely on "
        "the runtime pre-pass that enables groups before the first segment apply.",
        concept="track-group-not-auto-enabled",
    )


def convert_mod(mod_folder, out_folder):
    mod_id, mod_name, version, author = meta(mod_folder)
    mixinto_sources, mixinto_order = mixinto_metadata(mod_folder)
    mixinto_order_index = {name: index for index, name in enumerate(mixinto_order)}
    referenced_source_files = set(mixinto_order_index)
    source_files = sorted(
        (
            path for path in mod_folder.iterdir()
            if path.suffix.lower() == ".json"
            and path.name not in ("Definition.json", "Info.json")
            and not path.name.lower().endswith(".bak")
            and "signal" not in path.name.lower()
            and (not referenced_source_files or path.name.lower() in referenced_source_files)
        ),
        key=lambda path: source_file_order(path, mixinto_order_index),
    )

    if not source_files:
        raise SystemExit(f"No source JSON files found in {mod_folder}")

    out_folder.mkdir(parents=True, exist_ok=True)
    written = []
    written_order = {}
    summaries = []
    used_names = set()
    order_state = {}
    declared_initial_groups: set[str] = set()
    for source_index, source_file in enumerate(source_files):
        fragment = slug(source_file.stem)
        base_fragment = fragment
        index = 2
        while fragment in used_names:
            fragment = f"{base_fragment}-{index}"
            index += 1
        used_names.add(fragment)

        rail = skeleton(mod_id, mod_name, version, author, fragment)
        mixinto = mixinto_sources.get(source_file.name.lower())
        if mixinto:
            rail["mixinto"] = mixinto
        convert_source(
            load_json(source_file),
            rail,
            source_name=source_file.name,
            order_state=order_state,
        )

        _collect_initially_enabled_groups(rail, declared_initial_groups)

        out_name = f"{fragment}.fuse.json"
        save_json(out_folder / out_name, rail)
        written.append(out_name)
        written_order[out_name] = source_index
        summaries.append((source_file.name, out_name, count_content(rail)))

    _emit_track_group_coverage_warning(declared_initial_groups)

    rail_data_files = sorted(written, key=lambda name: rail_data_file_order(name, written_order.get(name, 1000000)))
    info = {
        "$schema": ".\\schemas\\umm-info.schema.json",
        "Id": f"{mod_id}.FUSE",
        "DisplayName": f"{mod_name} (FUSE)",
        "Author": author,
        "Version": version,
        "ManagerVersion": "0.27.10",
        "Requirements": ["FUSE"],
        "LoadAfter": ["FUSE"],
        "FuseDataFiles": rail_data_files,
    }
    save_json(out_folder / "Info.json", info)
    return summaries


def source_file_order(path, mixinto_order_index):
    lower = path.name.lower()
    if lower in mixinto_order_index:
        return 0, mixinto_order_index[lower], lower
    phase, _source_order, weight, fallback = rail_data_file_order(path.name)
    return 1, phase, weight, fallback


def rail_data_file_order(name, source_order=1000000):
    lower = name.lower()
    phase = 0

    if "loads" in lower:
        weight = 0
    elif "game-graph" in lower:
        weight = 1
    elif "turntable" in lower:
        weight = 2
    elif "industry" in lower:
        weight = 2
    else:
        weight = 5
    return phase, source_order, weight, lower


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mod_folder")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    summaries = convert_mod(Path(args.mod_folder).resolve(), Path(args.out).resolve())
    for source, output, counts in summaries:
        interesting = {key: value for key, value in counts.items() if value}
        print(f"{source} -> {output}: {interesting}")


if __name__ == "__main__":
    main()

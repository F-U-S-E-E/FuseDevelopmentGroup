#!/usr/bin/env python3
import argparse
import json
import re
import sys
from pathlib import Path

RAIL_SCHEMA_VERSION = 1

HANDLER_MAP = {
    "StrangeCustoms.FlowyThingBuilder": "road",
    "StrangeCustoms.AutoTrestleBuilder": "trestle",
    "StrangeCustoms.RiverBuilder": "river",
    "StrangeCustoms.WaterfallBuilder": "waterfall",
    "StrangeCustoms.TerrainRoadBuilder": "terrainRoad",
}

TURNTABLE_HANDLER = "AlinasMapMod.Turntable.TurntableBuilder"
LOADER_HANDLER = "AlinasMapMod.Loaders.LoaderBuilder"
STATION_HANDLER = "AlinasMapMod.Stations.StationAgentBuilder"
MAP_LABEL_HANDLER = "AlinasMapMod.MapLabelBuilder"


def load_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
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
    if not isinstance(value, dict):
        return {"x": 1, "y": 1, "z": 1} if default_scale else {"x": 0, "y": 0, "z": 0}

    return {
        "x": round(float(value.get("x", 0)), 6),
        "y": round(float(value.get("y", 0)), 6),
        "z": round(float(value.get("z", 0)), 6),
    }


def optional_vector(value):
    return vector(value) if isinstance(value, dict) else None


def skeleton(mod_id, mod_name, mod_version, author, fragment_name):
    return {
        "$schema": ".\\schemas\\rail-mod.schema.json",
        "schemaVersion": RAIL_SCHEMA_VERSION,
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
            "splineys": {},
            "telegraphPoles": {},
            "mapLabels": {},
            "mapMasks": {},
            "mapTiles": {},
            "sceneClones": {},
        },
        "progression": {"progressions": {}, "mapFeatures": {}},
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
            author = data.get("author") or data.get("Author") or "KingG"
            return mod_id, mod_name, version, author

    return mod_folder.name, mod_folder.name, "1.0.0", "Unknown"


def convert_node(item):
    return {
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "flipSwitchStand": bool(item.get("flipSwitchStand", False)),
    }


def convert_segment(item):
    return {
        "startNodeId": item.get("startId") or item.get("startNodeId") or item.get("nodeA") or item.get("a") or "",
        "endNodeId": item.get("endId") or item.get("endNodeId") or item.get("nodeB") or item.get("b") or "",
        "style": item.get("Style") or item.get("style") or "standard",
        "trackClass": item.get("trackClass") or item.get("TrackClass") or "main",
        "speedLimit": int(item.get("speedLimit", item.get("SpeedLimit", 45))),
        "priority": int(item.get("priority", 0)),
        "groupId": item.get("groupId") or item.get("GroupId") or None,
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
        "segmentId": item.get("segmentId") or item.get("segment") or "",
        "end": normalize_end(item.get("end")) or "A",
    }
    if "normalized" in item:
        result["normalized"] = float(item.get("normalized") or 0)
    else:
        result["distance"] = float(item.get("distance") or 0)
    if "offset" in item:
        result["offset"] = float(item.get("offset") or 0)
    return result


def convert_span(item):
    return {
        "upper": convert_location(item.get("upper")),
        "lower": convert_location(item.get("lower")),
    }


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
    return clean({
        "name": item.get("name") or area_id,
        "position": optional_vector(item.get("position") or item.get("localPosition")),
        "radius": float(radius) if radius is not None else None,
        "tagColor": item.get("tagColor"),
        "order": order,
        "spanIds": item.get("spanIds") or item.get("spans"),
        "groupId": item.get("groupId") or item.get("GroupId"),
    })


def convert_component(component_id, item):
    component_type = item.get("type") or component_id
    is_passenger = "paxstationcomponent" in component_type.lower() or "passenger" in component_type.lower()
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
    }
    return clean(result)


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
    model = item.get("model") or item.get("modelIdentifier") or ""
    if model and "://" not in model:
        model = f"scenery://{model}"
    return clean({
        "model": model,
        "position": vector(item.get("position") or item.get("localPosition")),
        "rotation": vector(item.get("rotation") or item.get("localRotation")),
        "scale": vector(item.get("scale") or item.get("localScale"), default_scale=True),
    })


def convert_spliney(item):
    handler = item.get("handler") or ""
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
        "type": HANDLER_MAP.get(handler, item.get("type") or "unknown"),
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


def convert_source(source, rail, late_rail=None):
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
            rail["tracks"]["spans"][span_id] = convert_span(span)

    for load_id, load in (source.get("loads") or {}).items():
        if isinstance(load, dict):
            rail["operations"]["loads"][load_id] = clean(convert_load(load_id, load))

    for area_order, (area_id, area) in enumerate((source.get("areas") or {}).items()):
        if not isinstance(area, dict):
            continue
        rail["tracks"]["areas"][area_id] = convert_area(area_id, area, area_order)
        for industry_order, (industry_id, industry) in enumerate((area.get("industries") or {}).items()):
            if isinstance(industry, dict):
                rail["operations"]["industries"][industry_id] = convert_industry(industry_id, industry, area_id, industry_order)

    for industry_order, (industry_id, industry) in enumerate((source.get("industries") or {}).items()):
        if isinstance(industry, dict):
            rail["operations"]["industries"][industry_id] = convert_industry(industry_id, industry, order=industry_order)

    for table_id, table in (source.get("turntables") or {}).items():
        if isinstance(table, dict):
            rail["operations"]["turntables"][table_id] = convert_turntable(table_id, table)

    for scenery_id, scenery in (source.get("scenery") or {}).items():
        if isinstance(scenery, dict):
            rail["world"]["scenery"][scenery_id] = convert_scenery(scenery)

    for spliney_id, spliney in (source.get("splineys") or {}).items():
        if isinstance(spliney, dict):
            handler = spliney.get("handler")
            if handler == TURNTABLE_HANDLER:
                rail["operations"]["turntables"][spliney_id] = convert_turntable(spliney_id, spliney)
            elif handler == LOADER_HANDLER:
                target = late_rail if late_rail is not None else rail
                target["operations"]["loaders"][spliney_id] = convert_loader(spliney)
            elif handler == STATION_HANDLER:
                target = late_rail if late_rail is not None else rail
                target["operations"]["stations"][spliney_id] = convert_station(spliney)
            elif handler == MAP_LABEL_HANDLER:
                rail["world"]["mapLabels"][spliney_id] = convert_label(spliney_id, spliney)
            elif len(spliney.get("points") or []) < 2:
                rail["extensions"].setdefault("legacySplineyObjects", {})[spliney_id] = spliney
            else:
                rail["world"]["splineys"][spliney_id] = convert_spliney(spliney)

    for clone_id, clone in (source.get("mandelas") or {}).items():
        if isinstance(clone, dict):
            rail["world"]["sceneClones"][clone_id] = convert_scene_clone(clone_id, clone)

    for label_id, label in (source.get("texts") or {}).items():
        if isinstance(label, dict):
            rail["world"]["mapLabels"][label_id] = convert_label(label_id, label)

    simple_graphs = source.get("simpleGraphs") or {}
    if simple_graphs:
        rail["extensions"]["simpleGraphs"] = simple_graphs


def count_content(rail):
    counts = {}
    for section in ("tracks", "operations", "world", "progression"):
        for key, value in rail.get(section, {}).items():
            if isinstance(value, dict):
                counts[f"{section}.{key}"] = len(value)
            elif isinstance(value, list):
                counts[f"{section}.{key}"] = len(value)
    removals = rail["tracks"].get("removals") or {}
    for key, value in removals.items():
        counts[f"tracks.removals.{key}"] = len(value or [])
    return counts


def has_content(rail):
    counts = count_content(rail)
    return any(value for key, value in counts.items() if key != "tracks.removals")


def convert_mod(mod_folder, out_folder):
    mod_id, mod_name, version, author = meta(mod_folder)
    source_files = sorted(
        path for path in mod_folder.iterdir()
        if path.suffix.lower() == ".json"
        and path.name not in ("Definition.json", "Info.json")
        and not path.name.lower().endswith(".bak")
    )

    if not source_files:
        raise SystemExit(f"No source JSON files found in {mod_folder}")

    out_folder.mkdir(parents=True, exist_ok=True)
    written = []
    summaries = []
    used_names = set()
    for source_file in source_files:
        fragment = slug(source_file.stem)
        base_fragment = fragment
        index = 2
        while fragment in used_names:
            fragment = f"{base_fragment}-{index}"
            index += 1
        used_names.add(fragment)

        rail = skeleton(mod_id, mod_name, version, author, fragment)
        late_fragment = f"{fragment}-late"
        late_rail = skeleton(mod_id, mod_name, version, author, late_fragment)
        convert_source(load_json(source_file), rail, late_rail)

        out_name = f"{fragment}.rail.json"
        save_json(out_folder / out_name, rail)
        written.append(out_name)
        summaries.append((source_file.name, out_name, count_content(rail)))
        if has_content(late_rail):
            late_out_name = f"{late_fragment}.rail.json"
            save_json(out_folder / late_out_name, late_rail)
            written.append(late_out_name)
            summaries.append((source_file.name, late_out_name, count_content(late_rail)))

    rail_data_files = sorted(written, key=rail_data_file_order)
    info = {
        "$schema": ".\\schemas\\umm-info.schema.json",
        "Id": f"{mod_id}.RAIL",
        "DisplayName": f"{mod_name} (RAIL)",
        "Author": author,
        "Version": version,
        "ManagerVersion": "0.27.10",
        "Requirements": ["RAIL"],
        "LoadAfter": ["RAIL"],
        "RailDataFiles": rail_data_files,
    }
    save_json(out_folder / "Info.json", info)
    return summaries


def rail_data_file_order(name):
    lower = name.lower()
    if "loads" in lower:
        weight = 0
    elif "late" in lower:
        weight = 3
    elif "game-graph" in lower:
        weight = 1
    elif "turntable" in lower:
        weight = 2
    elif "industry" in lower:
        weight = 2
    else:
        weight = 5
    return weight, lower


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

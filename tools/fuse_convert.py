#!/usr/bin/env python3
import argparse
import json
import math
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


def _info(message: str, *, file: str = "", concept: str = "") -> None:
    _PENDING_WARNINGS.append({
        "level": "INFO",
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

SUPPORTED_CUSTOM_INDUSTRY_COMPONENT_TYPES = {
    "confusingsupplements.industrycomponents.captiveconversionloader",
    "confusingsupplements.industrycomponents.captiveconversionunloader",
    "confusingsupplements.industrycomponents.pay4resource",
    "confusingsupplements.industrycomponents.empty",
}

CANONICAL_COMPONENT_TYPES = {
    "loader",
    "unloader",
    "formulaic",
    "repairTrack",
    "teamTrack",
    "interchange",
    "interchangedLoader",
    "interchangedUnloader",
    "teleportLoading",
    "progression",
    "passengerStop",
}

COMPONENT_SCHEMA_KEYS = {
    "type",
    "name",
    "trackspanids",
    "trackspans",
    "spans",
    "cartypefilter",
    "loadid",
    "load",
    "convertedloadid",
    "convertedloadid",
    "convertedload",
    "sharedstorage",
    "storagechangerate",
    "maxstorage",
    "cartransferrate",
    "costperunit",
    "notbeforehour",
    "notafterhour",
    "fillpercentage",
    "bookreasons",
    "title",
    "orderaroundempties",
    "orderaroundloaded",
    "inputspanids",
    "outputspanids",
    "inputtermsperday",
    "outputtermsperday",
    "idealcars",
    "teamprofiles",
    "canoverhaul",
    "passengerstopid",
    "timetablecode",
    "basepopulation",
    "neighborids",
    "branch",
    "branchdefinitions",
    "branches",
    "carloadperiod",
    "carlengthfeet",
    "extradata",
    "fields",
}

LOAD_SCHEMA_KEYS = {
    "name",
    "description",
    "units",
    "density",
    "unitweightinpounds",
    "importable",
    "payperquantity",
    "costperunit",
    "cartypefilter",
    "emptycartype",
    "loadedcartype",
    "icon",
    "fields",
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
        "gauge": item.get("gauge") or item.get("Gauge"),
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


def _vector_distance(a, b):
    if not isinstance(a, dict) or not isinstance(b, dict):
        return None
    dx = float(a.get("x", 0)) - float(b.get("x", 0))
    dy = float(a.get("y", 0)) - float(b.get("y", 0))
    dz = float(a.get("z", 0)) - float(b.get("z", 0))
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def _location_distance(location, segment_length):
    if not isinstance(location, dict) or segment_length is None:
        return None
    if location.get("normalized") is not None:
        distance = float(location.get("normalized") or 0) * float(segment_length)
    else:
        distance = float(location.get("distance") or 0)
    distance += float(location.get("offset") or 0)
    return distance


def _set_location_distance(location, distance):
    distance = max(0.0, float(distance))
    location["distance"] = round(distance, 6)
    location.pop("normalized", None)
    location.pop("offset", None)


def _distance_from_segment_a(location, segment_length):
    distance = _location_distance(location, segment_length)
    if distance is None:
        return None
    end = location.get("end")
    if end == "A":
        return distance
    if end == "B":
        return float(segment_length) - distance
    return None


def _same_segment_span_is_valid(upper, lower, segment_length):
    if upper.get("end") == lower.get("end"):
        return False
    upper_from_a = _distance_from_segment_a(upper, segment_length)
    lower_from_a = _distance_from_segment_a(lower, segment_length)
    if upper_from_a is None or lower_from_a is None:
        return True
    if upper.get("end") == "A":
        start_side = upper_from_a
        end_side = lower_from_a
    else:
        start_side = lower_from_a
        end_side = upper_from_a
    return start_side < end_side


def _estimate_segment_lengths(converted_fragments):
    nodes = {}
    segments = {}
    for _source_name, _out_name, _source_index, rail in converted_fragments:
        tracks = rail.get("tracks") or {}
        for node_id, node in (tracks.get("nodes") or {}).items():
            if isinstance(node, dict):
                nodes[node_id] = node.get("position")
        for segment_id, segment in (tracks.get("segments") or {}).items():
            if isinstance(segment, dict):
                segments[segment_id] = segment

    lengths = {}
    for segment_id, segment in segments.items():
        start = nodes.get(segment.get("startNodeId"))
        end = nodes.get(segment.get("endNodeId"))
        length = _vector_distance(start, end)
        if length is not None and length > 0:
            lengths[segment_id] = length
    return lengths


def _collect_converted_track_graph(converted_fragments):
    nodes = {}
    segments = {}
    for _source_name, _out_name, _source_index, rail in converted_fragments:
        tracks = rail.get("tracks") or {}
        for node_id, node in (tracks.get("nodes") or {}).items():
            if isinstance(node, dict):
                nodes[node_id] = node.get("position")
        for segment_id, segment in (tracks.get("segments") or {}).items():
            if isinstance(segment, dict):
                segments[segment_id] = segment

    lengths = {}
    node_to_segments = {}
    for segment_id, segment in segments.items():
        start_node = segment.get("startNodeId")
        end_node = segment.get("endNodeId")
        for node_id in (start_node, end_node):
            if node_id:
                node_to_segments.setdefault(node_id, set()).add(segment_id)
        length = _vector_distance(nodes.get(start_node), nodes.get(end_node))
        if length is not None and length > 0:
            lengths[segment_id] = length

    segment_neighbors = {segment_id: set() for segment_id in segments}
    for connected_segments in node_to_segments.values():
        connected = list(connected_segments)
        for index, left in enumerate(connected):
            for right in connected[index + 1:]:
                segment_neighbors[left].add(right)
                segment_neighbors[right].add(left)

    return {
        "nodes": nodes,
        "segments": segments,
        "lengths": lengths,
        "neighbors": segment_neighbors,
    }


def _clamp_span_endpoint(span_id, endpoint_name, location, segment_length, source_name):
    distance = _location_distance(location, segment_length)
    if distance is None:
        return False
    clamped = min(max(distance, 0.0), float(segment_length))
    if abs(clamped - distance) <= 0.0001:
        return False
    _set_location_distance(location, clamped)
    _info(
        f"Span '{span_id}' {endpoint_name} endpoint on segment '{location.get('segmentId')}' "
        f"had distance {distance:.3f} outside estimated segment length {segment_length:.3f}; "
        f"clamped to {clamped:.3f}.",
        file=source_name,
        concept="span-repaired",
    )
    return True


def _repair_same_segment_span(span_id, span, segment_length, source_name):
    if not isinstance(span, dict):
        return False
    upper = span.get("upper")
    lower = span.get("lower")
    if not isinstance(upper, dict) or not isinstance(lower, dict):
        return False
    if not upper.get("segmentId") or upper.get("segmentId") != lower.get("segmentId"):
        return False
    if upper.get("end") not in ("A", "B") or lower.get("end") not in ("A", "B"):
        return False
    if segment_length is None or segment_length <= 0:
        return False

    repaired = False
    repaired |= _clamp_span_endpoint(span_id, "upper", upper, segment_length, source_name)
    repaired |= _clamp_span_endpoint(span_id, "lower", lower, segment_length, source_name)

    if _same_segment_span_is_valid(upper, lower, segment_length):
        return repaired

    if upper.get("end") == lower.get("end"):
        _warn(
            f"Span '{span_id}' on segment '{upper.get('segmentId')}' has both endpoints anchored to "
            f"{upper.get('end')}; FUSE needs opposite-facing endpoints and cannot safely infer the other side.",
            file=source_name,
            concept="span-geometry-crossed",
        )
        return repaired

    swapped_upper = dict(lower)
    swapped_lower = dict(upper)
    if _same_segment_span_is_valid(swapped_upper, swapped_lower, segment_length):
        span["upper"] = swapped_upper
        span["lower"] = swapped_lower
        _info(
            f"Span '{span_id}' on segment '{upper.get('segmentId')}' had crossed endpoints; swapped upper/lower.",
            file=source_name,
            concept="span-repaired",
        )
        return True

    _warn(
        f"Span '{span_id}' on segment '{upper.get('segmentId')}' has crossed endpoints for estimated "
        f"segment length {segment_length:.3f}; converter preserved the original endpoints because no safe "
        "automatic repair was available.",
        file=source_name,
        concept="span-geometry-crossed",
    )
    return repaired


def _shared_node(segment_a, segment_b):
    if not isinstance(segment_a, dict) or not isinstance(segment_b, dict):
        return None
    nodes_a = {segment_a.get("startNodeId"), segment_a.get("endNodeId")}
    nodes_b = {segment_b.get("startNodeId"), segment_b.get("endNodeId")}
    shared = [node for node in nodes_a.intersection(nodes_b) if node]
    return shared[0] if len(shared) == 1 else None


def _opposite_end_for_node(segment, node_id):
    if not isinstance(segment, dict) or not node_id:
        return None
    if segment.get("startNodeId") == node_id:
        return "B"
    if segment.get("endNodeId") == node_id:
        return "A"
    return None


def _flip_location_end_preserving_position(location, desired_end, segment_length):
    if not isinstance(location, dict) or desired_end not in ("A", "B"):
        return False
    current_end = location.get("end")
    if current_end == desired_end:
        return False
    distance = _location_distance(location, segment_length)
    if distance is None or segment_length is None:
        return False
    location["end"] = desired_end
    _set_location_distance(location, float(segment_length) - distance)
    return True


def _find_segment_path(start_segment_id, end_segment_id, neighbors):
    if start_segment_id == end_segment_id:
        return [start_segment_id]
    if start_segment_id not in neighbors or end_segment_id not in neighbors:
        return None

    queue = [(start_segment_id, [start_segment_id])]
    visited = {start_segment_id}
    while queue:
        current, path = queue.pop(0)
        for neighbor in sorted(neighbors.get(current, ())):
            if neighbor in visited:
                continue
            next_path = path + [neighbor]
            if neighbor == end_segment_id:
                return next_path
            visited.add(neighbor)
            queue.append((neighbor, next_path))
    return None


def _repair_multi_segment_span(span_id, span, graph, source_name):
    if not isinstance(span, dict):
        return False
    upper = span.get("upper")
    lower = span.get("lower")
    if not isinstance(upper, dict) or not isinstance(lower, dict):
        return False

    upper_segment_id = upper.get("segmentId")
    lower_segment_id = lower.get("segmentId")
    if not upper_segment_id or not lower_segment_id or upper_segment_id == lower_segment_id:
        return False

    segments = graph["segments"]
    lengths = graph["lengths"]
    if upper_segment_id not in segments or lower_segment_id not in segments:
        missing = [
            segment_id
            for segment_id in (upper_segment_id, lower_segment_id)
            if segment_id and segment_id not in segments
        ]
        if missing:
            _warn(
                f"Span '{span_id}' references segment(s) not defined in converted source files: "
                f"{', '.join(missing)}. Treating them as external/base-game dependencies.",
                file=source_name,
                concept="span-external-segment",
            )
        return False

    path = _find_segment_path(lower_segment_id, upper_segment_id, graph["neighbors"])
    if not path or len(path) < 2:
        _warn(
            f"Span '{span_id}' endpoints '{lower_segment_id}' -> '{upper_segment_id}' are both converted "
            "but no connected segment path was found between them. Preserved original anchors.",
            file=source_name,
            concept="span-route-unresolved",
        )
        return False

    lower_shared_node = _shared_node(segments[lower_segment_id], segments[path[1]])
    upper_shared_node = _shared_node(segments[upper_segment_id], segments[path[-2]])
    desired_lower_end = _opposite_end_for_node(segments[lower_segment_id], lower_shared_node)
    desired_upper_end = _opposite_end_for_node(segments[upper_segment_id], upper_shared_node)

    if desired_lower_end not in ("A", "B") or desired_upper_end not in ("A", "B"):
        _warn(
            f"Span '{span_id}' has a connected segment path but the converter could not infer endpoint "
            "direction at one side. Preserved original anchors.",
            file=source_name,
            concept="span-route-unresolved",
        )
        return False

    lower_length = lengths.get(lower_segment_id)
    upper_length = lengths.get(upper_segment_id)
    if lower_length is None or upper_length is None:
        _warn(
            f"Span '{span_id}' needs A/B anchor repair, but one endpoint segment has no estimated length. "
            "Preserved original anchors.",
            file=source_name,
            concept="span-route-unresolved",
        )
        return False

    old_lower = lower.get("end")
    old_upper = upper.get("end")
    repaired = False
    repaired |= _flip_location_end_preserving_position(lower, desired_lower_end, lower_length)
    repaired |= _flip_location_end_preserving_position(upper, desired_upper_end, upper_length)

    if repaired:
        _info(
            f"Span '{span_id}' endpoint anchors were aligned to converted segment topology "
            f"path='{ ' -> '.join(path) }' lowerEnd {old_lower}->{lower.get('end')} "
            f"upperEnd {old_upper}->{upper.get('end')}.",
            file=source_name,
            concept="span-repaired",
        )

    return repaired


def repair_package_spans(converted_fragments):
    graph = _collect_converted_track_graph(converted_fragments)
    segment_lengths = graph["lengths"]
    if not graph["segments"]:
        return
    for source_name, _out_name, _source_index, rail in converted_fragments:
        for span_id, span in (rail.get("tracks", {}).get("spans") or {}).items():
            upper = span.get("upper") if isinstance(span, dict) else None
            lower = span.get("lower") if isinstance(span, dict) else None
            segment_id = upper.get("segmentId") if isinstance(upper, dict) else None
            if not segment_id or not isinstance(lower, dict):
                continue
            if segment_id == lower.get("segmentId"):
                _repair_same_segment_span(span_id, span, segment_lengths.get(segment_id), source_name)
            else:
                _repair_multi_segment_span(span_id, span, graph, source_name)


def convert_load(load_id, item):
    result = {
        "name": item.get("name") or item.get("description") or load_id,
        "units": item.get("units") or "Quantity",
        "density": item.get("density"),
        "unitWeightInPounds": item.get("unitWeightInPounds"),
        "importable": item.get("importable"),
        "payPerQuantity": item.get("payPerQuantity"),
        "costPerUnit": item.get("costPerUnit"),
        "carTypeFilter": item.get("carTypeFilter"),
    }
    fields = {}
    explicit = item.get("fields")
    if isinstance(explicit, dict):
        fields.update(explicit)
    for key, value in item.items():
        if value is None:
            continue
        if str(key).strip().lower() in LOAD_SCHEMA_KEYS:
            continue
        fields.setdefault(key, value)
    if fields:
        result["fields"] = fields
    return result


KNOWN_COMPAT_LOADS = {
    "machine-parts": {
        "name": "Machine Parts",
        "units": "Pounds",
        "density": 42.5,
        "unitWeightInPounds": 0.0,
        "importable": True,
        "payPerQuantity": 0.0,
        "costPerUnit": 0.0,
    },
    "mining-explosives": {
        "name": "Mining Explosives",
        "units": "Pounds",
        "density": 37.5,
        "unitWeightInPounds": 0.0,
        "importable": True,
        "payPerQuantity": 0.0,
        "costPerUnit": 0.0,
    },
}


def _collect_load_references(value, sink):
    if isinstance(value, list):
        for item in value:
            _collect_load_references(item, sink)
        return
    if not isinstance(value, dict):
        return

    for key, item in value.items():
        if key in ("loadId", "convertedLoadId", "load") and isinstance(item, str) and item.strip():
            sink.add(item.strip())
        else:
            _collect_load_references(item, sink)


def ensure_known_compat_loads(rail):
    defined = set((rail.get("operations", {}).get("loads") or {}).keys())
    referenced = set()
    _collect_load_references(rail.get("operations"), referenced)
    _collect_load_references(rail.get("progression"), referenced)
    missing = sorted(
        load_id for load_id in referenced
        if load_id not in defined and load_id.lower() in KNOWN_COMPAT_LOADS
    )
    for load_id in missing:
        rail["operations"]["loads"][load_id] = dict(KNOWN_COMPAT_LOADS[load_id.lower()])


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
        _info(
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
    extra = item.get("extraData") or item.get("ExtraData") or {}

    def get_field(*keys):
        for key in keys:
            if key in item:
                return item.get(key)
        for key in keys:
            if isinstance(extra, dict) and key in extra:
                return extra.get(key)
        return None

    result = {
        "type": component_type,
        "name": item.get("name") or component_id,
        "trackSpanIds": item.get("trackSpanIds") or item.get("trackSpans") or item.get("spans") or [],
        "carTypeFilter": item.get("carTypeFilter"),
        "loadId": get_field("loadId", "LoadId", "load") or ("passengers" if is_passenger else None),
        "convertedLoadId": get_field("convertedLoadId", "convertedLoadID", "convertedLoad", "ConvertedLoadId"),
        "sharedStorage": item.get("sharedStorage", True),
        "storageChangeRate": get_field("storageChangeRate", "StorageChangeRate"),
        "maxStorage": get_field("maxStorage", "MaxStorage"),
        "carTransferRate": get_field("carTransferRate", "CarTransferRate"),
        "costPerUnit": get_field("costPerUnit"),
        "notBeforeHour": get_field("notBeforeHour"),
        "notAfterHour": get_field("notAfterHour"),
        "fillPercentage": get_field("fillPercentage"),
        "bookReasons": get_field("bookReasons"),
        "title": get_field("title"),
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
        "branchDefinitions": item.get("branchDefinitions") or item.get("branches"),
        "carLoadPeriod": item.get("carLoadPeriod"),
        "carLengthFeet": item.get("carLengthFeet"),
    }

    custom_fields = collect_custom_component_fields(component_type, item, extra)
    if custom_fields:
        result["fields"] = custom_fields

    return clean(result)


def collect_custom_component_fields(component_type, item, extra):
    normalized = str(component_type or "").strip()
    if not normalized or normalized in CANONICAL_COMPONENT_TYPES:
        return {}

    fields = {}
    explicit = item.get("fields")
    if isinstance(explicit, dict):
        fields.update(explicit)

    for source in (item, extra if isinstance(extra, dict) else {}):
        for key, value in source.items():
            if value is None:
                continue
            if str(key).strip().lower() in COMPONENT_SCHEMA_KEYS:
                continue
            fields.setdefault(key, value)

    return fields


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
        "captiveconversionloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
        "captive-conversion-loader": "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
        "confusingsupplements.captiveconversionloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
        "confusingsupplements.industrycomponents.captiveconversionloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
        "captiveconversionunloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
        "captive-conversion-unloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
        "confusingsupplements.captiveconversionunloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
        "confusingsupplements.industrycomponents.captiveconversionunloader": "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
        "pay4resource": "ConfusingSupplements.IndustryComponents.Pay4Resource",
        "pay-for-resource": "ConfusingSupplements.IndustryComponents.Pay4Resource",
        "confusingsupplements.pay4resource": "ConfusingSupplements.IndustryComponents.Pay4Resource",
        "confusingsupplements.industrycomponents.pay4resource": "ConfusingSupplements.IndustryComponents.Pay4Resource",
        "confusingsupplements.empty": "ConfusingSupplements.IndustryComponents.Empty",
        "confusingsupplements.industrycomponents.empty": "ConfusingSupplements.IndustryComponents.Empty",
    }
    return aliases.get(normalized, value)


def is_supported_custom_component_type(component_type):
    normalized = str(normalize_component_type(component_type) or "").strip().lower()
    return normalized in SUPPORTED_CUSTOM_INDUSTRY_COMPONENT_TYPES


def _flag_spanless_passenger_stop(industry_id, component_id, converted, source_name=None):
    # Spanless passenger stops were dropped by an earlier converter pass
    # because FUSE's validator errored on them. The runtime now downgrades
    # that check to a warning (matching legacy AlinasMapMod behavior), so
    # the converter only annotates - the component flows through to FUSE
    # and loads as a virtual stop with no physical platform.
    if not isinstance(converted, dict):
        return
    if str(converted.get("type") or "").strip() != "passengerStop":
        return
    spans = converted.get("trackSpanIds")
    if isinstance(spans, list) and len(spans) > 0:
        return
    _warn(
        f"Industry '{industry_id}' component '{component_id}' is a passengerStop with no "
        "trackSpans; emitting as a virtual stop. Add 'trackSpans' in the legacy source to "
        "give it a physical platform.",
        file=source_name or "",
        concept="passenger-stop-spanless",
    )


def _make_component_sub_id(industry_id, component_id, converted, existing):
    raw = str(component_id or "").strip()
    if raw:
        return raw

    component_type = str(converted.get("type") or "").strip()
    if component_type == "formulaic":
        preferred = "formula"
    elif component_type == "repairTrack":
        preferred = "repair"
    elif component_type == "teamTrack":
        preferred = "teamtrack"
    elif converted.get("loadId"):
        preferred = str(converted.get("loadId"))
    elif converted.get("name"):
        preferred = str(converted.get("name"))
    else:
        preferred = "component"

    base = re.sub(r"[^0-9A-Za-z]+", "-", preferred.strip().lower()).strip("-") or "component"
    sub_id = base
    index = 2
    while sub_id in existing:
        sub_id = f"{base}-{index}"
        index += 1

    _info(
        f"Industry '{industry_id}' had a legacy component with a blank id; generated component id '{sub_id}'.",
        concept="industry-component-empty-id",
    )
    return sub_id


def convert_industry(industry_id, item, area_id=None, order=None, source_name=None):
    components = {}
    for component_id, component in (item.get("components") or {}).items():
        if isinstance(component, dict):
            converted = convert_component(component_id, component)
            sub_id = _make_component_sub_id(industry_id, component_id, converted, components)
            _flag_spanless_passenger_stop(industry_id, sub_id, converted, source_name)
            components[sub_id] = converted

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


def scenery_model_identifier(item):
    return (
        item.get("assetIdentifier")
        or item.get("model")
        or item.get("modelIdentifier")
        or item.get("prefabIdentifier")
        or item.get("prefab")
        or ""
    )


def convert_scenery(item):
    model = scenery_model_identifier(item)
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
    offset_y = item.get("offsetY", item.get("offsety"))
    if offset_y is None and handler == "StrangeCustoms.FlowyThingBuilder":
        # Strange Customs' FlowyData defaults OffsetY to -0.1. Preserve that
        # instead of letting FUSE deserialize the missing float as 0.
        offset_y = -0.1
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
        "offsetY": offset_y,
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


def _iter_progression_section_definitions(progression_root):
    if not isinstance(progression_root, dict):
        return

    top_sections = progression_root.get("sections")
    if isinstance(top_sections, dict):
        for section_id, section in top_sections.items():
            if isinstance(section, dict):
                yield str(section_id), section
    elif isinstance(top_sections, list):
        for section in top_sections:
            if not isinstance(section, dict):
                continue
            section_id = section.get("id") or section.get("identifier")
            if section_id:
                yield str(section_id), section

    for progression in (progression_root.get("progressions") or {}).values():
        if not isinstance(progression, dict):
            continue
        sections = progression.get("sections")
        if isinstance(sections, dict):
            for section_id, section in sections.items():
                if isinstance(section, dict):
                    yield str(section_id), section
        elif isinstance(sections, list):
            for section in sections:
                if not isinstance(section, dict):
                    continue
                section_id = section.get("id") or section.get("identifier")
                if section_id:
                    yield str(section_id), section


def _append_unique_id(container, field, item_id):
    if not item_id:
        return
    values = container.get(field)
    if values is None:
        values = []
    elif isinstance(values, dict):
        values = bool_dictionary_to_array(values) or []
    elif not isinstance(values, list):
        values = [values]

    text = str(item_id).strip()
    if text and not any(str(existing).lower() == text.lower() for existing in values):
        values.append(text)
    container[field] = values


def reconcile_progression_section_feature_aliases(rail):
    progression_root = (rail.get("progression") or {})
    map_features = progression_root.get("mapFeatures")
    if not isinstance(map_features, dict):
        return

    section_defs = {}
    for section_id, section in _iter_progression_section_definitions(progression_root):
        key = str(section_id or "").strip()
        if key:
            section_defs.setdefault(key, []).append(section)

    if not section_defs:
        return

    referenced_features = set()
    for feature in map_features.values():
        if not isinstance(feature, dict):
            continue
        for field in ("prerequisiteFeatureIds", "enableFeaturesOnUnlock", "disableFeaturesOnUnlock"):
            for feature_id in feature.get(field) or []:
                text = str(feature_id or "").strip()
                if text:
                    referenced_features.add(text)

    for feature_id in sorted(referenced_features):
        sections = section_defs.get(feature_id)
        if not sections or feature_id in map_features:
            continue

        first_section = sections[0]
        map_features[feature_id] = clean({
            "displayName": first_section.get("displayName") or feature_id,
            "description": first_section.get("description"),
            "initiallyEnabled": False,
        })
        for section in sections:
            _append_unique_id(section, "enableFeaturesOnUnlock", feature_id)
        _info(
            f"Progression map feature reference '{feature_id}' points to a section id; emitted a FUSE map-feature alias and enabled it when that section unlocks.",
            concept="progression-section-feature-alias",
        )


def _legacy_order_value(item):
    if not isinstance(item, dict) or "order" not in item:
        return None

    value = item.get("order")
    if value is None or isinstance(value, bool):
        return None

    try:
        return int(value)
    except (TypeError, ValueError):
        _warn(
            f"Legacy order value '{value}' is not an integer; falling back to source encounter order.",
            concept="invalid-order-value",
        )
        return None


def next_area_order(order_state, area_id, item=None):
    if order_state is None:
        return None

    area_orders = order_state.setdefault("area_orders", {})
    key = str(area_id or "").lower()
    explicit_order = _legacy_order_value(item)
    if explicit_order is not None:
        area_orders[key] = explicit_order
        return explicit_order

    if key in area_orders:
        return area_orders[key]

    # Area order is a global route/location order in game, not just a local
    # source-file encounter order. Generating 0, 1, 2... for every converted
    # mod made unrelated route packages all fight for the top of the Company
    # Locations list. If legacy did not provide an explicit area order, leave
    # it unset and let the runtime preserve normal scene/source creation order.
    return None


def next_industry_order(order_state, area_id, industry_id, item=None):
    if order_state is None:
        return None

    area_key = str(area_id or "__unassigned__").lower()
    industry_orders_by_area = order_state.setdefault("industry_orders_by_area", {})
    next_by_area = order_state.setdefault("next_industry_order_by_area", {})
    industry_orders = industry_orders_by_area.setdefault(area_key, {})
    industry_key = str(industry_id or "").lower()
    explicit_order = _legacy_order_value(item)
    if explicit_order is not None:
        industry_orders[industry_key] = explicit_order
        return explicit_order

    if industry_key in industry_orders:
        return industry_orders[industry_key]

    order = next_by_area.get(area_key, 0)
    next_by_area[area_key] = order + 1
    industry_orders[industry_key] = order
    return order


def _record_runtime_duplicate(order_state, kind, object_id, source_name):
    if order_state is None or not object_id:
        return

    registry = order_state.setdefault("runtime_ids", {})
    key = (str(kind), str(object_id).lower())
    if key not in registry:
        registry[key] = {
            "source": source_name or "",
        }
        return

    _info(
        f"Duplicate legacy {kind} id '{object_id}' in '{source_name}' also appeared in "
        f"'{registry[key].get('source')}'. Keeping the same FUSE id so the later mixinto updates/replaces the earlier runtime object.",
        file=source_name or "",
        concept=f"duplicate-{kind}-id",
    )


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
        area_order = next_area_order(order_state, area_id, area)
        rail["tracks"]["areas"][area_id] = convert_area(area_id, area, area_order)
        for industry_id, industry in (area.get("industries") or {}).items():
            if isinstance(industry, dict):
                industry_order = next_industry_order(order_state, area_id, industry_id, industry)
                rail["operations"]["industries"][industry_id] = convert_industry(industry_id, industry, area_id, industry_order, source_name)

    for industry_id, industry in (source.get("industries") or {}).items():
        if isinstance(industry, dict):
            area_id = industry.get("areaId") or industry.get("area")
            industry_order = next_industry_order(order_state, area_id, industry_id, industry)
            rail["operations"]["industries"][industry_id] = convert_industry(industry_id, industry, order=industry_order, source_name=source_name)

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
                converted = convert_turntable(spliney_id, spliney)
                _record_runtime_duplicate(order_state, "turntable", spliney_id, source_name)
                rail["operations"]["turntables"][spliney_id] = converted
            elif handler in LOADER_HANDLERS:
                converted = convert_loader(spliney)
                _record_runtime_duplicate(order_state, "loader", spliney_id, source_name)
                rail["operations"]["loaders"][spliney_id] = converted
            elif handler in STATION_HANDLERS:
                converted = convert_station(spliney)
                _record_runtime_duplicate(order_state, "station", spliney_id, source_name)
                rail["operations"]["stations"][spliney_id] = converted
            elif handler == MAP_LABEL_HANDLER:
                converted = convert_label(spliney_id, spliney)
                _record_runtime_duplicate(order_state, "map-label", spliney_id, source_name)
                rail["world"]["mapLabels"][spliney_id] = converted
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
    ensure_known_compat_loads(rail)


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

    reconcile_progression_section_feature_aliases(rail)


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
    _info(
        f"{len(uncovered)} track group(s) referenced by segments are not auto-enabled by any "
        f"progression with initiallyEnabled=true (e.g. {sample}{suffix}). FUSE will transiently "
        "enable those groups during staged graph apply, then restore progression/map-feature "
        "state after apply-progression.",
        concept="track-group-not-auto-enabled",
    )


def convert_mod(mod_folder, out_folder):
    mod_id, mod_name, version, author = meta(mod_folder)
    mixinto_sources, mixinto_order = mixinto_metadata(mod_folder)
    mixinto_order_index = {name: index for index, name in enumerate(mixinto_order)}
    source_files = sorted(
        (
            path for path in mod_folder.iterdir()
            if path.suffix.lower() == ".json"
            and path.name not in ("Definition.json", "Info.json")
            and not path.name.lower().endswith(".bak")
            and "signal" not in path.name.lower()
        ),
        key=lambda path: source_file_order(path, mixinto_order_index),
    )

    if not source_files:
        raise SystemExit(f"No source JSON files found in {mod_folder}")

    out_folder.mkdir(parents=True, exist_ok=True)
    converted_fragments = []
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
        converted_fragments.append((source_file.name, out_name, source_index, rail))

    repair_package_spans(converted_fragments)

    written = []
    written_order = {}
    written_counts = {}
    for source_name, out_name, source_index, rail in converted_fragments:
        save_json(out_folder / out_name, rail)
        written.append(out_name)
        written_order[out_name] = source_index
        counts = count_content(rail)
        written_counts[out_name] = counts
        summaries.append((source_name, out_name, counts))

    _emit_track_group_coverage_warning(declared_initial_groups)

    # Preserve the source-file conversion order. Earlier converter passes tried
    # to be clever and re-sort by content type ("track first", "world late"),
    # but legacy packages often rely on their own file-per-concern order and
    # modders expect a one-to-one source -> FUSE file mapping.
    rail_data_files = list(written)
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
    return 1, lower


def rail_data_file_order(name, source_order=1000000, counts=None):
    lower = name.lower()
    weight = rail_data_file_weight(lower, counts)
    return 0, weight, source_order, lower


def rail_data_file_weight(lower_name, counts=None):
    counts = counts or {}
    if counts:
        has_track = any(counts.get(key, 0) for key in (
            "tracks.nodes",
            "tracks.segments",
            "tracks.spans",
            "tracks.removals.nodes",
            "tracks.removals.segments",
            "tracks.removals.spans",
            "operations.turntables",
        ))
        has_loads = counts.get("operations.loads", 0) > 0
        has_industries = counts.get("operations.industries", 0) > 0 or counts.get("tracks.areas", 0) > 0
        has_loaders_or_stations = counts.get("operations.loaders", 0) > 0 or counts.get("operations.stations", 0) > 0
        has_progression = counts.get("progression.sections", 0) > 0 or counts.get("progression.progressions", 0) > 0 or counts.get("progression.mapFeatures", 0) > 0
        has_world = any(
            count > 0
            for key, count in counts.items()
            if key.startswith("world.") and key != "world.mapTiles"
        )
        if has_loads and not (has_track or has_industries or has_loaders_or_stations or has_world or has_progression):
            return 0
        if has_track:
            return 10
        if counts.get("world.mapTiles", 0) > 0:
            return 15
        if has_industries:
            return 20
        if has_loaders_or_stations:
            return 30
        if has_progression:
            return 40
        if has_world:
            return 50

    if "loads" in lower_name:
        return 0
    if any(token in lower_name for token in ("game-graph", "gamegraph", "graph", "track", "yard", "branch", "cutoff", "turntable")):
        return 10
    if any(token in lower_name for token in ("industry", "industries", "area", "town")):
        return 20
    if any(token in lower_name for token in ("loader", "station", "pax", "passenger")):
        return 30
    if any(token in lower_name for token in ("progression", "feature", "unlock")):
        return 40
    if any(token in lower_name for token in ("scenery", "spline", "road", "river", "mandela", "text", "label")):
        return 50
    return 90


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

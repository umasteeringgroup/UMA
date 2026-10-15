#!/usr/bin/env python3
# -*- coding: utf-8 -*-

bl_info = {
    "name": "UMA Tools",
    "author": "UMA Open Source",
    "version": (1, 0, 31),
    "blender": (4, 5, 0),
    "location": "View3D > Sidebar > UMA Tools",
    "description": "Quick checks and fixes for UMA export readiness.",
    "category": "3D View",
}

import bpy
import os
import bmesh
import math
import time
from mathutils import Matrix, kdtree


ADDON_VERSION_STR = "1.31"


def _ensure_object_mode(context, obj: bpy.types.Object):
    if obj is None:
        return
    if obj.mode == 'OBJECT':
        return
    prev_active = context.view_layer.objects.active
    try:
        context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode='OBJECT')
    except Exception:
        pass
    finally:
        context.view_layer.objects.active = prev_active


def _udim_tile_from_uv(uv) -> tuple[int, int]:
    # UDIM tiles are defined by integer offsets of UV space.
    # tile (0,0) corresponds to UDIM 1001.
    return (int(math.floor(uv[0])), int(math.floor(uv[1])))


def _udim_number_from_tile(tile_u: int, tile_v: int) -> int:
    # Standard UDIM numbering: 1001 + U + 10*V
    return 1001 + int(tile_u) + int(tile_v) * 10


def _unique_material_name(base_name: str) -> str:
    if bpy.data.materials.get(base_name) is None:
        return base_name
    i = 1
    while True:
        candidate = f"{base_name}.{i:03d}"
        if bpy.data.materials.get(candidate) is None:
            return candidate
        i += 1


def _is_udim_image(img: bpy.types.Image | None) -> bool:
    if img is None:
        return False
    try:
        if getattr(img, "source", None) == 'TILED':
            return True
    except Exception:
        pass

    try:
        fp = (getattr(img, "filepath", "") or "")
        if "<UDIM>" in fp:
            return True
    except Exception:
        pass

    return False


def _force_image_single_file(img: bpy.types.Image | None, abs_path: str):
    if img is None:
        return
    try:
        img.source = 'FILE'
    except Exception:
        pass
    for attr_name in ("filepath", "filepath_raw"):
        try:
            setattr(img, attr_name, abs_path)
        except Exception:
            pass
    try:
        # If this image has UDIM tiles, try to clear them.
        tiles = getattr(img, "tiles", None)
        if tiles is not None:
            try:
                tiles.clear()
            except Exception:
                pass
    except Exception:
        pass
    try:
        img.reload()
    except Exception:
        pass


def _try_open_image_no_udim(abs_path: str) -> bpy.types.Image | None:
    # Use operator-based open with UDIM detection disabled (if an IMAGE_EDITOR area exists).
    # This is best-effort; if it fails, callers should fall back to bpy.data.images.load.
    screen = getattr(bpy.context, "screen", None)
    if screen is None:
        return None

    img_area = None
    img_region = None
    swapped_area = None
    swapped_area_prev_type = None
    try:
        for area in screen.areas:
            if area is None:
                continue
            if area.type != 'IMAGE_EDITOR':
                continue
            img_area = area
            for r in area.regions:
                if r.type == 'WINDOW':
                    img_region = r
                    break
            break
    except Exception:
        img_area = None
        img_region = None

    # If no image editor exists, temporarily switch the first area.
    if img_area is None or img_region is None:
        try:
            swapped_area = next((a for a in screen.areas if a is not None), None)
            if swapped_area is not None:
                swapped_area_prev_type = swapped_area.type
                swapped_area.type = 'IMAGE_EDITOR'
                img_area = swapped_area
                img_region = next((r for r in swapped_area.regions if r.type == 'WINDOW'), None)
        except Exception:
            img_area = None
            img_region = None

    if img_area is None or img_region is None:
        return None

    before = set()
    try:
        for im in bpy.data.images:
            try:
                before.add(im.as_pointer())
            except Exception:
                pass
    except Exception:
        before = set()

    try:
        with bpy.context.temp_override(area=img_area, region=img_region):
            try:
                bpy.ops.image.open(
                    filepath=abs_path,
                    relative_path=False,
                    check_existing=False,
                    use_sequence_detection=False,
                    use_udim_detecting=False,
                )
            except TypeError:
                # Some builds use a slightly different arg name.
                bpy.ops.image.open(
                    filepath=abs_path,
                    relative_path=False,
                    check_existing=False,
                    use_sequence_detection=False,
                    use_udim_detection=False,
                )
    except Exception:
        return None
    finally:
        if swapped_area is not None and swapped_area_prev_type is not None:
            try:
                swapped_area.type = swapped_area_prev_type
            except Exception:
                pass

    # Pick the newest image that matches the path.
    candidates = []
    for im in bpy.data.images:
        try:
            if im.as_pointer() in before:
                continue
        except Exception:
            continue

        try:
            fp = bpy.path.abspath(getattr(im, "filepath", "") or "")
            if fp and os.path.normcase(fp) == os.path.normcase(abs_path):
                candidates.append(im)
        except Exception:
            pass

    if candidates:
        # Prefer non-UDIM image.
        for im in candidates:
            if not _is_udim_image(im):
                return im
        return candidates[-1]

    return None


def _load_single_tile_image(abs_path: str, cache: dict[str, bpy.types.Image]) -> bpy.types.Image | None:
    key = os.path.normcase(abs_path)
    if key in cache:
        return cache[key]

    img = None

    # Prefer operator open with UDIM detection disabled.
    try:
        img = _try_open_image_no_udim(abs_path)
    except Exception:
        img = None

    if img is not None:
        _force_image_single_file(img, abs_path)
        cache[key] = img
        return img

    try:
        img = bpy.data.images.load(abs_path, check_existing=False)
    except Exception:
        img = None

    if img is not None:
        _force_image_single_file(img, abs_path)

    if img is not None:
        cache[key] = img
    return img


def _insert_mapping_offset_for_image_node(nodes, links, image_node: bpy.types.Node, tile_u: int, tile_v: int, uv_map_name: str | None):
    if image_node is None:
        return

    vec_input = image_node.inputs.get('Vector')
    if vec_input is None:
        return

    mapping_node = nodes.new('ShaderNodeMapping')
    mapping_node.name = "UMA_UDIM_Mapping"
    mapping_node.label = "UMA UDIM Offset"
    try:
        mapping_node.vector_type = 'POINT'
    except Exception:
        pass
    try:
        mapping_node.inputs['Location'].default_value[0] = -float(tile_u)
        mapping_node.inputs['Location'].default_value[1] = -float(tile_v)
        mapping_node.inputs['Location'].default_value[2] = 0.0
    except Exception:
        pass

    # Wire: existing_vector -> mapping -> image
    if vec_input.is_linked and vec_input.links:
        existing_link = vec_input.links[0]
        from_socket = existing_link.from_socket
        links.remove(existing_link)
        links.new(from_socket, mapping_node.inputs['Vector'])
    else:
        if uv_map_name:
            uv_node = nodes.new('ShaderNodeUVMap')
            uv_node.uv_map = uv_map_name
            uv_out = uv_node.outputs.get('UV')
            if uv_out is not None:
                links.new(uv_out, mapping_node.inputs['Vector'])
        else:
            texcoord = nodes.new('ShaderNodeTexCoord')
            uv_out = texcoord.outputs.get('UV')
            if uv_out is not None:
                links.new(uv_out, mapping_node.inputs['Vector'])

    out_vec = mapping_node.outputs.get('Vector')
    if out_vec is not None:
        links.new(out_vec, vec_input)


def _convert_material_to_single_udim_tile(mat: bpy.types.Material, udim_number: int, tile_u: int, tile_v: int, uv_map_name: str | None, image_cache: dict[str, bpy.types.Image] | None = None):
    if mat is None or not mat.use_nodes or mat.node_tree is None:
        return (0, 0)

    converted = 0
    missing = 0
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links

    for node in nodes:
        if getattr(node, "type", None) != 'TEX_IMAGE':
            continue

        img = getattr(node, "image", None)
        if img is None:
            continue
        if getattr(img, "source", None) != 'TILED':
            continue

        tile = None
        try:
            tile = next((t for t in img.tiles if t.number == udim_number), None)
        except Exception:
            tile = None

        tile_path = None
        if tile is not None:
            tile_path = getattr(tile, "filepath", None) or getattr(tile, "path", None)

        if not tile_path:
            missing += 1
            continue

        abs_path = bpy.path.abspath(tile_path)
        if not abs_path or not os.path.exists(abs_path):
            missing += 1
            continue

        try:
            cache = image_cache if image_cache is not None else {}
            new_img = _load_single_tile_image(abs_path, cache)
            if new_img is None:
                missing += 1
                continue
            node.image = new_img
            _insert_mapping_offset_for_image_node(nodes, links, node, tile_u, tile_v, uv_map_name)
            converted += 1
        except Exception:
            missing += 1

    return (converted, missing)


def _clone_material_for_udim_tile(original: bpy.types.Material, udim_number: int, tile_u: int, tile_v: int, uv_map_name: str | None, image_cache: dict[str, bpy.types.Image]):
    if original is None:
        return None

    clone = original.copy()
    clone.name = _unique_material_name(f"{original.name}_UDIM{udim_number}")
    clone["uma_udim_split"] = True
    clone["uma_udim_original"] = original.name
    clone["uma_udim_tile"] = int(udim_number)
    clone["uma_udim_tile_u"] = int(tile_u)
    clone["uma_udim_tile_v"] = int(tile_v)

    converted, missing = _convert_material_to_single_udim_tile(clone, udim_number, tile_u, tile_v, uv_map_name, image_cache=image_cache)
    return (clone, converted, missing)


def _get_object_active_uv_name(mesh_obj: bpy.types.Object) -> str | None:
    if mesh_obj is None or mesh_obj.type != 'MESH' or mesh_obj.data is None:
        return None
    uv_layers = getattr(mesh_obj.data, "uv_layers", None)
    if not uv_layers:
        return None
    active = getattr(uv_layers, "active", None)
    if active is not None and getattr(active, "name", None):
        return active.name
    if len(uv_layers) > 0:
        return uv_layers[0].name
    return None


def _uma_default_export_path(suffix):
    if bpy.data.filepath:
        blend_dir = os.path.dirname(bpy.data.filepath)
        blend_name = os.path.splitext(os.path.basename(bpy.data.filepath))[0]
        return os.path.join(blend_dir, blend_name + suffix + ".fbx")

    tmp_dir = bpy.app.tempdir
    if tmp_dir:
        return os.path.join(tmp_dir, "uma" + suffix + ".fbx")

    return os.path.join(os.path.expanduser("~"), "uma" + suffix + ".fbx")


def _uma_fbx_export_kwargs(context, use_selection: bool):
    use_uma_2_format = getattr(context.window_manager, "uma_tools_export_uma2_format", False)
    axis_forward = 'Z' if use_uma_2_format else '-Z'
    axis_up = 'Y'
    primary_bone_axis = 'X' if use_uma_2_format else 'Y'
    secondary_bone_axis = '-Y' if use_uma_2_format else 'X'

    return {
        "use_selection": use_selection,
        "use_active_collection": False,
        "global_scale": 1.0,
        "apply_unit_scale": True,
        "apply_scale_options": 'FBX_SCALE_ALL',
        "bake_space_transform": False,
        "object_types": {'ARMATURE', 'MESH', 'EMPTY'},
        "use_mesh_modifiers": True,
        "use_mesh_modifiers_render": True,
        "mesh_smooth_type": 'OFF',
        "use_subsurf": False,
        "use_mesh_edges": False,
        "use_tspace": False,
        "use_custom_props": False,
        "add_leaf_bones": True,
        "primary_bone_axis": primary_bone_axis,
        "secondary_bone_axis": secondary_bone_axis,
        "armature_nodetype": 'NULL',
        "bake_anim": False,
        "bake_anim_use_all_bones": True,
        "bake_anim_use_nla_strips": True,
        "bake_anim_use_all_actions": True,
        "bake_anim_force_startend_keying": True,
        "bake_anim_step": 1.0,
        "bake_anim_simplify_factor": 1.0,
        "path_mode": 'COPY',
        "embed_textures": True,
        "batch_mode": 'OFF',
        "use_batch_own_dir": True,
        "use_metadata": True,
        "axis_forward": axis_forward,
        "axis_up": axis_up,
    }


def _iter_target_objects(context, selected_only: bool, visible_only: bool = False):
    if selected_only:
        objs = list(context.selected_objects)
    else:
        objs = list(context.scene.objects)

    # Only process meshes and armatures (bones) for UMA workflows.
    targets = [o for o in objs if o is not None and o.type in {'MESH', 'ARMATURE'}]
    if not visible_only:
        return targets

    visible_targets = []
    for obj in targets:
        try:
            if not obj.visible_get():
                continue
        except Exception:
            if getattr(obj, "hide_viewport", False):
                continue
        visible_targets.append(obj)
    return visible_targets


def _deselect_all_objects(context):
    for obj in context.view_layer.objects:
        if obj is not None and obj.select_get():
            obj.select_set(False)


def _get_selected_objects(context):
    return [obj for obj in context.view_layer.objects if obj is not None and obj.select_get()]


def _swap_left_right_name(name: str) -> str:
    if name.startswith("Left"):
        return "Right" + name[4:]
    if name.startswith("Right"):
        return "Left" + name[5:]
    return name


def _get_nonzero_vertex_group_names(mesh_obj: bpy.types.Object):
    if mesh_obj is None or mesh_obj.type != 'MESH':
        return []

    used_groups = set()
    for v in mesh_obj.data.vertices:
        for g in v.groups:
            if g.weight > 0.0:
                used_groups.add(g.group)

    names = []
    for idx in sorted(used_groups):
        if 0 <= idx < len(mesh_obj.vertex_groups):
            names.append(mesh_obj.vertex_groups[idx].name)
    return names


def _clear_vertex_groups(context, mesh_obj: bpy.types.Object):
    if mesh_obj is None or mesh_obj.type != 'MESH':
        return 0

    _ensure_object_mode(context, mesh_obj)

    removed = 0
    for idx in range(len(mesh_obj.vertex_groups) - 1, -1, -1):
        mesh_obj.vertex_groups.remove(mesh_obj.vertex_groups[idx])
        removed += 1

    return removed


def _remove_empty_vertex_groups(mesh_obj: bpy.types.Object):
    if mesh_obj is None or mesh_obj.type != 'MESH' or mesh_obj.data is None:
        return 0

    used_groups = set()
    for vertex in mesh_obj.data.vertices:
        for group in vertex.groups:
            if group.weight > 0.0001:
                used_groups.add(group.group)

    removed = 0
    for idx in range(len(mesh_obj.vertex_groups) - 1, -1, -1):
        if idx not in used_groups:
            mesh_obj.vertex_groups.remove(mesh_obj.vertex_groups[idx])
            removed += 1

    return removed


def _remove_negligible_vertex_weights(context, mesh_obj: bpy.types.Object, max_weight: float = 0.001):
    if mesh_obj is None or mesh_obj.type != 'MESH' or mesh_obj.data is None:
        return 0

    _ensure_object_mode(context, mesh_obj)

    removed = 0
    for vertex_group in list(mesh_obj.vertex_groups):
        remove_indices = []
        for vertex in mesh_obj.data.vertices:
            for assignment in vertex.groups:
                if assignment.group == vertex_group.index and assignment.weight <= max_weight:
                    remove_indices.append(vertex.index)
                    break

        if remove_indices:
            vertex_group.remove(remove_indices)
            removed += len(remove_indices)

    return removed


def _smooth_vertex_group_weights(context, mesh_obj: bpy.types.Object, factor: float = 0.25):
    if mesh_obj is None or mesh_obj.type != 'MESH' or mesh_obj.data is None:
        return (0, 0)

    _ensure_object_mode(context, mesh_obj)

    vertices = mesh_obj.data.vertices
    if not vertices or not mesh_obj.vertex_groups:
        return (0, 0)

    neighbors = [[] for _ in range(len(vertices))]
    for edge in mesh_obj.data.edges:
        v1, v2 = edge.vertices
        neighbors[v1].append(v2)
        neighbors[v2].append(v1)

    groups = list(mesh_obj.vertex_groups)
    if not groups:
        return (0, 0)

    original_totals = [0.0] * len(vertices)
    smoothed_weights = {group.index: [0.0] * len(vertices) for group in groups}

    for vertex in vertices:
        for assignment in vertex.groups:
            if assignment.group not in smoothed_weights:
                continue
            smoothed_weights[assignment.group][vertex.index] = assignment.weight
            original_totals[vertex.index] += assignment.weight

    for group in groups:
        weights = smoothed_weights[group.index]
        updated = [0.0] * len(vertices)

        for vertex in vertices:
            current_weight = weights[vertex.index]
            vertex_neighbors = neighbors[vertex.index]
            if vertex_neighbors:
                neighbor_avg = sum(weights[neighbor_index] for neighbor_index in vertex_neighbors) / len(vertex_neighbors)
                updated[vertex.index] = ((1.0 - factor) * current_weight) + (factor * neighbor_avg)
            else:
                updated[vertex.index] = current_weight

        smoothed_weights[group.index] = updated

    normalized_vertices = 0
    for vertex in vertices:
        original_total = original_totals[vertex.index]
        new_total = sum(smoothed_weights[group.index][vertex.index] for group in groups)

        if original_total > 0.0 and new_total > 0.0:
            scale = original_total / new_total
            normalized_vertices += 1
        else:
            scale = 0.0

        for group in groups:
            weight = smoothed_weights[group.index][vertex.index] * scale
            if weight > 0.0001:
                group.add([vertex.index], weight, 'REPLACE')
            else:
                group.remove([vertex.index])

    return (len(groups), normalized_vertices)


def _on_quick_select_index_update(self, context):
    obj = context.view_layer.objects.active
    if obj is None or obj.type != 'MESH':
        return

    idx = self.uma_tools_group_quick_select_index
    if idx < 0 or idx >= len(self.uma_tools_group_quick_select):
        return

    name = self.uma_tools_group_quick_select[idx].name
    vg = obj.vertex_groups.get(name)
    if vg is None:
        return

    obj.vertex_groups.active_index = vg.index


def _needs_transform_apply(obj: bpy.types.Object) -> bool:
    # Match Blender UI meaning: apply if not default.
    if obj is None:
        return False

    # Ignore non-transformable object types? Keep broad; user asked "objects".
    if obj.location.length_squared > 1e-12:
        return True

    # Quaternion compare: identity
    if abs(obj.rotation_quaternion.w - 1.0) > 1e-12:
        return True
    if obj.rotation_quaternion.x * obj.rotation_quaternion.x + obj.rotation_quaternion.y * obj.rotation_quaternion.y + obj.rotation_quaternion.z * obj.rotation_quaternion.z > 1e-12:
        return True

    if abs(obj.scale.x - 1.0) > 1e-12 or abs(obj.scale.y - 1.0) > 1e-12 or abs(obj.scale.z - 1.0) > 1e-12:
        return True

    return False


def _armature_hierarchy_issues(arm_obj: bpy.types.Object):
    issues = []
    if arm_obj is None or arm_obj.type != 'ARMATURE':
        return issues

    # Armature object naming convention
    if arm_obj.name != "Root":
        issues.append(f"ARMATURE: armature object must be named 'Root' (found '{arm_obj.name}')")

    arm_data = arm_obj.data
    if arm_data is None:
        return issues

    bones = arm_data.bones
    if bones is None:
        return issues

    # Validate top-level bones: exactly one root-level bone named "Global".
    top_level = [b for b in bones if b.parent is None]
    if len(top_level) != 1:
        issues.append(
            f"ARMATURE: '{arm_obj.name}' must have exactly 1 top-level bone (found {len(top_level)})"
        )
        return issues

    global_bone = top_level[0]
    if global_bone.name != "Global":
        issues.append(
            f"ARMATURE: '{arm_obj.name}' top-level bone must be named 'Global' (found '{global_bone.name}')"
        )
        return issues

    # Validate Global has exactly one child named "Position".
    global_children = list(global_bone.children)
    if len(global_children) != 1:
        issues.append(
            f"ARMATURE: '{arm_obj.name}' bone 'Global' must have exactly 1 child (found {len(global_children)})"
        )
        return issues

    if global_children[0].name != "Position":
        issues.append(
            f"ARMATURE: '{arm_obj.name}' bone 'Global' child must be named 'Position' (found '{global_children[0].name}')"
        )

    return issues


def _has_armature_modifier(mesh_obj: bpy.types.Object) -> bool:
    if mesh_obj is None or mesh_obj.type != 'MESH':
        return True

    for m in mesh_obj.modifiers:
        if m.type == 'ARMATURE':
            return True
    return False


def _armature_has_pose(arm_obj: bpy.types.Object) -> bool:
    if arm_obj is None or arm_obj.type != 'ARMATURE':
        return False

    pose_bones = arm_obj.pose.bones
    for pb in pose_bones:
        # Compare local pose transform to identity.
        # pb.matrix_basis is the delta from rest pose.
        mb: Matrix = pb.matrix_basis
        if not mb.is_identity:
            return True
    return False


def _refresh_report_text(context, lines):
    wm = context.window_manager
    wm.uma_tools_report_lines.clear()

    if lines:
        for s in lines:
            item = wm.uma_tools_report_lines.add()
            item.text = s
    else:
        item = wm.uma_tools_report_lines.add()
        item.text = "No issues found."


def _get_transform_object_name_from_line(line: str):
    if not line or not line.startswith("TRANSFORM:"):
        return None
    start = line.find("'")
    end = line.find("'", start + 1) if start >= 0 else -1
    if start >= 0 and end > start:
        return line[start + 1:end]
    return None


def _get_missing_armature_object_name_from_line(line: str):
    if not line or not line.startswith("MESH:"):
        return None
    if "missing an Armature modifier" not in line:
        return None
    start = line.find("'")
    end = line.find("'", start + 1) if start >= 0 else -1
    if start >= 0 and end > start:
        return line[start + 1:end]
    return None


class UMA_OT_tools_check_errors(bpy.types.Operator):
    bl_idname = "uma_tools.check_errors"
    bl_label = "Check for Errors"
    bl_options = {'REGISTER'}

    selected_only: bpy.props.BoolProperty(default=False)
    visible_only: bpy.props.BoolProperty(default=True)

    def execute(self, context):
        targets = _iter_target_objects(context, self.selected_only, self.visible_only)

        issues = []

        # Transform checks
        for obj in targets:
            if _needs_transform_apply(obj):
                issues.append(f"TRANSFORM: '{obj.name}' has unapplied transforms")

        # Armature pose checks
        for obj in targets:
            if obj.type == 'ARMATURE':
                if _armature_has_pose(obj):
                    issues.append(f"ARMATURE: '{obj.name}' has pose bones not in rest pose")

                issues.extend(_armature_hierarchy_issues(obj))

        # Mesh armature modifier checks
        for obj in targets:
            if obj.type == 'MESH':
                if not _has_armature_modifier(obj):
                    issues.append(f"MESH: '{obj.name}' is missing an Armature modifier")

        _refresh_report_text(context, issues)
        return {'FINISHED'}


class UMA_OT_tools_insert_global_position_bones(bpy.types.Operator):
    bl_idname = "uma_tools.insert_global_position_bones"
    bl_label = "Insert Global/Position bones"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        arm_obj = context.view_layer.objects.active
        if arm_obj is None or arm_obj.type != 'ARMATURE':
            arm_obj = next((o for o in context.selected_objects if o is not None and o.type == 'ARMATURE'), None)

        if arm_obj is None:
            self.report({'WARNING'}, "Select an armature (or make it the active object)")
            return {'CANCELLED'}

        arm = arm_obj.data
        if arm is None:
            self.report({'WARNING'}, "Selected armature has no data")
            return {'CANCELLED'}

        prev_mode = arm_obj.mode
        try:
            if prev_mode != 'EDIT':
                bpy.ops.object.mode_set(mode='EDIT')

            ebones = arm.edit_bones
            if ebones is None:
                self.report({'ERROR'}, "Unable to access edit bones")
                return {'CANCELLED'}

            # Capture current top-level bones BEFORE changes.
            top_level = [b for b in ebones if b.parent is None]

            # Reuse Global only if it's the top-level bone. If the top-level bone is named "global",
            # rename it to "Global".
            global_bone = None
            if len(top_level) == 1:
                if top_level[0].name == "Global":
                    global_bone = top_level[0]
                elif top_level[0].name == "global":
                    top_level[0].name = "Global"
                    global_bone = top_level[0]

            # Otherwise create a new Global at the top-level (do not touch any other "Global" deeper in the rig).
            if global_bone is None:
                global_bone = ebones.new("Global")
                global_bone.parent = None

            # Place Global at armature local origin with no transform (head at 0,0,0).
            global_bone.head = (0.0, 0.0, 0.0)
            global_bone.tail = (0.0, 0.0, 0.1)
            global_bone.roll = 0.0

            # Ensure Position is the first/only child of Global.
            # If Global already has a first child named "position", rename it to "Position".
            # If not, create a new "Position" (do not touch any other "Position" deeper in the rig).
            global_children = [b for b in ebones if b.parent == global_bone]
            position_bone = None
            if global_children:
                if global_children[0].name == "Position":
                    position_bone = global_children[0]
                elif global_children[0].name == "position":
                    global_children[0].name = "Position"
                    position_bone = global_children[0]

            if position_bone is None:
                position_bone = ebones.new("Position")
                position_bone.parent = global_bone

            # Place Position at origin too (aligned with Global). Keep a small length.
            position_bone.head = (0.0, 0.0, 0.0)
            position_bone.tail = (0.0, 0.0, 0.1)
            position_bone.roll = 0.0

            # Reparent any previous top-level bones (except Global) under Position.
            for b in top_level:
                if b == global_bone:
                    continue
                b.parent = position_bone

        finally:
            # Restore previous mode
            if prev_mode != arm_obj.mode:
                try:
                    bpy.ops.object.mode_set(mode=prev_mode)
                except Exception:
                    bpy.ops.object.mode_set(mode='OBJECT')

        self.report({'INFO'}, f"Inserted/validated Global -> Position on '{arm_obj.name}'")
        return {'FINISHED'}


class UMA_OT_tools_copy_weights_to_selected(bpy.types.Operator):
    bl_idname = "uma_tools.copy_weights_to_selected"
    bl_label = "Copy Weights to All Selected"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        wm = context.window_manager
        scene = context.scene
        source = getattr(wm, "uma_tools_weights_source", None)
        smooth_weights = getattr(wm, "uma_tools_smooth_weights", False)
        mapping = getattr(wm, "uma_tools_weight_mapping", 'POLYINTERP_NEAREST')
        if source is None or source.type != 'MESH':
            self.report({'WARNING'}, "Pick a mesh source object for weights")
            return {'CANCELLED'}

        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH' and o != source]
        if not targets:
            self.report({'WARNING'}, "No target meshes selected")
            return {'CANCELLED'}

        source_groups = _get_nonzero_vertex_group_names(source)

        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)
        removed_empty_total = 0

        try:
            for obj in targets:
                _clear_vertex_groups(context, obj)

                for group_name in source_groups:
                    if obj.vertex_groups.get(group_name) is None:
                        obj.vertex_groups.new(name=group_name)

                # Create Data Transfer modifier
                mod = obj.modifiers.new(name="UMA_CopyWeights", type='DATA_TRANSFER')
                mod.object = source

                # Vertex groups (weights)
                mod.use_vert_data = True
                mod.data_types_verts = {'VGROUP_WEIGHTS'}

                # Mapping as requested
                mod.vert_mapping = mapping

                # Mix settings
                mod.mix_mode = 'REPLACE'
                mod.mix_factor = 1.0

                # Apply modifier (requires object to be active)
                _deselect_all_objects(context)
                obj.select_set(True)
                context.view_layer.objects.active = obj
                bpy.ops.object.modifier_apply(modifier=mod.name)

                if smooth_weights:
                    _smooth_vertex_group_weights(context, obj, factor=0.25)

                removed_empty_total += _remove_empty_vertex_groups(obj)

        except RuntimeError as e:
            self.report({'ERROR'}, f"Copy weights failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            _deselect_all_objects(context)
            for o in prev_sel:
                if o and o.name in context.scene.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active

        if smooth_weights:
            self.report({'INFO'}, f"Copied and smoothed weights from '{source.name}' to {len(targets)} mesh(es), removed {removed_empty_total} empty vertex group(s)")
        else:
            self.report({'INFO'}, f"Copied weights from '{source.name}' to {len(targets)} mesh(es), removed {removed_empty_total} empty vertex group(s)")
        return {'FINISHED'}


def _uma_tools_mesh_object_poll(self, obj):
    return obj is not None and getattr(obj, "type", None) == 'MESH'


def _uma_tools_create_fit_cage(context, obj, points, name):
    # Use raw coordinates so pins, drivers, key mixes and modifiers cannot
    # override the shapes picked in the fitting panel.
    mesh = bpy.data.meshes.new(name)
    cage = None
    try:
        mesh.from_pydata(
            [point.co[:] for point in points],
            [edge.vertices[:] for edge in obj.data.edges],
            [polygon.vertices[:] for polygon in obj.data.polygons],
        )
        mesh.update()
        cage = bpy.data.objects.new(name, mesh)
        context.scene.collection.objects.link(cage)
        cage.matrix_world = obj.matrix_world.copy()
        cage.hide_render = True
        return cage
    except Exception:
        if cage is not None:
            bpy.data.objects.remove(cage, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        raise


def _uma_tools_remove_fit_cage(cage):
    if cage is not None:
        mesh = cage.data
        bpy.data.objects.remove(cage, do_unlink=True)
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


class UMA_OT_tools_deform_selected_to_shape_key(bpy.types.Operator):
    bl_idname = "uma_tools.deform_selected_to_shape_key"
    bl_label = "Deform selected to shape key"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        wm = context.window_manager
        source = getattr(wm, "uma_tools_blendshape_source", None)
        from_name = getattr(wm, "uma_tools_blendshape_from", "") or ""
        to_name = getattr(wm, "uma_tools_blendshape_to", "") or ""

        if source is None or source.type != 'MESH':
            self.report({'WARNING'}, "Pick a mesh source object")
            return {'CANCELLED'}
        if not from_name or not to_name:
            self.report({'WARNING'}, "Pick From and To blendshapes from the source object")
            return {'CANCELLED'}
        if from_name == to_name:
            self.report({'WARNING'}, "From and To blendshapes must be different")
            return {'CANCELLED'}

        shape_keys = getattr(source.data, "shape_keys", None)
        key_blocks = getattr(shape_keys, "key_blocks", None)
        if not key_blocks:
            self.report({'WARNING'}, f"Source object '{source.name}' has no blendshapes")
            return {'CANCELLED'}
        from_key = key_blocks.get(from_name)
        to_key = key_blocks.get(to_name)
        if from_key is None or to_key is None:
            self.report({'WARNING'}, "From/To blendshape not found on the source object")
            return {'CANCELLED'}

        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH' and o != source]
        if not targets:
            self.report({'WARNING'}, "No target meshes selected")
            return {'CANCELLED'}

        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)
        prev_mode = prev_active.mode if prev_active is not None else 'OBJECT'
        source_cage = None
        done = 0
        failed = []

        try:
            if context.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
            from_coordinates = [coordinate for point in from_key.data for coordinate in point.co]
            to_coordinates = [coordinate for point in to_key.data for coordinate in point.co]
            source_cage = _uma_tools_create_fit_cage(context, source, from_key.data, "UMA_FitSource")

            for obj in targets:
                target_cage = None
                original_data = obj.data
                copied_data = None
                output_key = None
                created_basis = None
                created_output = False
                old_output = None
                target_keys = obj.data.shape_keys
                original_values = {key.name: key.value for key in target_keys.key_blocks} if target_keys else {}
                original_active_key = obj.active_shape_key_index
                try:
                    if obj.library is not None:
                        raise RuntimeError("target object is linked and cannot be edited")
                    if target_keys and not target_keys.use_relative:
                        raise RuntimeError("target uses absolute shape keys; relative shape keys are required")
                    # Preserve the target's reference shape when fitting to a
                    # source key with the same name (usually To = Basis).
                    reference_name = target_keys.reference_key.name if target_keys else "Basis"
                    output_name = f"{to_name}_Fit" if to_name == reference_name else to_name

                    # Prefer a matching From key; otherwise the mesh's Basis is
                    # the garment already fitted to the selected source From.
                    bind_key = (target_keys.key_blocks.get(from_name) or target_keys.reference_key) if target_keys else None
                    bind_points = bind_key.data if bind_key is not None else obj.data.vertices
                    target_cage = _uma_tools_create_fit_cage(context, obj, bind_points, "UMA_FitTarget")
                    source_cage.data.vertices.foreach_set("co", from_coordinates)
                    source_cage.data.update()
                    mod = target_cage.modifiers.new(name="UMA_BlendshapeFit", type='SURFACE_DEFORM')
                    mod.target = source_cage

                    _deselect_all_objects(context)
                    target_cage.select_set(True)
                    context.view_layer.objects.active = target_cage
                    context.view_layer.update()
                    bind_result = bpy.ops.object.surfacedeform_bind(modifier=mod.name)
                    if 'FINISHED' not in bind_result or not mod.is_bound:
                        raise RuntimeError("Surface Deform bind failed")

                    source_cage.data.vertices.foreach_set("co", to_coordinates)
                    source_cage.data.update()
                    context.view_layer.update()
                    fitted = target_cage.evaluated_get(context.evaluated_depsgraph_get())
                    if len(fitted.data.vertices) != len(obj.data.vertices):
                        raise RuntimeError("Surface Deform changed the target vertex count")
                    coordinates = [coordinate for vertex in fitted.data.vertices for coordinate in vertex.co]
                    if not all(math.isfinite(coordinate) for coordinate in coordinates):
                        raise RuntimeError("Surface Deform produced invalid coordinates")

                    # Commit only after fitting succeeds. Shared meshes need
                    # their own output because objects can have different transforms.
                    if obj.data.users > 1 or obj.data.library is not None:
                        copied_data = obj.data.copy()
                        obj.data = copied_data
                    if obj.data.shape_keys is None:
                        created_basis = obj.shape_key_add(name="Basis", from_mix=False)
                    target_keys = obj.data.shape_keys
                    output_key = target_keys.key_blocks.get(output_name)
                    if output_key is not None:
                        old_output = (
                            [coordinate for point in output_key.data for coordinate in point.co],
                            output_key.relative_key, output_key.vertex_group, output_key.mute,
                            output_key.slider_min, output_key.slider_max,
                        )
                    else:
                        output_key = obj.shape_key_add(name=output_name, from_mix=False)
                        created_output = True
                    output_key.data.foreach_set("co", coordinates)
                    output_key.relative_key = target_keys.reference_key
                    output_key.vertex_group = ""
                    output_key.mute = False
                    output_key.slider_min = 0.0
                    output_key.slider_max = 1.0
                    for key in target_keys.key_blocks:
                        key.value = 1.0 if key == output_key else 0.0
                    obj.active_shape_key_index = target_keys.key_blocks.find(output_key.name)
                    obj.data.update()
                    done += 1
                except Exception as ex:
                    failed.append(f"{obj.name} ({ex})")
                    if copied_data is not None:
                        obj.data = original_data
                        if copied_data.users == 0:
                            bpy.data.meshes.remove(copied_data)
                    else:
                        if created_output:
                            obj.shape_key_remove(output_key)
                        elif old_output is not None:
                            output_key.data.foreach_set("co", old_output[0])
                            output_key.relative_key = old_output[1]
                            output_key.vertex_group = old_output[2]
                            output_key.mute = old_output[3]
                            output_key.slider_min = old_output[4]
                            output_key.slider_max = old_output[5]
                        if created_basis is not None:
                            obj.shape_key_remove(created_basis)
                    if copied_data is not None or output_key is not None or created_basis is not None:
                        if obj.data.shape_keys is not None:
                            for key in obj.data.shape_keys.key_blocks:
                                if key.name in original_values:
                                    key.value = original_values[key.name]
                        obj.active_shape_key_index = original_active_key
                finally:
                    _uma_tools_remove_fit_cage(target_cage)
        except Exception as ex:
            self.report({'ERROR'}, f"Blendshape fitting failed: {ex}")
            return {'CANCELLED'}
        finally:
            _uma_tools_remove_fit_cage(source_cage)
            _deselect_all_objects(context)
            for o in prev_sel:
                if o and o.name in context.scene.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active
            if prev_active is not None and prev_mode != 'OBJECT':
                bpy.ops.object.mode_set(mode=prev_mode)
            context.view_layer.update()

        if failed:
            self.report({'WARNING'}, f"Deformed {done} mesh(es); {len(failed)} failed: {', '.join(failed)}")
            return {'CANCELLED'} if done == 0 else {'FINISHED'}
        self.report({'INFO'}, f"Deformed {done} mesh(es) to blendshape '{to_name}'")
        return {'FINISHED'}


class UMA_OT_tools_process_rename_selected(bpy.types.Operator):
    bl_idname = "uma_tools.process_rename_selected"
    bl_label = "Process rename on selected"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        wm = context.window_manager
        prefix = (getattr(wm, "uma_tools_rename_prepend", "") or "")
        suffix = (getattr(wm, "uma_tools_rename_append", "") or "")

        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH']
        if not targets:
            self.report({'WARNING'}, "No mesh objects selected")
            return {'CANCELLED'}

        changed = 0
        for obj in targets:
            new_name = obj.name

            if prefix:
                if not new_name.startswith(prefix):
                    new_name = prefix + new_name

            if suffix:
                if not new_name.endswith(suffix):
                    new_name = new_name + suffix

            if new_name != obj.name:
                obj.name = new_name
                changed += 1

        self.report({'INFO'}, f"Renamed {changed} of {len(targets)} mesh(es)")
        return {'FINISHED'}


class UMA_OT_tools_remove_empty_vertex_groups(bpy.types.Operator):
    bl_idname = "uma_tools.remove_empty_vertex_groups"
    bl_label = "Remove empty vertex groups"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH']
        if not targets:
            self.report({'WARNING'}, "No mesh objects selected")
            return {'CANCELLED'}

        removed_total = 0

        for obj in targets:
            removed_total += _remove_empty_vertex_groups(obj)

        self.report({'INFO'}, f"Removed {removed_total} empty vertex group(s)")
        return {'FINISHED'}


class UMA_OT_tools_remove_negligible_weights(bpy.types.Operator):
    bl_idname = "uma_tools.remove_negligible_weights"
    bl_label = "Remove negligible weights"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH']
        if not targets:
            self.report({'WARNING'}, "No mesh objects selected")
            return {'CANCELLED'}

        removed_total = 0
        for obj in targets:
            removed_total += _remove_negligible_vertex_weights(context, obj, max_weight=0.001)

        self.report({'INFO'}, f"Removed {removed_total} negligible weight assignment(s)")
        return {'FINISHED'}


class UMA_OT_tools_strip_weights_and_rig_to_selected(bpy.types.Operator):
    bl_idname = "uma_tools.strip_weights_and_rig_to_selected"
    bl_label = "Strip weights and rig to selected"
    bl_description = "Remove all weights from the selected vertices and assign them fully to the active vertex group"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'ERROR'}, "Select a mesh object")
            return {'CANCELLED'}

        active_group = obj.vertex_groups.active
        if active_group is None:
            self.report({'ERROR'}, "No vertex group selected")
            return {'CANCELLED'}

        initial_mode = obj.mode
        if initial_mode == 'EDIT':
            bm = bmesh.from_edit_mesh(obj.data)
            selected_indices = [vert.index for vert in bm.verts if vert.select]
        else:
            selected_indices = [vert.index for vert in obj.data.vertices if vert.select]

        if not selected_indices:
            self.report({'ERROR'}, "No vertices selected")
            return {'CANCELLED'}

        group_name = active_group.name

        try:
            if obj.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')

            # Vertex-group membership can only be changed in Object Mode.
            for vertex_group in obj.vertex_groups:
                vertex_group.remove(selected_indices)
            active_group.add(selected_indices, 1.0, 'REPLACE')

        except (RuntimeError, TypeError) as e:
            self.report({'ERROR'}, f"Could not rig selected vertices: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != initial_mode:
                try:
                    bpy.ops.object.mode_set(mode=initial_mode)
                except Exception:
                    pass

        self.report(
            {'INFO'},
            f"Rigged {len(selected_indices)} vertex(es) 100% to bone '{group_name}'",
        )
        return {'FINISHED'}


def _normalize_vertex_weights_for_indices(obj: bpy.types.Object, vertex_indices):
    normalized = 0
    skipped = 0

    for idx in vertex_indices:
        vert = obj.data.vertices[idx]
        weighted_groups = [g for g in vert.groups if g.weight > 0.0 and g.group < len(obj.vertex_groups)]
        total = sum(g.weight for g in weighted_groups)

        if total <= 0.0:
            skipped += 1
            continue

        for g in weighted_groups:
            vg = obj.vertex_groups[g.group]
            vg.add([idx], g.weight / total, 'REPLACE')

        normalized += 1

    return normalized, skipped


class UMA_OT_tools_normalize_selected_weights(bpy.types.Operator):
    bl_idname = "uma_tools.normalize_selected_weights"
    bl_label = "Normalize selected"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object")
            return {'CANCELLED'}

        initial_mode = obj.mode

        # Read selected vertices first (especially when coming from Edit Mode).
        if initial_mode == 'EDIT':
            bm = bmesh.from_edit_mesh(obj.data)
            selected_indices = [v.index for v in bm.verts if v.select]
        else:
            selected_indices = [v.index for v in obj.data.vertices if v.select]

        if not selected_indices:
            self.report({'WARNING'}, "No selected vertices found")
            return {'CANCELLED'}

        normalized = 0
        skipped = 0

        try:
            if obj.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
            normalized, skipped = _normalize_vertex_weights_for_indices(obj, selected_indices)

        except RuntimeError as e:
            self.report({'ERROR'}, f"Normalize selected failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != initial_mode:
                try:
                    bpy.ops.object.mode_set(mode=initial_mode)
                except Exception:
                    pass

        if skipped:
            self.report({'INFO'}, f"Normalized {normalized} vertex(es); skipped {skipped} without weights")
        else:
            self.report({'INFO'}, f"Normalized {normalized} vertex(es)")
        return {'FINISHED'}


class UMA_OT_tools_normalize_all_weights(bpy.types.Operator):
    bl_idname = "uma_tools.normalize_all_weights"
    bl_label = "Normalize all"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object")
            return {'CANCELLED'}

        initial_mode = obj.mode
        all_indices = [v.index for v in obj.data.vertices]
        if not all_indices:
            self.report({'WARNING'}, "Active mesh has no vertices")
            return {'CANCELLED'}

        try:
            if obj.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
            normalized, skipped = _normalize_vertex_weights_for_indices(obj, all_indices)

        except RuntimeError as e:
            self.report({'ERROR'}, f"Normalize all failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != initial_mode:
                try:
                    bpy.ops.object.mode_set(mode=initial_mode)
                except Exception:
                    pass

        if skipped:
            self.report({'INFO'}, f"Normalized {normalized} vertex(es); skipped {skipped} without weights")
        else:
            self.report({'INFO'}, f"Normalized {normalized} vertex(es)")
        return {'FINISHED'}



# Pose baking deliberately evaluates every stored key, including Basis. Adding the
# Basis displacement to other keys would lose rotations/scales of their deltas.
def _uma_rest_pose_source(source):
    if source is None or source.type != 'MESH' or source.mode != 'OBJECT':
        raise ValueError("Select a mesh in Object Mode")
    arms = [m for m in source.modifiers if m.type == 'ARMATURE']
    if len(arms) != 1 or arms[0].object is None:
        raise ValueError("The mesh must have exactly one Armature modifier with a rig")
    arm = arms[0]
    rig = arm.object
    if not arm.show_viewport or not arm.show_render:
        raise ValueError("Enable the Armature modifier in both viewport and render")
    if rig.mode != 'OBJECT' or rig.data.pose_position != 'POSE':
        raise ValueError("Use Object Mode and set the armature to Pose Position")
    if arm.use_bone_envelopes or arm.use_multi_modifier:
        raise ValueError("Bone envelopes and multi-modifier skinning are not supported")
    if any(b.use_deform and b.bbone_segments > 1 for b in rig.data.bones):
        raise ValueError("Segmented deform B-Bones are not supported by this bake")
    for mod in source.modifiers:
        if mod == arm:
            break
        if mod.show_viewport or mod.show_render:
            raise ValueError("Move the Armature modifier before other enabled modifiers")
    if not source.data.vertices:
        raise ValueError("The selected mesh has no vertices")
    return rig, source.modifiers.find(arm.name)


def _uma_rest_pose_coords(points):
    import numpy as np
    result = np.empty(len(points) * 3, dtype=np.float32)
    points.foreach_get("co", result)
    return result


def _uma_rest_pose_evaluate(context, obj, world=True):
    """Return evaluated vertices and connectivity; always release temporary meshes."""
    import numpy as np
    obj.data.update()
    obj.update_tag()
    context.view_layer.update()
    evaluated = obj.evaluated_get(context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    try:
        coords = _uma_rest_pose_coords(mesh.vertices).reshape((-1, 3)).astype(np.float64)
        if world:
            matrix = np.array(evaluated.matrix_world, dtype=np.float64)
            coords = coords @ matrix[:3, :3].T + matrix[:3, 3]
        edges = np.empty(len(mesh.edges) * 2, dtype=np.int32)
        loops = np.empty(len(mesh.loops), dtype=np.int32)
        sizes = np.empty(len(mesh.polygons), dtype=np.int32)
        mesh.edges.foreach_get("vertices", edges)
        mesh.loops.foreach_get("vertex_index", loops)
        mesh.polygons.foreach_get("loop_total", sizes)
        return coords, (edges.tobytes(), loops.tobytes(), sizes.tobytes())
    finally:
        evaluated.to_mesh_clear()


def _uma_rest_pose_compare(before, after, label, tolerance, checks):
    import numpy as np
    a, topology_a = before
    b, topology_b = after
    if a.shape != b.shape or topology_a != topology_b:
        raise ValueError(f"Geometry check failed for {label}: topology changed")
    if not np.isfinite(a).all() or not np.isfinite(b).all():
        raise ValueError(f"Geometry check failed for {label}: non-finite coordinates")
    error = float(np.linalg.norm(a - b, axis=1).max(initial=0.0))
    if error > tolerance:
        raise ValueError(
            f"Geometry check failed for {label}: {error:.8g} world units "
            f"(tolerance {tolerance:.8g})"
        )
    checks.append({"shape": label, "max_error": error})


def _uma_rest_pose_clone(context, source, collection, owned, name):
    """Independent object/data, frozen object transform, without source parenting."""
    world = source.evaluated_get(context.evaluated_depsgraph_get()).matrix_world.copy()
    obj = source.copy()
    obj.data = source.data.copy()
    owned.append((obj, obj.data))
    collection.objects.link(obj)
    obj.name = name
    obj.data.name = name + "_Data"
    obj.animation_data_clear()
    for constraint in list(obj.constraints):
        obj.constraints.remove(constraint)
    obj.parent = None
    obj.matrix_world = world
    obj.hide_viewport = False
    obj.hide_render = False
    obj.hide_select = False
    obj.hide_set(False)
    return obj


def _uma_rest_pose_remove(owned):
    for obj, data in reversed(owned):
        bpy.data.objects.remove(obj, do_unlink=True)
        if data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Armature):
                bpy.data.armatures.remove(data)
    owned.clear()


def _uma_rest_pose_remap_keys(keys, mapping, actions):
    """Keep key animation and settings, with independent actions and local targets."""
    if not keys or not keys.animation_data:
        return
    animation = keys.animation_data
    copies = {}
    def copy_action(owner):
        action = owner.action
        if action:
            if action not in copies:
                copies[action] = action.copy()
                actions.append(copies[action])
            slot = getattr(owner, "action_slot", None)
            owner.action = copies[action]
            if slot is not None:
                for new_slot in owner.action.slots:
                    if new_slot.identifier == slot.identifier:
                        owner.action_slot = new_slot
                        break
    copy_action(animation)
    def copy_strips(strips):
        for strip in strips:
            copy_action(strip)
            if hasattr(strip, "strips"):
                copy_strips(strip.strips)
    for track in animation.nla_tracks:
        copy_strips(track.strips)
    for fcurve in animation.drivers:
        for variable in fcurve.driver.variables:
            for target in variable.targets:
                if target.id in mapping:
                    target.id = mapping[target.id]


def _uma_bake_pose_to_rest(context, source, tolerance=0.00001):
    """Transactional bake: originals are read-only, failed results are discarded."""
    import json
    import math as _math
    import numpy as np
    if not _math.isfinite(tolerance) or tolerance <= 0:
        raise ValueError("Geometry tolerance must be positive and finite")
    rig, arm_index = _uma_rest_pose_source(source)
    previous_active = context.view_layer.objects.active
    previous_selection = list(context.selected_objects)
    collection = bpy.data.collections.new(source.name + "_RestPose")
    context.scene.collection.children.link(collection)
    output, temporary, actions, checks = [], [], [], []
    success = False
    progress = context.window_manager
    progress.progress_begin(0, 100)
    try:
        depsgraph = context.evaluated_depsgraph_get()
        posed_rig = rig.evaluated_get(depsgraph)
        matrices = {p.name: p.matrix.copy() for p in posed_rig.pose.bones}
        rest_rig = _uma_rest_pose_clone(context, rig, collection, output, rig.name + "_RestPose")
        for bone in rest_rig.pose.bones:
            for constraint in list(bone.constraints):
                bone.constraints.remove(constraint)
        context.view_layer.update()
        # Parent matrices must be set before children, including constrained rigs.
        for bone in sorted(rest_rig.pose.bones, key=lambda p: len(p.parent_recursive)):
            bone.matrix = matrices[bone.name]
        context.view_layer.update()

        result = _uma_rest_pose_clone(context, source, collection, output, source.name + "_RestPose")
        world = result.matrix_world.copy()
        result.parent = rest_rig
        result.parent_type = 'OBJECT'
        result.matrix_parent_inverse = rest_rig.matrix_world.inverted()
        result.matrix_world = world
        result.modifiers[arm_index].object = rest_rig
        keys = source.data.shape_keys
        result_keys = result.data.shape_keys
        mapping = {source: result, rig: rest_rig, source.data: result.data, rig.data: rest_rig.data}
        if keys:
            mapping[keys] = result_keys
        # Remap other modifier object references that point to the original pair.
        for modifier in result.modifiers:
            for prop in modifier.bl_rna.properties:
                if prop.type == 'POINTER' and not prop.is_readonly:
                    value = getattr(modifier, prop.identifier, None)
                    if isinstance(value, bpy.types.ID) and value in mapping:
                        setattr(modifier, prop.identifier, mapping[value])
        _uma_rest_pose_remap_keys(result_keys, mapping, actions)

        probe = _uma_rest_pose_clone(context, source, collection, temporary, "_UMA_BakeProbe")
        probe.shape_key_clear()
        for index in reversed(range(len(probe.modifiers))):
            if index != arm_index:
                probe.modifiers.remove(probe.modifiers[index])
        # Probe keeps the original rig; each raw key is evaluated in the same pose.
        blocks = list(keys.key_blocks) if keys else [None]
        baked = []
        for index, key in enumerate(blocks):
            coords = _uma_rest_pose_coords(key.data if key else source.data.vertices)
            probe.data.vertices.foreach_set("co", coords)
            evaluated, topology = _uma_rest_pose_evaluate(context, probe, world=False)
            if len(evaluated) != len(source.data.vertices):
                raise ValueError("Armature deformation changed the vertex count")
            if not np.isfinite(evaluated).all():
                raise ValueError("Armature deformation produced non-finite coordinates")
            baked.append((key.name if key else "Basis", evaluated, topology))
            if result_keys:
                result_keys.key_blocks[index].data.foreach_set("co", evaluated.ravel())
            progress.progress_update(35 * (index + 1) / len(blocks))
        result.data.vertices.foreach_set("co", baked[0][1].ravel())
        result.data.update()

        for obj in list(context.selected_objects):
            obj.select_set(False)
        rest_rig.select_set(True)
        context.view_layer.objects.active = rest_rig
        with context.temp_override(object=rest_rig, active_object=rest_rig,
                                   selected_objects=[rest_rig], selected_editable_objects=[rest_rig]):
            bpy.ops.object.mode_set(mode='POSE')
            try:
                status = bpy.ops.pose.armature_apply(selected=False)
                if 'FINISHED' not in status:
                    raise RuntimeError("Blender could not apply the copied rig's rest pose")
            finally:
                bpy.ops.object.mode_set(mode='OBJECT')
        context.view_layer.update()

        # Validate every stored key against the *new* rest rig, independent of
        # slider values, mute flags, relative parents, and vertex group masks.
        probe.modifiers[0].object = rest_rig
        import numpy as np
        world_matrix = np.array(probe.matrix_world, dtype=np.float64)
        for index, (name, coords, topology) in enumerate(baked):
            stored = (result_keys.key_blocks[index].data if result_keys
                      else result.data.vertices)
            probe.data.vertices.foreach_set("co", _uma_rest_pose_coords(stored))
            expected = coords @ world_matrix[:3, :3].T + world_matrix[:3, 3]
            _uma_rest_pose_compare((expected, topology),
                                  _uma_rest_pose_evaluate(context, probe),
                                  "Stored key: " + name, tolerance, checks)
            progress.progress_update(35 + 25 * (index + 1) / len(blocks))

        # Use separate probes so testing sliders never changes either character.
        before = _uma_rest_pose_clone(context, source, collection, temporary, "_UMA_BeforeProbe")
        after = _uma_rest_pose_clone(context, result, collection, temporary, "_UMA_AfterProbe")
        for obj in (before, after):
            if obj.data.shape_keys:
                obj.data.shape_keys.animation_data_clear()
        def compare_shape(label):
            _uma_rest_pose_compare(_uma_rest_pose_evaluate(context, before),
                                   _uma_rest_pose_evaluate(context, after),
                                   label, tolerance, checks)
        compare_shape("Current mix / full modifier stack")
        if keys:
            before.show_only_shape_key = after.show_only_shape_key = False
            key_pair = (before.data.shape_keys, after.data.shape_keys)
            if keys.use_relative:
                for key_data in key_pair:
                    for key in key_data.key_blocks:
                        key.value = 0
                        key.mute = False
                compare_shape("Neutral relative mix")
                for index in range(1, len(blocks)):
                    original = blocks[index]
                    # Include endpoints to exercise negative/overdriven sliders.
                    for value in sorted(set((original.slider_min, original.slider_max, 1.0))):
                        for key_data in key_pair:
                            key = key_data.key_blocks[index]
                            key.slider_min = min(key.slider_min, value)
                            key.slider_max = max(key.slider_max, value)
                            key.value = value
                        compare_shape(f"Relative key: {original.name} @ {value:g}")
                    for key_data in key_pair:
                        key_data.key_blocks[index].value = 0
                    progress.progress_update(60 + 35 * index / max(1, len(blocks) - 1))
                for key_data in key_pair:
                    for index, key in enumerate(key_data.key_blocks):
                        if index:
                            key.value = min(key.slider_max, max(key.slider_min, 0.37))
                compare_shape("Combined relative mix")
            else:
                frames = sorted(set(key.frame for key in blocks))
                samples = frames + [(a + b) / 2 for a, b in zip(frames, frames[1:])]
                for frame in samples:
                    for key_data in key_pair:
                        key_data.eval_time = frame
                    compare_shape(f"Absolute time: {frame:g}")

        # Include live key drivers in the current display check.
        _uma_rest_pose_compare(_uma_rest_pose_evaluate(context, source),
                              _uma_rest_pose_evaluate(context, result),
                              "Current display including drivers", tolerance, checks)
        report = {
            "source_mesh": source.name, "source_rig": rig.name,
            "mesh": result.name, "rig": rest_rig.name,
            "shape_keys": len(blocks) if keys else 0, "checks": checks,
            "max_error": max(c["max_error"] for c in checks),
            "tolerance_world_units": tolerance, "blender": bpy.app.version_string,
        }
        collection["uma_rest_pose_validation"] = json.dumps(report)
        result["uma_rest_pose_source"] = source.name
        rest_rig["uma_rest_pose_source"] = rig.name
        success = True
        return result, rest_rig, collection, report
    finally:
        if context.object and context.object.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        _uma_rest_pose_remove(temporary)
        if not success:
            _uma_rest_pose_remove(output)
            for action in actions:
                if action.users == 0:
                    bpy.data.actions.remove(action)
            bpy.data.collections.remove(collection)
        for obj in list(context.selected_objects):
            obj.select_set(False)
        for obj in previous_selection:
            obj.select_set(True)
        context.view_layer.objects.active = previous_active
        progress.progress_end()


class UMA_OT_tools_bake_pose_to_rest_preserve_shape_keys(bpy.types.Operator):
    bl_idname = "uma_tools.bake_pose_to_rest_preserve_shape_keys"
    bl_label = "Bake Pose to Rest Pose — Preserve Shape Keys"
    bl_description = (
        "Create independent mesh and rig copies; bake every shape key into the "
        "current pose, apply the copied rig's rest pose, and verify all shapes. "
        "Originals stay unchanged. Copied rig/object animation and constraints "
        "are frozen at this frame; shape key animation is retained"
    )
    bl_options = {'REGISTER', 'UNDO'}

    tolerance: bpy.props.FloatProperty(
        name="Geometry tolerance",
        description="Maximum allowed vertex error in world-space Blender units",
        default=0.00001, min=0.0000001, soft_max=0.001, precision=7,
    )

    @classmethod
    def poll(cls, context):
        obj = context.active_object
        return obj is not None and obj.type == 'MESH' and context.mode == 'OBJECT'

    def execute(self, context):
        try:
            mesh, rig, collection, report = _uma_bake_pose_to_rest(
                context, context.active_object, self.tolerance)
        except Exception as exc:
            self.report({'ERROR'}, f"Rest pose bake cancelled: {exc}")
            return {'CANCELLED'}
        for obj in list(context.selected_objects):
            obj.select_set(False)
        mesh.select_set(True)
        rig.select_set(True)
        context.view_layer.objects.active = mesh
        self.report({'INFO'},
                    f"Created {collection.name}: {report['shape_keys']} keys, "
                    f"{len(report['checks'])} geometry checks passed; "
                    f"max error {report['max_error']:.3g}")
        return {'FINISHED'}



class UMA_OT_tools_reset_pose_transforms(bpy.types.Operator):
    bl_idname = "uma_tools.reset_pose_transforms"
    bl_label = "Reset pose transforms"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        armatures = [o for o in context.scene.objects if o is not None and o.type == 'ARMATURE']
        if not armatures:
            self.report({'WARNING'}, "No armatures found in the scene")
            return {'CANCELLED'}

        reset_bones = 0
        try:
            for arm in armatures:
                pose = getattr(arm, "pose", None)
                if pose is None:
                    continue
                for pose_bone in pose.bones:
                    # Reset the pose channels without relying on an active
                    # object, selection state, or a View3D operator context.
                    pose_bone.matrix_basis = Matrix.Identity(4)
                    reset_bones += 1

        except (AttributeError, RuntimeError, TypeError) as e:
            self.report({'ERROR'}, f"Reset pose transforms failed: {str(e)}")
            return {'CANCELLED'}

        self.report(
            {'INFO'},
            f"Reset {reset_bones} pose bone(s) on {len(armatures)} armature(s)",
        )
        return {'FINISHED'}


class UMA_OT_tools_select_edge_loops(bpy.types.Operator):
    bl_idname = "uma_tools.select_edge_loops"
    bl_label = "Select edge loops"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object to use edge loop selection")
            return {'CANCELLED'}

        prev_mode = obj.mode
        try:
            if prev_mode != 'EDIT':
                bpy.ops.object.mode_set(mode='EDIT')

            bpy.ops.mesh.select_mode(type='EDGE')
            bpy.ops.mesh.loop_select()

        except RuntimeError as e:
            self.report({'ERROR'}, f"Select edge loops failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != prev_mode:
                try:
                    bpy.ops.object.mode_set(mode=prev_mode)
                except Exception:
                    pass

        return {'FINISHED'}


class UMA_OT_tools_copy_weights_mirrored(bpy.types.Operator):
    bl_idname = "uma_tools.copy_weights_mirrored"
    bl_label = "Copy Weights Mirrored"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object to copy mirrored weights")
            return {'CANCELLED'}

        prev_mode = obj.mode
        try:
            if prev_mode != 'EDIT':
                bpy.ops.object.mode_set(mode='EDIT')

            bm = bmesh.from_edit_mesh(obj.data)
            selected_verts = [v for v in bm.verts if v.select]
            if not selected_verts:
                self.report({'WARNING'}, "No selected vertices found")
                return {'CANCELLED'}

            kd = kdtree.KDTree(len(bm.verts))
            for v in bm.verts:
                kd.insert(v.co, v.index)
            kd.balance()

            mirror_map = []
            for v in selected_verts:
                mirror_pos = v.co.copy()
                mirror_pos.x *= -1.0

                _co, mirror_index, dist = kd.find(mirror_pos)
                if dist > 1e-6:
                    mirror_map.append((v.index, None))
                else:
                    mirror_map.append((v.index, mirror_index))

            bpy.ops.object.mode_set(mode='OBJECT')

            mesh = obj.data
            copied = 0
            skipped = 0

            for src_index, mirror_index in mirror_map:
                if mirror_index is None:
                    skipped += 1
                    continue

                src_vert = mesh.vertices[src_index]
                for g in src_vert.groups:
                    src_group = obj.vertex_groups[g.group]
                    src_name = src_group.name
                    if src_name.startswith("Left"):
                        tgt_name = "Right" + src_name[4:]
                    elif src_name.startswith("Right"):
                        tgt_name = "Left" + src_name[5:]
                    else:
                        tgt_name = src_name

                    tgt_group = obj.vertex_groups.get(tgt_name)
                    if tgt_group is None:
                        tgt_group = obj.vertex_groups.new(name=tgt_name)

                    tgt_group.add([mirror_index], g.weight, 'REPLACE')

                copied += 1

        except RuntimeError as e:
            self.report({'ERROR'}, f"Copy mirrored weights failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != prev_mode:
                try:
                    bpy.ops.object.mode_set(mode=prev_mode)
                except Exception:
                    pass

        if skipped > 0:
            self.report({'INFO'}, f"Copied weights for {copied} vertex(es); skipped {skipped} without a mirror")
        else:
            self.report({'INFO'}, f"Copied weights for {copied} vertex(es)")
        return {'FINISHED'}


class UMA_OT_tools_add_current_vertex_group(bpy.types.Operator):
    bl_idname = "uma_tools.add_current_vertex_group"
    bl_label = "Add current vertex group"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object with an active vertex group")
            return {'CANCELLED'}

        vg = obj.vertex_groups.active
        if vg is None:
            self.report({'WARNING'}, "No active vertex group to add")
            return {'CANCELLED'}

        scene = context.scene
        for item in scene.uma_tools_group_quick_select:
            if item.name == vg.name:
                self.report({'INFO'}, f"'{vg.name}' already added")
                return {'CANCELLED'}

        new_item = scene.uma_tools_group_quick_select.add()
        new_item.name = vg.name
        scene.uma_tools_group_quick_select_index = len(scene.uma_tools_group_quick_select) - 1
        return {'FINISHED'}


class UMA_OT_tools_select_vertex_group(bpy.types.Operator):
    bl_idname = "uma_tools.select_vertex_group"
    bl_label = "Select"
    bl_options = {'REGISTER', 'UNDO'}

    group_name: bpy.props.StringProperty(default="")

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object")
            return {'CANCELLED'}

        vg = obj.vertex_groups.get(self.group_name)
        if vg is None:
            self.report({'WARNING'}, f"Vertex group '{self.group_name}' not found")
            return {'CANCELLED'}

        obj.vertex_groups.active_index = vg.index

        try:
            bpy.ops.object.vertex_group_select()
        except RuntimeError:
            prev_mode = obj.mode
            try:
                bpy.ops.object.mode_set(mode='EDIT')
                bpy.ops.object.vertex_group_select()
            except RuntimeError:
                pass
            finally:
                if obj.mode != prev_mode:
                    try:
                        bpy.ops.object.mode_set(mode=prev_mode)
                    except Exception:
                        pass

        return {'FINISHED'}


class UMA_OT_tools_select_vertex_group_opposite(bpy.types.Operator):
    bl_idname = "uma_tools.select_vertex_group_opposite"
    bl_label = "Opposite"
    bl_options = {'REGISTER', 'UNDO'}

    group_name: bpy.props.StringProperty(default="")

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object")
            return {'CANCELLED'}

        opposite_name = _swap_left_right_name(self.group_name)
        if opposite_name == self.group_name:
            self.report({'WARNING'}, "No Left/Right prefix to swap")
            return {'CANCELLED'}

        vg = obj.vertex_groups.get(opposite_name)
        if vg is None:
            self.report({'WARNING'}, f"Vertex group '{opposite_name}' not found")
            return {'CANCELLED'}

        obj.vertex_groups.active_index = vg.index

        return {'FINISHED'}


class UMA_OT_tools_select_all_vertices(bpy.types.Operator):
    bl_idname = "uma_tools.select_all_vertices"
    bl_label = "Select all vertexes"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object")
            return {'CANCELLED'}

        prev_mode = obj.mode
        try:
            if prev_mode != 'EDIT':
                bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
        except RuntimeError as e:
            self.report({'ERROR'}, f"Select all vertices failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != prev_mode:
                try:
                    bpy.ops.object.mode_set(mode=prev_mode)
                except Exception:
                    pass

        return {'FINISHED'}


class UMA_OT_tools_unselect_all_vertices(bpy.types.Operator):
    bl_idname = "uma_tools.unselect_all_vertices"
    bl_label = "Unselect all vertexes"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None or obj.type != 'MESH':
            self.report({'WARNING'}, "Select a mesh object")
            return {'CANCELLED'}

        prev_mode = obj.mode
        try:
            if prev_mode != 'EDIT':
                bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='DESELECT')
        except RuntimeError as e:
            self.report({'ERROR'}, f"Unselect all vertices failed: {str(e)}")
            return {'CANCELLED'}
        finally:
            if obj.mode != prev_mode:
                try:
                    bpy.ops.object.mode_set(mode=prev_mode)
                except Exception:
                    pass

        return {'FINISHED'}


class UMA_OT_tools_cursor_move_to_origin(bpy.types.Operator):
    bl_idname = "uma_tools.cursor_move_to_origin"
    bl_label = "Move to Origin"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        context.scene.cursor.location = (0.0, 0.0, 0.0)
        return {'FINISHED'}


class UMA_OT_tools_cursor_align_with_object(bpy.types.Operator):
    bl_idname = "uma_tools.cursor_align_with_object"
    bl_label = "Align with Object"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None:
            self.report({'WARNING'}, "Select an object to align the 3D Cursor")
            return {'CANCELLED'}

        context.scene.cursor.location = obj.matrix_world.translation
        return {'FINISHED'}


class UMA_OT_tools_remove_vertex_group_quick_select(bpy.types.Operator):
    bl_idname = "uma_tools.remove_vertex_group_quick_select"
    bl_label = "Remove"
    bl_options = {'REGISTER', 'UNDO'}

    index: bpy.props.IntProperty(default=-1)

    def execute(self, context):
        scene = context.scene
        if self.index < 0 or self.index >= len(scene.uma_tools_group_quick_select):
            return {'CANCELLED'}

        scene.uma_tools_group_quick_select.remove(self.index)
        scene.uma_tools_group_quick_select_index = min(
            scene.uma_tools_group_quick_select_index,
            max(0, len(scene.uma_tools_group_quick_select) - 1),
        )
        return {'FINISHED'}


class UMA_OT_tools_fix_transforms(bpy.types.Operator):
    bl_idname = "uma_tools.fix_transforms"
    bl_label = "Fix"
    bl_options = {'REGISTER', 'UNDO'}

    selected_only: bpy.props.BoolProperty(default=False)

    def execute(self, context):
        targets = _iter_target_objects(context, self.selected_only)

        # Apply transforms to objects that need it.
        # Do not apply armature pose to rest pose (explicitly required).
        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)

        try:
            for obj in targets:
                if obj is None:
                    continue

                if not _needs_transform_apply(obj):
                    continue

                # Blender ops require active + selected.
                _deselect_all_objects(context)
                obj.select_set(True)
                context.view_layer.objects.active = obj

                try:
                    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
                except RuntimeError:
                    # Ignore objects that can’t be applied due to mode/constraints
                    pass
        finally:
            # Restore selection
            _deselect_all_objects(context)
            for o in prev_sel:
                if o and o.name in context.scene.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active

        # Re-run checks after fix
        bpy.ops.uma_tools.check_errors(selected_only=self.selected_only)
        return {'FINISHED'}


class UMA_OT_tools_fix_transform_from_report(bpy.types.Operator):
    bl_idname = "uma_tools.fix_transform_from_report"
    bl_label = "Fix transform"
    bl_options = {'REGISTER', 'UNDO'}

    mode: bpy.props.EnumProperty(
        items=(
            ("SELECTED", "Selected", "Fix selected report item"),
            ("ALL", "All", "Fix all transform report items"),
        ),
        default="SELECTED",
    )

    def _apply_transform(self, context, obj):
        if obj is None or obj.type not in {'MESH', 'ARMATURE'}:
            return False
        if not _needs_transform_apply(obj):
            return False

        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)
        try:
            _deselect_all_objects(context)
            obj.select_set(True)
            context.view_layer.objects.active = obj
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        except RuntimeError:
            return False
        finally:
            _deselect_all_objects(context)
            for o in prev_sel:
                if o and o.name in context.scene.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active

        return True

    def execute(self, context):
        wm = context.window_manager
        lines = wm.uma_tools_report_lines

        if not lines:
            self.report({'WARNING'}, "No report items")
            return {'CANCELLED'}

        targets = []
        if self.mode == "SELECTED":
            if not (0 <= wm.uma_tools_report_index < len(lines)):
                self.report({'WARNING'}, "No report item selected")
                return {'CANCELLED'}
            name = _get_transform_object_name_from_line(lines[wm.uma_tools_report_index].text)
            if name:
                obj = context.scene.objects.get(name)
                if obj is not None:
                    targets.append(obj)
        else:
            seen = set()
            for item in lines:
                name = _get_transform_object_name_from_line(item.text)
                if name and name not in seen:
                    obj = context.scene.objects.get(name)
                    if obj is not None:
                        targets.append(obj)
                        seen.add(name)

        if not targets:
            self.report({'WARNING'}, "No transform items to fix")
            return {'CANCELLED'}

        fixed = 0
        for obj in targets:
            if self._apply_transform(context, obj):
                fixed += 1

        bpy.ops.uma_tools.check_errors(selected_only=False)
        self.report({'INFO'}, f"Fixed transforms on {fixed} object(s)")
        return {'FINISHED'}


class UMA_OT_tools_fix_missing_armature_modifiers(bpy.types.Operator):
    bl_idname = "uma_tools.fix_missing_armature_modifiers"
    bl_label = "Fix all missing Armature"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        wm = context.window_manager
        lines = wm.uma_tools_report_lines
        if not lines:
            self.report({'WARNING'}, "No report items")
            return {'CANCELLED'}

        root = context.scene.objects.get("Root")
        if root is None or root.type != 'ARMATURE':
            self.report({'ERROR'}, "Armature object named 'Root' not found")
            return {'CANCELLED'}

        targets = []
        seen = set()
        for item in lines:
            name = _get_missing_armature_object_name_from_line(item.text)
            if name and name not in seen:
                obj = context.scene.objects.get(name)
                if obj is not None and obj.type == 'MESH':
                    targets.append(obj)
                    seen.add(name)

        if not targets:
            self.report({'WARNING'}, "No missing Armature modifier items to fix")
            return {'CANCELLED'}

        fixed = 0
        for obj in targets:
            if _has_armature_modifier(obj):
                continue
            mod = obj.modifiers.new(name="Armature", type='ARMATURE')
            mod.object = root
            fixed += 1

        bpy.ops.uma_tools.check_errors(selected_only=False)
        self.report({'INFO'}, f"Added Armature modifier to {fixed} object(s)")
        return {'FINISHED'}


class UMA_OT_tools_select_all(bpy.types.Operator):
    bl_idname = "uma_tools.select_all"
    bl_label = "Select All"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        _deselect_all_objects(context)
        first_armature = None
        for obj in context.scene.objects:
            if obj is None:
                continue
            if obj.type in {'MESH', 'ARMATURE'}:
                obj.select_set(True)
                if first_armature is None and obj.type == 'ARMATURE':
                    first_armature = obj

        if first_armature is not None:
            context.view_layer.objects.active = first_armature

        return {'FINISHED'}


class UMA_OT_tools_parent_set_object(bpy.types.Operator):
    bl_idname = "uma_tools.parent_set_object"
    bl_label = "Set Parent (Object)"
    bl_options = {'REGISTER', 'UNDO'}

    keep_transform: bpy.props.BoolProperty(
        name="Keep Transform",
        description="Keep transform when parenting",
        default=True,
    )

    def execute(self, context):
        bpy.ops.object.parent_set(type='OBJECT', keep_transform=self.keep_transform)
        return {'FINISHED'}


class UMA_OT_tools_parent_clear(bpy.types.Operator):
    bl_idname = "uma_tools.parent_clear"
    bl_label = "Clear Parent"
    bl_options = {'REGISTER', 'UNDO'}

    clear_type: bpy.props.EnumProperty(
        name="Clear Type",
        items=[
            ('CLEAR', "Clear Parent", "Clear parent and keep transformation"),
            ('CLEAR_KEEP_TRANSFORM', "Clear and Keep Transform", "Clear parent and keep transform"),
            ('CLEAR_INVERSE', "Clear Parent Inverse", "Clear inverse parent matrix"),
        ],
        default='CLEAR_KEEP_TRANSFORM',
    )

    def execute(self, context):
        bpy.ops.object.parent_clear(type=self.clear_type)
        return {'FINISHED'}


class UMA_OT_tools_apply_all_transforms(bpy.types.Operator):
    bl_idname = "uma_tools.apply_all_transforms"
    bl_label = "Apply All Transforms"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        selected_objects = list(context.selected_objects)
        if not selected_objects:
            self.report({'WARNING'}, "No objects selected")
            return {'CANCELLED'}

        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)
        applied = 0

        try:
            for obj in selected_objects:
                if obj is None:
                    continue

                _ensure_object_mode(context, obj)
                _deselect_all_objects(context)
                obj.select_set(True)
                context.view_layer.objects.active = obj

                try:
                    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
                    applied += 1
                except RuntimeError as e:
                    self.report({'WARNING'}, f"Could not apply transforms to {obj.name}: {str(e)}")
        finally:
            _deselect_all_objects(context)
            for obj in prev_sel:
                if obj and obj.name in context.scene.objects:
                    obj.select_set(True)
            context.view_layer.objects.active = prev_active

        self.report({'INFO'}, f"Applied transforms to {applied} object(s)")
        return {'FINISHED'}


class UMA_OT_tools_generate_apply_data_transfer(bpy.types.Operator):
    bl_idname = "uma_tools.generate_apply_data_transfer"
    bl_label = "Generate Layers and Apply Data Transfer"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        selected_objects = list(context.selected_objects)
        if not selected_objects:
            self.report({'WARNING'}, "No objects selected")
            return {'CANCELLED'}

        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)
        objects_processed = 0
        modifiers_applied = 0

        try:
            for obj in selected_objects:
                if obj is None or obj.type != 'MESH':
                    continue

                _ensure_object_mode(context, obj)
                modifier_names = [mod.name for mod in obj.modifiers if mod.type == 'DATA_TRANSFER']
                if not modifier_names:
                    continue

                objects_processed += 1

                for modifier_name in modifier_names:
                    _deselect_all_objects(context)
                    obj.select_set(True)
                    context.view_layer.objects.active = obj

                    try:
                        bpy.ops.object.datalayout_transfer(modifier=modifier_name)
                        bpy.ops.object.modifier_apply(modifier=modifier_name)
                        modifiers_applied += 1
                    except RuntimeError as e:
                        self.report({'WARNING'}, f"Could not process Data Transfer modifier '{modifier_name}' on {obj.name}: {str(e)}")
                    except Exception as e:
                        self.report({'WARNING'}, f"Error processing {obj.name}: {str(e)}")
        finally:
            _deselect_all_objects(context)
            for obj in prev_sel:
                if obj and obj.name in context.scene.objects:
                    obj.select_set(True)
            context.view_layer.objects.active = prev_active

        if modifiers_applied > 0:
            self.report({'INFO'}, f"Processed {objects_processed} object(s), applied {modifiers_applied} Data Transfer modifier(s)")
            return {'FINISHED'}

        self.report({'WARNING'}, "No Data Transfer modifiers found on selected objects")
        return {'CANCELLED'}


class UMA_OT_tools_duplicate_object(bpy.types.Operator):
    bl_idname = "uma_tools.duplicate_object"
    bl_label = "Duplicate Objects"
    bl_options = {'REGISTER', 'UNDO'}

    def _get_target_object(self, context):
        target = getattr(context, "id", None)
        if isinstance(target, bpy.types.Object):
            return target

        target = getattr(context, "object", None)
        if isinstance(target, bpy.types.Object):
            return target

        target = getattr(context, "active_object", None)
        if isinstance(target, bpy.types.Object):
            return target

        return None

    def execute(self, context):
        target = self._get_target_object(context)
        if target is None:
            self.report({'WARNING'}, "No object available to duplicate")
            return {'CANCELLED'}

        prev_active = context.view_layer.objects.active
        prev_sel = _get_selected_objects(context)
        before_ptrs = set()
        for obj in context.scene.objects:
            try:
                before_ptrs.add(obj.as_pointer())
            except Exception:
                pass

        try:
            _ensure_object_mode(context, target)
            _deselect_all_objects(context)
            target.select_set(True)
            context.view_layer.objects.active = target
            bpy.ops.object.duplicate()
        except RuntimeError as e:
            self.report({'WARNING'}, f"Could not duplicate {target.name}: {str(e)}")
            return {'CANCELLED'}

        duplicate = None
        for obj in context.selected_objects:
            try:
                if obj.as_pointer() not in before_ptrs:
                    duplicate = obj
                    break
            except Exception:
                pass

        _deselect_all_objects(context)
        for obj in prev_sel:
            if obj and obj.name in context.scene.objects:
                obj.select_set(True)
        if duplicate is not None:
            duplicate.select_set(True)
            context.view_layer.objects.active = duplicate
            self.report({'INFO'}, f"Duplicated {target.name}")
        else:
            context.view_layer.objects.active = prev_active
            self.report({'INFO'}, f"Duplicated {target.name}")

        return {'FINISHED'}


class UMA_OT_tools_toggle_on(bpy.types.Operator):
    bl_idname = "uma_tools.toggle_on"
    bl_label = "Toggle On"
    bl_options = {'REGISTER', 'UNDO'}

    def _get_target_object(self, context):
        target = getattr(context, "id", None)
        if isinstance(target, bpy.types.Object):
            return target

        target = getattr(context, "object", None)
        if isinstance(target, bpy.types.Object):
            return target

        target = getattr(context, "active_object", None)
        if isinstance(target, bpy.types.Object):
            return target

        return None

    def _get_target_collection(self, context, target):
        if target is None:
            return None

        for attr_name in ("collection", "layer_collection"):
            collection = getattr(context, attr_name, None)
            if isinstance(collection, bpy.types.LayerCollection):
                collection = getattr(collection, "collection", None)

            if not isinstance(collection, bpy.types.Collection):
                continue

            try:
                if collection.objects.get(target.name) is not None:
                    return collection
            except Exception:
                pass

        users_collection = getattr(target, "users_collection", ())
        if users_collection:
            return users_collection[0]

        return None

    def execute(self, context):
        target = self._get_target_object(context)
        if target is None:
            self.report({'WARNING'}, "No object available to toggle on")
            return {'CANCELLED'}

        collection = self._get_target_collection(context, target)
        if collection is None:
            self.report({'WARNING'}, f"No collection found for {target.name}")
            return {'CANCELLED'}

        try:
            target.hide_viewport = False
        except Exception:
            pass

        try:
            target.hide_set(False)
        except Exception:
            pass

        hidden_count = 0
        for obj in collection.objects:
            if obj is None:
                continue

            if obj == target:
                try:
                    obj.hide_viewport = False
                except Exception:
                    pass
                try:
                    obj.hide_set(False)
                except Exception:
                    pass
                continue

            try:
                was_hidden = obj.hide_get()
            except Exception:
                was_hidden = False

            try:
                obj.hide_set(True)
                if not was_hidden:
                    hidden_count += 1
            except Exception:
                continue

        self.report({'INFO'}, f"Showing {target.name}; hid {hidden_count} other object(s) in {collection.name}")
        return {'FINISHED'}


class UMA_OT_tools_import_obj_uma(bpy.types.Operator):
    bl_idname = "uma_tools.import_obj_uma"
    bl_label = "Import Wavefront OBJ (UMA)"
    bl_options = {'REGISTER', 'UNDO'}

    filepath: bpy.props.StringProperty(subtype="FILE_PATH", default="")
    filter_glob: bpy.props.StringProperty(default="*.obj", options={'HIDDEN'})

    def execute(self, context):
        if not self.filepath:
            self.report({'WARNING'}, "No OBJ file selected")
            return {'CANCELLED'}

        import_material = getattr(context.scene, "uma_tools_import_material", None)

        before_ptrs = set()
        for obj in context.scene.objects:
            try:
                before_ptrs.add(obj.as_pointer())
            except Exception:
                pass

        try:
            bpy.ops.wm.obj_import(filepath=self.filepath)
        except Exception as e:
            self.report({'ERROR'}, f"OBJ import failed: {str(e)}")
            return {'CANCELLED'}

        imported_objects = []
        for obj in context.scene.objects:
            try:
                if obj.as_pointer() not in before_ptrs:
                    imported_objects.append(obj)
            except Exception:
                pass

        imported_meshes = [obj for obj in imported_objects if obj is not None and obj.type == 'MESH']
        if not imported_meshes:
            self.report({'WARNING'}, "Import finished, but no mesh objects were detected")
            return {'CANCELLED'}

        processed = 0
        smooth_failures = 0

        for obj in imported_meshes:
            _ensure_object_mode(context, obj)
            _deselect_all_objects(context)
            obj.select_set(True)
            context.view_layer.objects.active = obj

            if import_material is not None and obj.data is not None:
                mats = obj.data.materials
                if len(mats) == 0:
                    mats.append(import_material)
                    target_slot = 0
                else:
                    target_slot = -1
                    for idx, mat in enumerate(mats):
                        if mat == import_material:
                            target_slot = idx
                            break
                    if target_slot < 0:
                        mats[0] = import_material
                        target_slot = 0

                if hasattr(obj.data, "polygons"):
                    for poly in obj.data.polygons:
                        poly.material_index = target_slot

            import_scale = getattr(context.scene, "uma_tools_import_scale", (0.17, 0.17, 0.18))
            import_rotation = getattr(context.scene, "uma_tools_import_rotation", (0.0, 0.0, 0.0))
            import_location = getattr(context.scene, "uma_tools_import_location", (0.0, 0.089, 0.113))
            
            obj.scale = import_scale
            obj.rotation_euler = import_rotation
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            obj.location = import_location

            try:
                bpy.ops.object.mode_set(mode='EDIT')
                bpy.ops.mesh.select_all(action='SELECT')
                bpy.ops.mesh.faces_shade_smooth()
            except RuntimeError:
                smooth_failures += 1
            finally:
                if obj.mode != 'OBJECT':
                    try:
                        bpy.ops.object.mode_set(mode='OBJECT')
                    except Exception:
                        pass

            processed += 1

        _deselect_all_objects(context)
        for obj in imported_meshes:
            obj.select_set(True)
        context.view_layer.objects.active = imported_meshes[0]

        if smooth_failures:
            self.report({'INFO'}, f"Imported {processed} mesh(es); smooth faces failed on {smooth_failures} mesh(es)")
        else:
            self.report({'INFO'}, f"Imported {processed} mesh(es) with UMA import settings")
        return {'FINISHED'}

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}


class UMA_OT_tools_import_copy_from_current(bpy.types.Operator):
    bl_idname = "uma_tools.import_copy_from_current"
    bl_label = "Copy Transform from Current"
    bl_options = {'REGISTER'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None:
            self.report({'WARNING'}, "No active object selected")
            return {'CANCELLED'}

        scene = context.scene
        scene.uma_tools_import_scale = obj.scale
        scene.uma_tools_import_rotation = obj.rotation_euler
        scene.uma_tools_import_location = obj.location

        self.report({'INFO'}, f"Copied scale, rotation, and location from '{obj.name}'")
        return {'FINISHED'}


class UMA_OT_tools_import_paste_to_current(bpy.types.Operator):
    bl_idname = "uma_tools.import_paste_to_current"
    bl_label = "Paste Transform to Current"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        obj = context.view_layer.objects.active
        if obj is None:
            self.report({'WARNING'}, "No active object selected")
            return {'CANCELLED'}

        scene = context.scene
        import_scale = getattr(scene, "uma_tools_import_scale", (0.17, 0.17, 0.18))
        import_rotation = getattr(scene, "uma_tools_import_rotation", (0.0, 0.0, 0.0))
        import_location = getattr(scene, "uma_tools_import_location", (0.0, 0.089, 0.113))

        obj.scale = import_scale
        obj.rotation_euler = import_rotation
        obj.location = import_location

        self.report({'INFO'}, f"Pasted scale, rotation, and location to '{obj.name}'")
        return {'FINISHED'}


class UMA_OT_tools_import_fbx_uma(bpy.types.Operator):
    bl_idname = "uma_tools.import_fbx_uma"
    bl_label = "Import FBX (UMA)"
    bl_options = {'REGISTER', 'UNDO'}

    filepath: bpy.props.StringProperty(subtype="FILE_PATH", default="")
    filter_glob: bpy.props.StringProperty(default="*.fbx", options={'HIDDEN'})

    def execute(self, context):
        if not self.filepath:
            self.report({'WARNING'}, "No FBX file selected")
            return {'CANCELLED'}

        import_material = getattr(context.scene, "uma_tools_import_material", None)

        before_ptrs = set()
        for obj in context.scene.objects:
            try:
                before_ptrs.add(obj.as_pointer())
            except Exception:
                pass

        try:
            bpy.ops.import_scene.fbx(filepath=self.filepath)
        except Exception as e:
            self.report({'ERROR'}, f"FBX import failed: {str(e)}")
            return {'CANCELLED'}

        imported_objects = []
        for obj in context.scene.objects:
            try:
                if obj.as_pointer() not in before_ptrs:
                    imported_objects.append(obj)
            except Exception:
                pass

        imported_meshes = [obj for obj in imported_objects if obj is not None and obj.type == 'MESH']
        if not imported_meshes:
            self.report({'WARNING'}, "Import finished, but no mesh objects were detected")
            return {'CANCELLED'}

        processed = 0
        smooth_failures = 0

        for obj in imported_meshes:
            _ensure_object_mode(context, obj)
            _deselect_all_objects(context)
            obj.select_set(True)
            context.view_layer.objects.active = obj

            if import_material is not None and obj.data is not None:
                mats = obj.data.materials
                if len(mats) == 0:
                    mats.append(import_material)
                    target_slot = 0
                else:
                    target_slot = -1
                    for idx, mat in enumerate(mats):
                        if mat == import_material:
                            target_slot = idx
                            break
                    if target_slot < 0:
                        mats[0] = import_material
                        target_slot = 0

                if hasattr(obj.data, "polygons"):
                    for poly in obj.data.polygons:
                        poly.material_index = target_slot

            import_scale = getattr(context.scene, "uma_tools_import_scale", (0.17, 0.17, 0.18))
            import_rotation = getattr(context.scene, "uma_tools_import_rotation", (0.0, 0.0, 0.0))
            import_location = getattr(context.scene, "uma_tools_import_location", (0.0, 0.089, 0.113))
            
            obj.scale = import_scale
            obj.rotation_euler = import_rotation
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            obj.location = import_location

            try:
                bpy.ops.object.mode_set(mode='EDIT')
                bpy.ops.mesh.select_all(action='SELECT')
                bpy.ops.mesh.faces_shade_smooth()
            except RuntimeError:
                smooth_failures += 1
            finally:
                if obj.mode != 'OBJECT':
                    try:
                        bpy.ops.object.mode_set(mode='OBJECT')
                    except Exception:
                        pass

            processed += 1

        _deselect_all_objects(context)
        for obj in imported_meshes:
            obj.select_set(True)
        context.view_layer.objects.active = imported_meshes[0]

        if smooth_failures:
            self.report({'INFO'}, f"Imported {processed} mesh(es); smooth faces failed on {smooth_failures} mesh(es)")
        else:
            self.report({'INFO'}, f"Imported {processed} mesh(es) with UMA import settings")
        return {'FINISHED'}

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}


class UMA_OT_tools_fbx_export_all(bpy.types.Operator):
    bl_idname = "uma_tools.fbx_export_all"
    bl_label = "UMA FBX Export (All)"
    bl_options = {'REGISTER'}

    filepath: bpy.props.StringProperty(subtype="FILE_PATH", default="")
    filter_glob: bpy.props.StringProperty(default="*.fbx", options={'HIDDEN'})

    def execute(self, context):
        if not self.filepath:
            self.report({'WARNING'}, "No file path specified")
            return {'CANCELLED'}

        if not self.filepath.lower().endswith('.fbx'):
            self.filepath += '.fbx'

        try:
            bpy.ops.export_scene.fbx(filepath=self.filepath, **_uma_fbx_export_kwargs(context, use_selection=False))
            self.report({'INFO'}, f"Exported FBX: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        except Exception as e:
            self.report({'ERROR'}, f"Export failed: {str(e)}")
            return {'CANCELLED'}

    def invoke(self, context, event):
        self.filepath = _uma_default_export_path("_all")
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}


class UMA_OT_tools_fbx_export_selected(bpy.types.Operator):
    bl_idname = "uma_tools.fbx_export_selected"
    bl_label = "UMA FBX Export (Selected)"
    bl_options = {'REGISTER'}

    filepath: bpy.props.StringProperty(subtype="FILE_PATH", default="")
    filter_glob: bpy.props.StringProperty(default="*.fbx", options={'HIDDEN'})

    def execute(self, context):
        if not self.filepath:
            self.report({'WARNING'}, "No file path specified")
            return {'CANCELLED'}

        if not context.selected_objects:
            self.report({'WARNING'}, "No objects selected")
            return {'CANCELLED'}

        if not self.filepath.lower().endswith('.fbx'):
            self.filepath += '.fbx'

        try:
            bpy.ops.export_scene.fbx(filepath=self.filepath, **_uma_fbx_export_kwargs(context, use_selection=True))
            self.report({'INFO'}, f"Exported FBX: {os.path.basename(self.filepath)}")
            return {'FINISHED'}
        except Exception as e:
            self.report({'ERROR'}, f"Export failed: {str(e)}")
            return {'CANCELLED'}

    def invoke(self, context, event):
        self.filepath = _uma_default_export_path("_selected")
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}


class UMA_OT_tools_split_udims_to_textures(bpy.types.Operator):
    bl_idname = "uma_tools.split_udims_to_textures"
    bl_label = "Split UDIMS into separate textures"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH']
        if not targets:
            self.report({'WARNING'}, "No mesh objects selected")
            return {'CANCELLED'}

        prev_active = context.view_layer.objects.active
        prev_selected = _get_selected_objects(context)

        created_mats = 0
        reassigned_faces = 0
        converted_nodes = 0
        missing_tiles = 0
        multi_tile_faces = 0
        image_cache: dict[str, bpy.types.Image] = {}

        try:
            for obj in targets:
                if obj.data is None:
                    continue

                _ensure_object_mode(context, obj)

                uv_name = _get_object_active_uv_name(obj)
                if not uv_name:
                    self.report({'WARNING'}, f"'{obj.name}' has no UV layers; skipping")
                    continue
                uv_layer = obj.data.uv_layers.get(uv_name)
                if uv_layer is None:
                    continue

                polys = obj.data.polygons
                if not polys:
                    continue

                slot_materials = list(obj.data.materials)
                if not slot_materials:
                    continue

                # Cache: (orig_material_name, udim_number) -> slot_index
                new_slot_by_key: dict[tuple[str, int], int] = {}

                for slot_index, mat in enumerate(slot_materials):
                    if mat is None:
                        continue

                    tile_to_polys: dict[tuple[int, int], list[int]] = {}

                    for poly in polys:
                        if poly.material_index != slot_index:
                            continue

                        loop_uvs = []
                        for li in poly.loop_indices:
                            try:
                                loop_uvs.append(uv_layer.data[li].uv)
                            except Exception:
                                pass

                        if not loop_uvs:
                            continue

                        # Determine the face's UDIM tile from average UV.
                        u_avg = sum((uv.x for uv in loop_uvs), 0.0) / float(len(loop_uvs))
                        v_avg = sum((uv.y for uv in loop_uvs), 0.0) / float(len(loop_uvs))
                        tile_u, tile_v = _udim_tile_from_uv((u_avg, v_avg))

                        # Detect faces spanning multiple tiles (best-effort warning).
                        tiles_seen = set(_udim_tile_from_uv((uv.x, uv.y)) for uv in loop_uvs)
                        if len(tiles_seen) > 1:
                            multi_tile_faces += 1

                        tile_to_polys.setdefault((tile_u, tile_v), []).append(poly.index)

                    if not tile_to_polys:
                        continue

                    # Create per-tile materials and reassign faces.
                    for (tile_u, tile_v), poly_indices in tile_to_polys.items():
                        udim_number = _udim_number_from_tile(tile_u, tile_v)
                        key = (mat.name, udim_number)

                        if key not in new_slot_by_key:
                            clone_result = _clone_material_for_udim_tile(mat, udim_number, tile_u, tile_v, uv_name, image_cache)
                            if not clone_result:
                                continue

                            clone, cnv, miss = clone_result

                            obj.data.materials.append(clone)
                            new_slot_index = len(obj.data.materials) - 1
                            new_slot_by_key[key] = new_slot_index
                            created_mats += 1

                            converted_nodes += cnv
                            missing_tiles += miss

                        new_slot_index = new_slot_by_key[key]
                        for pi in poly_indices:
                            polys[pi].material_index = new_slot_index
                        reassigned_faces += len(poly_indices)

                obj.data.update()

        finally:
            _deselect_all_objects(context)
            for o in prev_selected:
                if o and o.name in context.scene.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active

        msg = f"Created {created_mats} material(s); reassigned {reassigned_faces} face(s)"
        if converted_nodes:
            msg += f"; converted {converted_nodes} tiled image node(s)"
        if multi_tile_faces:
            msg += f"; {multi_tile_faces} face(s) spanned multiple tiles (best-effort assigned)"
        if missing_tiles:
            msg += f"; {missing_tiles} tiled image node(s) missing tile files"
        self.report({'INFO'}, msg)
        return {'FINISHED'}


class UMA_OT_tools_reset_to_udim(bpy.types.Operator):
    bl_idname = "uma_tools.reset_to_udim"
    bl_label = "Reset to UDIM"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        targets = [o for o in context.selected_objects if o is not None and o.type == 'MESH']
        if not targets:
            self.report({'WARNING'}, "No mesh objects selected")
            return {'CANCELLED'}

        prev_active = context.view_layer.objects.active
        prev_selected = _get_selected_objects(context)

        restored_faces = 0
        removed_slots = 0
        deleted_materials = 0
        split_materials_to_delete = []

        try:
            for obj in targets:
                if obj.data is None:
                    continue

                _ensure_object_mode(context, obj)

                polys = obj.data.polygons
                if not polys:
                    continue

                # Build lookup of current slots -> materials
                materials = list(obj.data.materials)
                if not materials:
                    continue

                for mat in materials:
                    if mat is None:
                        continue
                    if bool(mat.get("uma_udim_split", False)) and mat not in split_materials_to_delete:
                        split_materials_to_delete.append(mat)

                # Ensure original materials exist in slots when needed.
                slot_index_by_mat_name: dict[str, int] = {}
                for idx, mat in enumerate(materials):
                    if mat is not None:
                        slot_index_by_mat_name[mat.name] = idx

                # Reassign faces from split clones back to original materials.
                for slot_index, mat in enumerate(materials):
                    if mat is None:
                        continue
                    if not bool(mat.get("uma_udim_split", False)):
                        continue

                    original_name = mat.get("uma_udim_original")
                    if not original_name:
                        continue
                    original_mat = bpy.data.materials.get(original_name)
                    if original_mat is None:
                        continue

                    if original_mat.name in slot_index_by_mat_name:
                        original_slot = slot_index_by_mat_name[original_mat.name]
                    else:
                        obj.data.materials.append(original_mat)
                        original_slot = len(obj.data.materials) - 1
                        slot_index_by_mat_name[original_mat.name] = original_slot

                    for poly in polys:
                        if poly.material_index == slot_index:
                            poly.material_index = original_slot
                            restored_faces += 1

                # Cleanup: remove unused slots (iterate in reverse and remap indices).
                used = [False] * len(obj.data.materials)
                for p in polys:
                    if 0 <= p.material_index < len(used):
                        used[p.material_index] = True

                for idx in range(len(obj.data.materials) - 1, -1, -1):
                    if used[idx]:
                        continue

                    obj.data.materials.pop(idx)
                    used.pop(idx)
                    removed_slots += 1

                    for p in polys:
                        if p.material_index > idx:
                            p.material_index -= 1

                obj.data.update()

            for mat in split_materials_to_delete:
                if mat is None:
                    continue
                if mat.name not in bpy.data.materials:
                    continue
                if getattr(mat, "users", 0) != 0:
                    continue
                try:
                    bpy.data.materials.remove(mat)
                    deleted_materials += 1
                except Exception:
                    pass

        finally:
            _deselect_all_objects(context)
            for o in prev_selected:
                if o and o.name in context.scene.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active

        self.report(
            {'INFO'},
            f"Restored {restored_faces} face(s); removed {removed_slots} unused slot(s); deleted {deleted_materials} split material(s)",
        )
        return {'FINISHED'}


# File memory estimates deliberately use data counts, not sys.getsizeof(), which
# only measures the small Python wrappers around Blender's native allocations.
_MEMORY_ATTRIBUTE_BYTES = {
    'FLOAT': 4, 'INT': 4, 'FLOAT_VECTOR': 12, 'FLOAT_COLOR': 16,
    'BYTE_COLOR': 4, 'STRING': 256, 'BOOLEAN': 1, 'FLOAT2': 8,
    'INT8': 1, 'INT16_2D': 4, 'INT32_2D': 8, 'QUATERNION': 16,
    'FLOAT4X4': 64,
}


def _memory_format(byte_count):
    value = float(byte_count)
    for unit in ('B', 'KiB', 'MiB', 'GiB', 'TiB'):
        if value < 1024.0 or unit == 'TiB':
            return f"{value:,.2f} {unit}" if unit != 'B' else f"{int(value):,} B"
        value /= 1024.0


def _memory_attributes(data, excluded=()):
    return sum(
        128 + len(attr.data) * _MEMORY_ATTRIBUTE_BYTES.get(attr.data_type, 16)
        for attr in getattr(data, 'attributes', ()) if attr.name not in excluded
    )


def _memory_node_tree(tree):
    if tree is None:
        return 0
    # Linked images and nested node groups are separate data blocks in the report.
    return (1024 + len(tree.links) * 64 + sum(
        512 + (len(node.inputs) + len(node.outputs)) * 128 for node in tree.nodes
    ))


def _memory_packed_bytes(data):
    packed_files = getattr(data, 'packed_files', None)
    if packed_files is not None:
        return sum(entry.packed_file.size for entry in packed_files if entry.packed_file)
    packed = getattr(data, 'packed_file', None)
    return packed.size if packed else 0


def _memory_image(image):
    packed = _memory_packed_bytes(image)
    buffers = list(image.tiles) if image.source == 'TILED' else [image]
    pixels_bytes = 0
    unknown = 0
    dimensions = []
    for buffer in buffers:
        width, height = buffer.size
        is_generated = (image.source == 'GENERATED'
                        or getattr(buffer, 'is_generated_tile', False))
        if (width <= 0 or height <= 0) and is_generated:
            width, height = buffer.generated_width, buffer.generated_height
        if width <= 0 or height <= 0:
            unknown += 1
            # No accessible dimensions: a clearly labeled 1K RGBA placeholder.
            width = height = 1024
        channels = max(1, buffer.channels or image.channels or 4)
        is_float = (image.is_float or getattr(buffer, 'use_generated_float', False))
        # Blender's byte image buffers use RGBA even for RGB source files.
        pixels_bytes += width * height * (channels * 4 if is_float else 4)
        dimensions.append(f"{width} x {height}")
    note = (f"{len(buffers)} UDIM tile(s)" if image.source == 'TILED'
            else (dimensions[0] if dimensions else 'No tiles'))
    note += '; decoded pixel buffer + image overhead'
    if image.source in {'MOVIE', 'SEQUENCE'}:
        note += '; one frame only'
    if unknown:
        note += f'; {unknown} unknown size(s): assumed 1024 x 1024 RGBA'
    if packed:
        note += f'; includes {_memory_format(packed)} packed source bytes'
    return 1024 + pixels_bytes + packed, note


def _memory_mesh(mesh):
    vertex_count, edge_count = len(mesh.vertices), len(mesh.edges)
    face_count, corner_count = len(mesh.polygons), len(mesh.loops)
    if mesh.is_editmode:
        edit_mesh = bmesh.from_edit_mesh(mesh)
        vertex_count, edge_count = len(edit_mesh.verts), len(edit_mesh.edges)
        face_count = len(edit_mesh.faces)
        corner_count = sum(len(face.loops) for face in edit_mesh.faces)
    size = 2048 + vertex_count * 12 + edge_count * 8 + corner_count * 8 + (face_count + 1) * 4
    # These built-in attributes hold the topology already counted above. UVs,
    # color attributes and other custom layers are counted exactly once here.
    size += _memory_attributes(mesh, {'position', '.edge_verts', '.corner_vert', '.corner_edge'})
    size += len(mesh.materials) * 8
    # Sampling keeps very large weighted meshes responsive. Weight memory is
    # extrapolated from evenly spaced vertices, and identified as an estimate.
    sample_count = min(len(mesh.vertices), 2048)
    weights = sum(len(mesh.vertices[i * len(mesh.vertices) // sample_count].groups)
                  for i in range(sample_count))
    if weights:
        size += vertex_count * 16 + round(weights * vertex_count / sample_count) * 8
    note = f"{vertex_count:,} vertices, {face_count:,} faces; topology + attributes"
    if weights:
        note += '; sampled vertex weights'
    if mesh.is_editmode:
        # BMesh uses linked element structures and larger per-element headers.
        size += vertex_count * 64 + edge_count * 64 + face_count * 64 + corner_count * 48
        note += '; edit-mode overhead estimated'
    return size, note + '; shape keys counted separately'


def _memory_estimate(data, kind):
    """Return estimated native bytes and a human-readable estimation basis."""
    if kind == 'images':
        return _memory_image(data)
    if kind == 'meshes':
        return _memory_mesh(data)
    if kind == 'objects':
        size = (1536 + len(data.modifiers) * 512 + len(data.constraints) * 256
                + len(data.vertex_groups) * 80 + len(data.material_slots) * 16)
        if data.pose:
            size += sum(512 + len(b.constraints) * 256 for b in data.pose.bones)
        return size, f"{data.type}; object, modifiers and pose overhead; linked data counted separately"
    if kind == 'shape_keys':
        return (1024 + sum(256 + len(key.data) * 12 for key in data.key_blocks),
                f"{len(data.key_blocks)} shape(s), including Basis; 12 bytes per point")
    if kind == 'node_groups':
        return _memory_node_tree(data), 'Nodes, sockets and links; referenced data counted separately'
    if kind in {'materials', 'worlds', 'lights', 'textures', 'scenes'}:
        size = 1024 + _memory_node_tree(getattr(data, 'node_tree', None))
        return size, 'Data-block and embedded node tree; referenced images and objects counted separately'
    if kind == 'armatures':
        return 1024 + len(data.bones) * 512, f"{len(data.bones)} bones; estimated bone structures"
    if kind == 'curves':
        size = 1024 + sum(128 + len(s.points) * 32 + len(s.bezier_points) * 72 for s in data.splines)
        body = getattr(data, 'body', '')
        return size + len(body.encode('utf-8')), 'Control points and text; evaluated geometry excluded'
    if kind == 'hair_curves':
        return (1024 + (len(data.curves) + 1) * 4 + _memory_attributes(data),
                'Curve offsets and point/curve attributes')
    if kind == 'pointclouds':
        return 1024 + _memory_attributes(data), 'Point attributes, including positions and radii'
    if kind == 'lattices':
        return 1024 + len(data.points) * 32, f"{len(data.points)} lattice points"
    if kind == 'metaballs':
        return 1024 + len(data.elements) * 128, 'Metaball elements; evaluated geometry excluded'
    if kind == 'collections':
        return 512 + (len(data.objects) + len(data.children)) * 16, 'Collection and member references'
    if kind == 'texts':
        size = 512 + sum(64 + len(line.body.encode('utf-8')) for line in data.lines)
        return size, f"{len(data.lines)} text lines and UTF-8 contents"
    if kind == 'actions':
        curves = list(getattr(data, 'fcurves', ()))
        for layer in getattr(data, 'layers', ()):
            for strip in layer.strips:
                for bag in getattr(strip, 'channelbags', ()):
                    curves.extend(bag.fcurves)
        unique = {curve.as_pointer(): curve for curve in curves}
        size = 1024 + sum(256 + len(c.keyframe_points) * 72 + len(c.sampled_points) * 8
                          for c in unique.values())
        return size, f"{len(unique)} animation curves; keyframes and samples"
    # Blender does not expose allocation sizes for every ID type. Keep these
    # visible, with an explicit coarse fallback instead of implying zero usage.
    packed = _memory_packed_bytes(data)
    return 1024 + packed, 'Coarse 1 KiB data-block estimate + packed bytes; runtime caches unknown'


def _memory_snapshot():
    """Scan the whole file (including orphan/linked IDs), without scene changes."""
    groups = []
    seen = set()
    # UI state is not an asset, and all_ids (newer Blender) duplicates every ID.
    excluded = {'all_ids', 'window_managers', 'screens', 'workspaces'}
    labels = {'images': 'Textures / Images', 'textures': 'Texture Data-blocks'}
    icons = {'objects': 'OBJECT_DATA', 'meshes': 'MESH_DATA', 'images': 'IMAGE_DATA',
             'textures': 'TEXTURE', 'materials': 'MATERIAL', 'armatures': 'ARMATURE_DATA',
             'shape_keys': 'SHAPEKEY_DATA', 'node_groups': 'NODETREE'}
    for prop in bpy.data.bl_rna.properties:
        if prop.type != 'COLLECTION' or prop.identifier in excluded:
            continue
        entries = []
        for data in getattr(bpy.data, prop.identifier):
            if not isinstance(data, bpy.types.ID) or data.as_pointer() in seen:
                continue
            seen.add(data.as_pointer())
            try:
                size, note = _memory_estimate(data, prop.identifier)
            except Exception as exc:
                size, note = 1024, f"Coarse 1 KiB fallback; estimate unavailable ({type(exc).__name__})"
            name = data.name
            if data.library:
                name += f" [{os.path.basename(data.library.filepath)}]"
            note += f"; {data.users} user(s)"
            entries.append((name, max(0, int(size)), note))
        if entries:
            entries.sort(key=lambda entry: (-entry[1], entry[0].casefold()))
            groups.append((labels.get(prop.identifier, prop.name),
                           icons.get(prop.identifier, 'FILE_BLEND'), entries))
    groups.sort(key=lambda group: (-sum(entry[1] for entry in group[2]), group[0]))
    return groups


class UMA_ToolsMemoryItem(bpy.types.PropertyGroup):
    name: bpy.props.StringProperty()
    # Strings preserve integer totals beyond the 32-bit RNA IntProperty limit.
    byte_count: bpy.props.StringProperty(default="0")
    memory_label: bpy.props.StringProperty()
    detail: bpy.props.StringProperty()
    icon_name: bpy.props.StringProperty(default='FILE_BLEND')
    parent_index: bpy.props.IntProperty(default=-1)
    is_group: bpy.props.BoolProperty(default=False)
    expanded: bpy.props.BoolProperty(default=False, options={'SKIP_SAVE'})


class UMA_UL_tools_memory(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        split = layout.split(factor=0.76)
        row = split.row(align=True)
        if item.is_group:
            row.prop(item, 'expanded', text='', emboss=False,
                     icon='TRIA_DOWN' if item.expanded or data.search else 'TRIA_RIGHT')
        else:
            row.label(text='', icon='BLANK1')
        row.label(text=item.name, icon=item.icon_name)
        amount = split.row()
        amount.alignment = 'RIGHT'
        amount.label(text=item.memory_label)

    def filter_items(self, context, data, propname):
        items = getattr(data, propname)
        query = data.search.strip().casefold()
        flags = [0] * len(items)
        for index, item in enumerate(items):
            if item.is_group:
                if not query or query in item.name.casefold():
                    flags[index] = self.bitflag_filter_item
            else:
                parent = items[item.parent_index]
                if query:
                    visible = query in item.name.casefold() or query in parent.name.casefold()
                else:
                    visible = parent.expanded
                if visible:
                    flags[index] = self.bitflag_filter_item
                    flags[item.parent_index] = self.bitflag_filter_item
        return flags, []

    def draw_filter(self, context, layout):
        # Sorting individual rows would break the parent/child hierarchy.
        pass


class UMA_OT_tools_view_memory_usage(bpy.types.Operator):
    bl_idname = "uma_tools.view_memory_usage"
    bl_label = "View memory usage"
    bl_description = "Estimate memory used by every asset in the current file, grouped by type"
    bl_options = {'INTERNAL'}

    items: bpy.props.CollectionProperty(type=UMA_ToolsMemoryItem, options={'SKIP_SAVE'})
    active_index: bpy.props.IntProperty(default=0, options={'SKIP_SAVE'})
    search: bpy.props.StringProperty(name="Search", options={'SKIP_SAVE', 'TEXTEDIT_UPDATE'})
    total_label: bpy.props.StringProperty(options={'SKIP_SAVE'})
    file_label: bpy.props.StringProperty(options={'SKIP_SAVE'})

    def _populate(self):
        self.items.clear()
        self.search = ''
        self.active_index = 0
        total = 0
        count = 0
        for title, icon, entries in _memory_snapshot():
            subtotal = sum(entry[1] for entry in entries)
            parent_index = len(self.items)
            group = self.items.add()
            group.name = f"{title} ({len(entries):,})"
            group.is_group = True
            group.icon_name = icon
            group.byte_count = str(subtotal)
            group.memory_label = '~ ' + _memory_format(subtotal)
            group.detail = f"{len(entries):,} data-block(s); {_memory_format(subtotal)} estimated total"
            for name, size, note in entries:
                item = self.items.add()
                item.name = name
                item.parent_index = parent_index
                item.icon_name = icon
                item.byte_count = str(size)
                item.memory_label = '~ ' + _memory_format(size)
                item.detail = note
            total += subtotal
            count += len(entries)
        self.total_label = f"Estimated total: {_memory_format(total)}  |  {count:,} data-blocks"
        self.file_label = os.path.basename(bpy.data.filepath) or 'Unsaved file'

    def invoke(self, context, event):
        self._populate()
        return context.window_manager.invoke_props_dialog(self, width=850, confirm_text="Close")

    def draw(self, context):
        layout = self.layout
        layout.label(text=self.file_label, icon='FILE_BLEND')
        layout.label(text=self.total_label)
        layout.prop(self, 'search', text='', icon='VIEWZOOM')
        layout.template_list('UMA_UL_tools_memory', '', self, 'items', self, 'active_index',
                             item_dyntip_propname='detail', rows=16, maxrows=16, sort_lock=True)
        if 0 <= self.active_index < len(self.items):
            item = self.items[self.active_index]
            box = layout.box()
            box.label(text=f"{item.name}: {item.memory_label}")
            # Wrap notes without making the dialog wider than the screen.
            import textwrap
            for line in textwrap.wrap(item.detail, width=105):
                box.label(text=line)
        layout.label(text="~ Estimated file data; shared assets counted once. Search keeps full-file totals.", icon='INFO')
        layout.label(text="Excludes GPU copies, undo, render/simulation caches and evaluated modifiers.")
        layout.label(text="Snapshot at opening; reopen to refresh. Expand a type to see individual assets.")

    def execute(self, context):
        return {'FINISHED'}


class UMA_PT_tools_panel(bpy.types.Panel):
    bl_label = "UMA Tools"
    bl_idname = "UMA_PT_tools_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'UMA Tools'

    def draw(self, context):
        layout = self.layout
        wm = context.window_manager
        scene = context.scene

        row = layout.row()
        row.label(text=f"Version: {ADDON_VERSION_STR}")

        layout.separator()

        def _fold_text(name, expanded):
            if expanded:
                return "\u25BC " + name
            return "\u25B6 " + name

        def _draw_fold_header(parent, prop_name, title):
            row = parent.row(align=True)
            row.alignment = 'LEFT'
            expanded = getattr(wm, prop_name)
            row.prop(wm, prop_name, text=_fold_text(title, expanded), emboss=False)

        # Error checking
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_error_checking", "Error checking")
        if wm.uma_tools_section_error_checking:
            col = box.column(align=True)
            col.prop(wm, "uma_tools_error_check_visible_only", text="Visible Only")
            op = col.operator(UMA_OT_tools_check_errors.bl_idname, text="Check for Errors")
            op.selected_only = False
            op.visible_only = wm.uma_tools_error_check_visible_only

            row = col.row(align=True)
            row.operator(UMA_OT_tools_select_all.bl_idname, text="Select All")
            op = row.operator(UMA_OT_tools_fix_transforms.bl_idname, text="Apply Transforms")
            op.selected_only = True

            col.operator(UMA_OT_tools_insert_global_position_bones.bl_idname, text="Insert Global/Position bones")

            col.separator()
            col.label(text="Report:")
            row = col.row()
            row.template_list(
                "UMA_UL_tools_report",
                "",
                wm,
                "uma_tools_report_lines",
                wm,
                "uma_tools_report_index",
                rows=6,
            )
            row = col.row(align=True)
            row.operator(UMA_OT_tools_fix_missing_armature_modifiers.bl_idname, text="Fix all missing Armature")
            op = row.operator(UMA_OT_tools_fix_transform_from_report.bl_idname, text="Fix all transform items")
            op.mode = "ALL"
            if 0 <= wm.uma_tools_report_index < len(wm.uma_tools_report_lines):
                sel = wm.uma_tools_report_lines[wm.uma_tools_report_index]
                col.separator()
                col.label(text="Selected:")
                col.label(text=getattr(sel, "text", ""))

        # Rigging and Weights
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_copy_weights", "Rigging and Weights")
        if wm.uma_tools_section_copy_weights:
            col = box.column(align=True)
            col.operator(UMA_OT_tools_reset_pose_transforms.bl_idname, text="Reset pose transforms")
            col.operator(UMA_OT_tools_bake_pose_to_rest_preserve_shape_keys.bl_idname)
            col.operator(UMA_OT_tools_copy_weights_mirrored.bl_idname, text="Copy Weights Mirrored")
            col.operator(UMA_OT_tools_remove_negligible_weights.bl_idname, text="Remove negligible weights")
            col.separator()
            col.prop(wm, "uma_tools_weights_source", text="Source")
            col.prop(wm, "uma_tools_smooth_weights", text="Smooth weights")
            col.prop(wm, "uma_tools_weight_mapping", text="Mapping")
            col.operator(UMA_OT_tools_copy_weights_to_selected.bl_idname, text="Copy weights to all selected")
            col.separator()
            col.operator(
                UMA_OT_tools_strip_weights_and_rig_to_selected.bl_idname,
                text="Strip weights and rig to selected",
            )

        # Blendshapes fitting
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_blendshape_fitting", "Blendshapes fitting")
        if wm.uma_tools_section_blendshape_fitting:
            fit = box.column(align=True)
            fit.prop(wm, "uma_tools_blendshape_source", text="Source object")
            source_obj = getattr(wm, "uma_tools_blendshape_source", None)
            if (source_obj is not None and source_obj.type == 'MESH'
                    and getattr(source_obj.data, "shape_keys", None) is not None):
                fit.prop_search(wm, "uma_tools_blendshape_from", source_obj.data.shape_keys, "key_blocks", text="From Blendshape")
                fit.prop_search(wm, "uma_tools_blendshape_to", source_obj.data.shape_keys, "key_blocks", text="To Blendshape")
            else:
                fit.prop(wm, "uma_tools_blendshape_from", text="From Blendshape")
                fit.prop(wm, "uma_tools_blendshape_to", text="To Blendshape")
                if source_obj is None:
                    fit.label(text="Pick a source object first", icon='INFO')
                else:
                    fit.label(text="Source has no blendshapes", icon='INFO')
            fit.separator()
            fit.operator(UMA_OT_tools_deform_selected_to_shape_key.bl_idname, text="Deform selected to shape key")

        # Ponytail Weights
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_pony_weights", "Ponytail Weights")
        if wm.uma_tools_section_pony_weights:
            _draw_pony_tail_weights_ui(box.column(align=True), scene)

        # Parenting
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_parenting", "Parenting")
        if wm.uma_tools_section_parenting:
            parent_col = box.column(align=True)
            parent_col.operator(UMA_OT_tools_parent_set_object.bl_idname, text="Set Parent (Object)")
            parent_col.operator(UMA_OT_tools_parent_clear.bl_idname, text="Clear Parent (Keep Transform)").clear_type = 'CLEAR_KEEP_TRANSFORM'
            parent_col.operator(UMA_OT_tools_parent_clear.bl_idname, text="Clear Parent Inverse").clear_type = 'CLEAR_INVERSE'
            parent_col.separator()
            parent_col.operator(UMA_OT_tools_apply_all_transforms.bl_idname, text="Apply All Transforms")
            parent_col.operator(UMA_OT_tools_generate_apply_data_transfer.bl_idname, text="Generate Layers and Apply Data Transfer")

        # Utilities
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_utilities", "Utilities")
        if wm.uma_tools_section_utilities:
            util = box.column(align=True)
            util.operator(UMA_OT_tools_view_memory_usage.bl_idname, text="View memory usage")
            util.separator()
            util.prop(wm, "uma_tools_rename_prepend", text="Prepend")
            util.prop(wm, "uma_tools_rename_append", text="Append")
            util.operator(UMA_OT_tools_process_rename_selected.bl_idname, text="Process rename on selected")
            util.separator()
            util.operator(UMA_OT_tools_remove_empty_vertex_groups.bl_idname, text="Remove empty vertex groups")
            util.operator(UMA_OT_tools_normalize_selected_weights.bl_idname, text="Normalize Selected")
            util.operator(UMA_OT_tools_normalize_all_weights.bl_idname, text="Normalize All")

        # Editing Tools
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_editing_tools", "Editing Tools")
        if wm.uma_tools_section_editing_tools:
            edit = box.column(align=True)
            edit.operator(UMA_OT_tools_select_edge_loops.bl_idname, text="Select edge loops")

        # Vertex Group Quick Select
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_vertex_group_quick_select", "Vertex Group Quick Select")
        if wm.uma_tools_section_vertex_group_quick_select:
            vg = box.column(align=True)
            row = vg.row(align=True)
            row.operator(UMA_OT_tools_select_all_vertices.bl_idname, text="Select all vertexes")
            row.operator(UMA_OT_tools_unselect_all_vertices.bl_idname, text="Unselect all vertexes")
            vg.operator(UMA_OT_tools_add_current_vertex_group.bl_idname, text="Add current vertex group")
            vg.template_list(
                "UMA_UL_tools_vertex_group_quick_select",
                "",
                scene,
                "uma_tools_group_quick_select",
                scene,
                "uma_tools_group_quick_select_index",
                rows=6,
            )

        # 3D Cursor
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_3d_cursor", "3D Cursor")
        if wm.uma_tools_section_3d_cursor:
            cur = box.column(align=True)
            cur.operator(UMA_OT_tools_cursor_move_to_origin.bl_idname, text="Move to Origin")
            cur.operator(UMA_OT_tools_cursor_align_with_object.bl_idname, text="Align with Object")

        # UDIM Tools
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_udim_tools", "UDIM Tools")
        if wm.uma_tools_section_udim_tools:
            udim = box.column(align=True)
            udim.operator(UMA_OT_tools_split_udims_to_textures.bl_idname, text="Split UDIMS into separate textures")
            udim.operator(UMA_OT_tools_reset_to_udim.bl_idname, text="Reset to UDIM")

        # UMA Import
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_import", "UMA Import")
        if wm.uma_tools_section_import:
            imp = box.column(align=True)
            imp.prop(scene, "uma_tools_import_material", text="Material")
            imp.label(text="Scale (X, Y, Z):")
            imp.prop(scene, "uma_tools_import_scale", text="")
            imp.label(text="Rotation (X, Y, Z):")
            imp.prop(scene, "uma_tools_import_rotation", text="")
            imp.label(text="Location (X, Y, Z):")
            imp.prop(scene, "uma_tools_import_location", text="")
            row = imp.row(align=True)
            row.operator(UMA_OT_tools_import_copy_from_current.bl_idname, text="Copy from current")
            row.operator(UMA_OT_tools_import_paste_to_current.bl_idname, text="Paste to current")
            imp.separator()
            imp.operator_context = 'INVOKE_DEFAULT'
            imp.operator(UMA_OT_tools_import_obj_uma.bl_idname, text="Import Wavefront OBJ")
            imp.operator(UMA_OT_tools_import_fbx_uma.bl_idname, text="Import FBX")
            imp.operator_context = 'EXEC_DEFAULT'

        # UMA Export
        box = layout.box()
        _draw_fold_header(box, "uma_tools_section_export", "UMA Export")
        if wm.uma_tools_section_export:
            box.prop(wm, "uma_tools_export_uma2_format", text="UMA 2 Format")
            box.operator_context = 'INVOKE_DEFAULT'
            box.operator(UMA_OT_tools_fbx_export_all.bl_idname, text="Export FBX (All)")
            box.operator(UMA_OT_tools_fbx_export_selected.bl_idname, text="Export FBX (Selected)")
            box.operator_context = 'EXEC_DEFAULT'

class UMA_ToolsReportLine(bpy.types.PropertyGroup):
    text: bpy.props.StringProperty(default="")


class UMA_ToolsVertexGroupQuickSelectItem(bpy.types.PropertyGroup):
    name: bpy.props.StringProperty(default="")


class UMA_UL_tools_report(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        if self.layout_type in {'DEFAULT', 'COMPACT'}:
            layout.label(text=getattr(item, "text", ""), icon='ERROR')
        else:
            layout.label(text="")


class UMA_UL_tools_vertex_group_quick_select(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        if self.layout_type in {'DEFAULT', 'COMPACT'}:
            row = layout.row(align=True)
            row.label(text=getattr(item, "name", ""))
            button_row = row.row(align=True)
            button_row.scale_x = 0.7
            op = button_row.operator(UMA_OT_tools_select_vertex_group.bl_idname, text="Select")
            op.group_name = getattr(item, "name", "")
            op = button_row.operator(UMA_OT_tools_select_vertex_group_opposite.bl_idname, text="Opposite")
            op.group_name = getattr(item, "name", "")
            op = row.operator(UMA_OT_tools_remove_vertex_group_quick_select.bl_idname, text="", icon='X')
            op.index = index
        else:
            layout.label(text="")


def draw_outliner_object_context(self, context):
    layout = self.layout

    layout.separator()
    layout.label(text="UMA", icon='ARMATURE_DATA')
    layout.operator(UMA_OT_tools_duplicate_object.bl_idname, text="Duplicate Objects")
    layout.operator(UMA_OT_tools_toggle_on.bl_idname, text="Toggle On", icon='HIDE_OFF')
    layout.operator(UMA_OT_tools_apply_all_transforms.bl_idname, text="Apply All Transforms", icon='CHECKMARK')
    layout.operator(UMA_OT_tools_generate_apply_data_transfer.bl_idname, text="Generate Layers and Apply Data Transfer", icon='MOD_DATA_TRANSFER')

    layout.separator()
    layout.label(text="Parent", icon='CONSTRAINT_BONE')

    layout.operator(UMA_OT_tools_parent_set_object.bl_idname, text="Set Parent (Object)").keep_transform = True
    layout.operator(UMA_OT_tools_parent_clear.bl_idname, text="Clear Parent (Keep Transform)").clear_type = 'CLEAR_KEEP_TRANSFORM'
    layout.operator(UMA_OT_tools_parent_clear.bl_idname, text="Clear Parent Inverse").clear_type = 'CLEAR_INVERSE'

    layout.separator()
    layout.label(text="UMA Export", icon='EXPORT')
    layout.operator_context = 'INVOKE_DEFAULT'
    layout.operator(UMA_OT_tools_fbx_export_all.bl_idname, text="Export FBX (All)", icon='SCENE_DATA')
    layout.operator(UMA_OT_tools_fbx_export_selected.bl_idname, text="Export FBX (Selected)", icon='RESTRICT_SELECT_OFF')
    layout.operator_context = 'EXEC_DEFAULT'


# ---------------------------------------------------------------------------
# Ponytail / Hair Weighting - PropertyGroup, UIList, Operators, and Panel
# ---------------------------------------------------------------------------
# Usage:
#   1. Select an armature (or a mesh with an Armature modifier) and enter
#      Pose or Edit mode. Select the bones you want to use for weighting.
#   2. In the UMA Tools sidebar "Ponytail Weights" section, press
#      "Add Selected Bones" to build the bone list.
#   3. Select a mesh and optionally select specific vertices (in Edit mode).
#   4. Adjust "Smooth Factor" (0 = nearest bone only, 1 = broad blend).
#   5. Press "Calculate weights for bones".
#   The operator creates vertex groups named after each bone, removes the
#   processed vertices from groups *not* in the list, and assigns normalized
#   weights using a Gaussian radial-basis function.
# ---------------------------------------------------------------------------


class UMATools_PonyBoneItem(bpy.types.PropertyGroup):
    """Stores a single bone name for the ponytail weighting list."""
    bone_name: bpy.props.StringProperty(name="Bone", default="")


class UMATools_UL_pony_bones(bpy.types.UIList):
    """UIList that displays the ponytail bone name list."""
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        if self.layout_type in {'DEFAULT', 'COMPACT'}:
            layout.label(text=getattr(item, "bone_name", ""), icon='BONE_DATA')
        else:
            layout.label(text="")


def _draw_pony_tail_weights_ui(layout, scene):
    """Draw the shared ponytail weighting controls."""
    layout.template_list(
        "UMATools_UL_pony_bones",
        "",
        scene,
        "umatools_pony_bones",
        scene,
        "umatools_pony_bones_index",
        rows=5,
    )

    row = layout.row(align=True)
    row.operator(UMATools_OT_add_pony_bones.bl_idname, text="Add Selected Bones")

    row = layout.row(align=True)
    op = row.operator(UMATools_OT_remove_pony_bone.bl_idname, text="Remove Selected")
    op.index = scene.umatools_pony_bones_index
    row.operator(UMATools_OT_clear_pony_bones.bl_idname, text="Clear List")

    layout.separator()
    layout.prop(scene, "umatools_pony_smooth", text="Smooth Factor", slider=True)
    layout.operator(
        UMATools_OT_calculate_pony_weights.bl_idname,
        text="Calculate weights for bones",
        icon='MOD_VERTEX_WEIGHT',
    )


class UMATools_OT_add_pony_bones(bpy.types.Operator):
    """Add selected bones from the active armature to the ponytail bone list.

    Works in Pose, Edit, or Object mode. Duplicate names are skipped.
    """
    bl_idname = "umatools.add_pony_bones"
    bl_label = "Add Selected Bones"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        scene = context.scene
        try:
            armature_obj = None
            active_obj = context.view_layer.objects.active
            if active_obj and active_obj.type == 'ARMATURE':
                armature_obj = active_obj
            else:
                for obj in context.selected_objects:
                    if obj.type == 'ARMATURE':
                        armature_obj = obj
                        break

            if armature_obj is None:
                self.report({'ERROR'}, "Select an armature and choose one or more bones")
                return {'CANCELLED'}

            selected_names = []
            if armature_obj.mode == 'EDIT':
                # In armature edit mode, selected edit bones are separate from
                # armature.data.bones. Use the edit collection directly.
                selected_names = [bone.name for bone in armature_obj.data.edit_bones if bone.select]
            elif armature_obj.mode == 'POSE':
                selected_pose_bones = getattr(context, "selected_pose_bones", None) or []
                selected_names = [pb.name for pb in selected_pose_bones]
            else:
                selected_names = [bone.name for bone in armature_obj.data.bones if bone.select]

            if not selected_names:
                self.report({'ERROR'}, "No bones are selected on the active armature")
                return {'CANCELLED'}

            existing = {item.bone_name for item in scene.umatools_pony_bones}
            added = 0
            for name in selected_names:
                if name in existing:
                    continue
                item = scene.umatools_pony_bones.add()
                item.bone_name = name
                existing.add(name)
                added += 1

            if added > 0:
                self.report({'INFO'}, f"Added {added} bone(s) to ponytail list")
            else:
                self.report({'INFO'}, "Selected bones are already in the list")
            return {'FINISHED'}
        except Exception as ex:
            self.report({'ERROR'}, f"Could not add selected bones: {ex}")
            return {'CANCELLED'}


class UMATools_OT_remove_pony_bone(bpy.types.Operator):
    """Remove the selected entry from the ponytail bone list."""
    bl_idname = "umatools.remove_pony_bone"
    bl_label = "Remove Selected"
    bl_options = {'REGISTER', 'UNDO'}

    index: bpy.props.IntProperty(default=-1)

    def execute(self, context):
        scene = context.scene
        collection = scene.umatools_pony_bones
        if self.index < 0 or self.index >= len(collection):
            return {'CANCELLED'}
        collection.remove(self.index)
        scene.umatools_pony_bones_index = min(
            scene.umatools_pony_bones_index,
            max(0, len(collection) - 1),
        )
        return {'FINISHED'}


class UMATools_OT_clear_pony_bones(bpy.types.Operator):
    """Clear the entire ponytail bone list."""
    bl_idname = "umatools.clear_pony_bones"
    bl_label = "Clear List"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        context.scene.umatools_pony_bones.clear()
        context.scene.umatools_pony_bones_index = 0
        self.report({'INFO'}, "Ponytail bone list cleared")
        return {'FINISHED'}


class UMATools_OT_calculate_pony_weights(bpy.types.Operator):
    """Calculate and assign vertex weights limited to the chosen ponytail bones.

    Uses a parameterized Gaussian radial-basis function controlled by the
    Smooth Factor slider.  The Gaussian is preferred over inverse-distance
    weighting because it is numerically stable at all distances and provides
    an intuitive mapping: factor=0 -> nearest-only (hard assignment),
    factor=1 -> broad smooth blending.

    If you wish to experiment with alternative smoothing functions, you can
    replace the Gaussian block (marked below) with one of these:

        # Softmax over negative distances:
        #   raw = [math.exp(-d) for d in distances]
        #   total = sum(raw) or 1.0
        #   weights = [r / total for r in raw]

        # Inverse-distance with exponent:
        #   epsilon = 1e-6
        #   raw = [1.0 / (d + epsilon) ** exponent for d in distances]

    The Gaussian is the default because its sigma parameter cleanly maps
    to the Smooth Factor without introducing a hard singularity at d=0.
    """
    bl_idname = "umatools.calculate_pony_weights"
    bl_label = "Calculate Weights for Bones"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        t_start = time.perf_counter()
        scene = context.scene
        mesh_obj = context.view_layer.objects.active
        prev_mode = None
        progress_started = False

        try:
            if mesh_obj is None or mesh_obj.type != 'MESH':
                self.report({'ERROR'}, "Active object must be a mesh")
                return {'CANCELLED'}

            mesh_data = mesh_obj.data
            prev_mode = mesh_obj.mode

            # Use selected edit-mode vertices when present. If none are
            # selected, process the full mesh as requested.
            selected_vert_indices = []
            if prev_mode == 'EDIT':
                bm = bmesh.from_edit_mesh(mesh_data)
                bm.verts.ensure_lookup_table()
                bm.verts.index_update()
                selected_vert_indices = [vert.index for vert in bm.verts if vert.select]
            if not selected_vert_indices:
                selected_vert_indices = [vert.index for vert in mesh_data.vertices]

            if not selected_vert_indices:
                self.report({'ERROR'}, "Mesh has no vertices to process")
                return {'CANCELLED'}

            armature_obj = None
            for mod in mesh_obj.modifiers:
                if mod.type == 'ARMATURE' and getattr(mod, "object", None) is not None:
                    armature_obj = mod.object
                    break
            if armature_obj is None:
                for obj in context.selected_objects:
                    if obj.type == 'ARMATURE':
                        armature_obj = obj
                        break
            if armature_obj is None:
                for obj in context.scene.objects:
                    if obj.type == 'ARMATURE':
                        armature_obj = obj
                        break
            if armature_obj is None:
                self.report({'ERROR'}, "No armature found for the active mesh")
                return {'CANCELLED'}

            bone_names = []
            seen_bones = set()
            for item in scene.umatools_pony_bones:
                name = (item.bone_name or "").strip()
                if name and name not in seen_bones:
                    bone_names.append(name)
                    seen_bones.add(name)
            if not bone_names:
                self.report({'ERROR'}, "Bone list is empty. Add bones first.")
                return {'CANCELLED'}

            pose_bones = getattr(armature_obj.pose, "bones", None)
            if pose_bones is None:
                self.report({'ERROR'}, f"{armature_obj.name} has no pose bones")
                return {'CANCELLED'}

            valid_bones = []
            skipped_bones = []
            for name in bone_names:
                pose_bone = pose_bones.get(name)
                if pose_bone is None:
                    skipped_bones.append(name)
                else:
                    valid_bones.append((name, pose_bone))

            if skipped_bones:
                skipped_preview = ", ".join(skipped_bones[:5])
                suffix = "..." if len(skipped_bones) > 5 else ""
                self.report({'WARNING'}, f"Skipped missing bones: {skipped_preview}{suffix}")
            if not valid_bones:
                self.report({'ERROR'}, "None of the listed bones exist in the armature")
                return {'CANCELLED'}

            if prev_mode != 'OBJECT':
                context.view_layer.objects.active = mesh_obj
                bpy.ops.object.mode_set(mode='OBJECT')
                mesh_data = mesh_obj.data

            vertex_groups = mesh_obj.vertex_groups
            target_groups = {}
            for name, _pose_bone in valid_bones:
                group = vertex_groups.get(name)
                if group is None:
                    group = vertex_groups.new(name=name)
                target_groups[name] = group

            target_group_indices = {group.index for group in target_groups.values()}
            remove_failures = 0
            for group in list(vertex_groups):
                if group.index in target_group_indices:
                    continue
                try:
                    group.remove(selected_vert_indices)
                except Exception:
                    remove_failures += 1

            arm_world = armature_obj.matrix_world
            bone_positions = [(name, arm_world @ pose_bone.head) for name, pose_bone in valid_bones]
            mat_world = mesh_obj.matrix_world
            smooth_factor = max(0.0, min(1.0, scene.umatools_pony_smooth))
            num_verts = len(selected_vert_indices)
            num_bones = len(bone_positions)
            min_sigma = 1e-4

            wm = context.window_manager
            wm.progress_begin(0, num_verts)
            progress_started = True

            for idx, vert_index in enumerate(selected_vert_indices):
                if idx % 100 == 0:
                    wm.progress_update(idx)

                vertex = mesh_data.vertices[vert_index]
                v_world = mat_world @ vertex.co
                dist_sq = [(v_world - bone_pos).length_squared for _name, bone_pos in bone_positions]
                max_dist = math.sqrt(max(dist_sq))

                # Gaussian radial basis is stable at zero distance and gives a
                # useful sigma control. It avoids the singularities common in
                # naive inverse-distance weighting.
                max_sigma = max_dist * 0.75 if max_dist > 0.0 else 1.0
                sigma = min_sigma + (max_sigma - min_sigma) * smooth_factor

                if smooth_factor <= 0.0 or sigma <= min_sigma:
                    nearest_idx = min(range(num_bones), key=lambda bone_index: dist_sq[bone_index])
                    raw_weights = [1.0 if bone_index == nearest_idx else 0.0 for bone_index in range(num_bones)]
                else:
                    inv_two_sigma_sq = 1.0 / (2.0 * sigma * sigma)
                    raw_weights = [math.exp(-d2 * inv_two_sigma_sq) for d2 in dist_sq]

                total = sum(raw_weights)
                if total > 0.0:
                    weights = [weight / total for weight in raw_weights]
                else:
                    weights = [1.0 / num_bones for _ in range(num_bones)]

                for bone_index, (name, _bone_pos) in enumerate(bone_positions):
                    target_groups[name].add([vert_index], weights[bone_index], 'REPLACE')

            elapsed = time.perf_counter() - t_start
            summary = (
                f"Ponytail weights: {num_verts} vertices, {num_bones} bones, "
                f"completed in {elapsed:.3f}s"
            )
            print(summary)
            if remove_failures:
                self.report({'WARNING'}, f"{summary}; {remove_failures} non-target group(s) could not be cleared")
            else:
                self.report({'INFO'}, summary)
            return {'FINISHED'}

        except Exception as ex:
            self.report({'ERROR'}, f"Could not calculate ponytail weights: {ex}")
            return {'CANCELLED'}
        finally:
            if progress_started:
                try:
                    context.window_manager.progress_end()
                except Exception:
                    pass
            if mesh_obj is not None and prev_mode and prev_mode != 'OBJECT':
                try:
                    context.view_layer.objects.active = mesh_obj
                    bpy.ops.object.mode_set(mode=prev_mode)
                except Exception:
                    pass


class UMATools_PT_pony_tail_weights(bpy.types.Panel):
    """Panel for hair/ponytail bone weighting in the UMA Tools sidebar."""
    bl_label = "Ponytail Weights"
    bl_idname = "UMATools_PT_pony_tail_weights"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'UMA Tools'
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        _draw_pony_tail_weights_ui(self.layout, context.scene)


classes = (
    UMA_ToolsReportLine,
    UMA_ToolsVertexGroupQuickSelectItem,
    UMA_ToolsMemoryItem,
    UMA_UL_tools_memory,
    UMA_OT_tools_view_memory_usage,
    UMA_UL_tools_report,
    UMA_UL_tools_vertex_group_quick_select,
    UMA_OT_tools_check_errors,
    UMA_OT_tools_insert_global_position_bones,
    UMA_OT_tools_select_all,
    UMA_OT_tools_fix_transforms,
    UMA_OT_tools_fix_transform_from_report,
    UMA_OT_tools_fix_missing_armature_modifiers,
    UMA_OT_tools_copy_weights_to_selected,
    UMA_OT_tools_deform_selected_to_shape_key,
    UMA_OT_tools_process_rename_selected,
    UMA_OT_tools_remove_empty_vertex_groups,
    UMA_OT_tools_remove_negligible_weights,
    UMA_OT_tools_strip_weights_and_rig_to_selected,
    UMA_OT_tools_normalize_selected_weights,
    UMA_OT_tools_normalize_all_weights,
    UMA_OT_tools_reset_pose_transforms,
    UMA_OT_tools_bake_pose_to_rest_preserve_shape_keys,
    UMA_OT_tools_select_edge_loops,
    UMA_OT_tools_copy_weights_mirrored,
    UMA_OT_tools_add_current_vertex_group,
    UMA_OT_tools_select_vertex_group,
    UMA_OT_tools_select_vertex_group_opposite,
    UMA_OT_tools_select_all_vertices,
    UMA_OT_tools_unselect_all_vertices,
    UMA_OT_tools_cursor_move_to_origin,
    UMA_OT_tools_cursor_align_with_object,
    UMA_OT_tools_remove_vertex_group_quick_select,
    UMA_OT_tools_parent_set_object,
    UMA_OT_tools_parent_clear,
    UMA_OT_tools_apply_all_transforms,
    UMA_OT_tools_generate_apply_data_transfer,
    UMA_OT_tools_duplicate_object,
    UMA_OT_tools_toggle_on,
    UMA_OT_tools_import_obj_uma,
    UMA_OT_tools_import_copy_from_current,
    UMA_OT_tools_import_paste_to_current,
    UMA_OT_tools_import_fbx_uma,
    UMA_OT_tools_fbx_export_all,
    UMA_OT_tools_fbx_export_selected,
    UMA_OT_tools_split_udims_to_textures,
    UMA_OT_tools_reset_to_udim,
    UMA_PT_tools_panel,
    # Ponytail / Hair Weighting
    UMATools_PonyBoneItem,
    UMATools_UL_pony_bones,
    UMATools_OT_add_pony_bones,
    UMATools_OT_remove_pony_bone,
    UMATools_OT_clear_pony_bones,
    UMATools_OT_calculate_pony_weights,
)


def register():
    # Allow re-running from the Text Editor without duplicate registrations.
    try:
        bpy.types.OUTLINER_MT_object.remove(draw_outliner_object_context)
    except Exception:
        pass

    for cls in classes:
        try:
            bpy.utils.unregister_class(cls)
        except Exception:
            pass
        bpy.utils.register_class(cls)

    bpy.types.OUTLINER_MT_object.append(draw_outliner_object_context)

    # Avoid bpy.data.* access in register (may be restricted during install).
    if hasattr(bpy.types.WindowManager, "uma_tools_report_lines"):
        try:
            del bpy.types.WindowManager.uma_tools_report_lines
        except Exception:
            pass
    if hasattr(bpy.types.WindowManager, "uma_tools_report_index"):
        try:
            del bpy.types.WindowManager.uma_tools_report_index
        except Exception:
            pass

    bpy.types.WindowManager.uma_tools_report_lines = bpy.props.CollectionProperty(type=UMA_ToolsReportLine)
    bpy.types.WindowManager.uma_tools_report_index = bpy.props.IntProperty(default=0)

    if hasattr(bpy.types.WindowManager, "uma_tools_error_check_visible_only"):
        try:
            del bpy.types.WindowManager.uma_tools_error_check_visible_only
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_error_check_visible_only = bpy.props.BoolProperty(
        name="Visible Only",
        description="Skip hidden objects when checking the scene for export issues",
        default=True,
    )

    if hasattr(bpy.types.Scene, "uma_tools_group_quick_select"):
        try:
            del bpy.types.Scene.uma_tools_group_quick_select
        except Exception:
            pass
    if hasattr(bpy.types.Scene, "uma_tools_group_quick_select_index"):
        try:
            del bpy.types.Scene.uma_tools_group_quick_select_index
        except Exception:
            pass

    bpy.types.Scene.uma_tools_group_quick_select = bpy.props.CollectionProperty(
        type=UMA_ToolsVertexGroupQuickSelectItem
    )
    bpy.types.Scene.uma_tools_group_quick_select_index = bpy.props.IntProperty(
        default=0,
        update=_on_quick_select_index_update,
    )

    if hasattr(bpy.types.Scene, "uma_tools_import_material"):
        try:
            del bpy.types.Scene.uma_tools_import_material
        except Exception:
            pass
    bpy.types.Scene.uma_tools_import_material = bpy.props.PointerProperty(
        name="Import Material",
        description="Material assigned to meshes imported from UMA Import",
        type=bpy.types.Material,
    )

    if hasattr(bpy.types.Scene, "uma_tools_import_scale"):
        try:
            del bpy.types.Scene.uma_tools_import_scale
        except Exception:
            pass
    bpy.types.Scene.uma_tools_import_scale = bpy.props.FloatVectorProperty(
        name="Import Scale",
        description="Scale (X, Y, Z) applied to imported meshes",
        size=3,
        default=(0.17, 0.17, 0.18),
        min=0.001,
        max=100.0,
        step=0.01,
    )

    if hasattr(bpy.types.Scene, "uma_tools_import_location"):
        try:
            del bpy.types.Scene.uma_tools_import_location
        except Exception:
            pass
    bpy.types.Scene.uma_tools_import_location = bpy.props.FloatVectorProperty(
        name="Import Location",
        description="Location (X, Y, Z) applied to imported meshes",
        size=3,
        default=(0.0, 0.089, 0.113),
        min=-1000.0,
        max=1000.0,
        step=0.001,
    )

    if hasattr(bpy.types.Scene, "uma_tools_import_rotation"):
        try:
            del bpy.types.Scene.uma_tools_import_rotation
        except Exception:
            pass
    bpy.types.Scene.uma_tools_import_rotation = bpy.props.FloatVectorProperty(
        name="Import Rotation",
        description="Rotation (X, Y, Z) in radians applied to imported meshes",
        size=3,
        default=(0.0, 0.0, 0.0),
        min=-6.283,
        max=6.283,
        step=0.01,
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_weights_source"):
        try:
            del bpy.types.WindowManager.uma_tools_weights_source
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_weights_source = bpy.props.PointerProperty(
        name="Weights Source",
        description="Source mesh object to copy vertex group weights from",
        type=bpy.types.Object,
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_smooth_weights"):
        try:
            del bpy.types.WindowManager.uma_tools_smooth_weights
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_smooth_weights = bpy.props.BoolProperty(
        name="Smooth weights",
        description="Blend copied vertex weights with neighboring vertices using gentle smoothing",
        default=False,
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_weight_mapping"):
        try:
            del bpy.types.WindowManager.uma_tools_weight_mapping
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_weight_mapping = bpy.props.EnumProperty(
        name="Mapping",
        description="Vertex mapping mode used by the Data Transfer modifier when copying weights",
        items=[
            ('TOPOLOGY', "Topology", "Copy from identical topology meshes"),
            ('NEAREST', "Nearest Vertex", "Copy from closest vertex"),
            ('EDGE_NEAREST', "Nearest Edge Vertex", "Copy from closest vertex of closest edge"),
            ('EDGEINTERP_NEAREST', "Nearest Edge Interpolated", "Copy from interpolated values of vertices from closest point on closest edge"),
            ('POLY_NEAREST', "Nearest Face Vertex", "Copy from closest vertex of closest face"),
            ('POLYINTERP_NEAREST', "Nearest Face Interpolated", "Copy from interpolated values of vertices from closest point on closest face"),
            ('POLYINTERP_VNORPROJ', "Projected Face Interpolated", "Copy from interpolated values of vertices from point on closest face hit by normal-projection"),
        ],
        default='POLYINTERP_NEAREST',
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_blendshape_source"):
        try:
            del bpy.types.WindowManager.uma_tools_blendshape_source
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_blendshape_source = bpy.props.PointerProperty(
        name="Source object",
        description="Source mesh holding the blendshapes used for surface deform fitting",
        type=bpy.types.Object,
        poll=_uma_tools_mesh_object_poll,
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_blendshape_from"):
        try:
            del bpy.types.WindowManager.uma_tools_blendshape_from
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_blendshape_from = bpy.props.StringProperty(
        name="From Blendshape",
        description="Exact source shape used to bind Surface Deform, regardless of current sliders or pins",
        default="",
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_blendshape_to"):
        try:
            del bpy.types.WindowManager.uma_tools_blendshape_to
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_blendshape_to = bpy.props.StringProperty(
        name="To Blendshape",
        description="Exact source shape to deform the selected meshes to, regardless of current sliders or pins",
        default="",
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_export_uma2_format"):
        try:
            del bpy.types.WindowManager.uma_tools_export_uma2_format
        except Exception:
            pass
    bpy.types.WindowManager.uma_tools_export_uma2_format = bpy.props.BoolProperty(
        name="UMA 2 Format",
        description="Use UMA 2 FBX export axes for both export buttons",
        default=False,
    )

    if hasattr(bpy.types.WindowManager, "uma_tools_rename_prepend"):
        try:
            del bpy.types.WindowManager.uma_tools_rename_prepend
        except Exception:
            pass
    if hasattr(bpy.types.WindowManager, "uma_tools_rename_append"):
        try:
            del bpy.types.WindowManager.uma_tools_rename_append
        except Exception:
            pass

    bpy.types.WindowManager.uma_tools_rename_prepend = bpy.props.StringProperty(
        name="Prepend",
        description="String to prepend to selected mesh object names",
        default="",
    )
    bpy.types.WindowManager.uma_tools_rename_append = bpy.props.StringProperty(
        name="Append",
        description="String to append to selected mesh object names",
        default="",
    )

    # Ponytail / Hair Weighting - Scene properties
    if hasattr(bpy.types.Scene, "umatools_pony_bones"):
        try:
            del bpy.types.Scene.umatools_pony_bones
        except Exception:
            pass
    bpy.types.Scene.umatools_pony_bones = bpy.props.CollectionProperty(
        type=UMATools_PonyBoneItem,
    )
    if hasattr(bpy.types.Scene, "umatools_pony_bones_index"):
        try:
            del bpy.types.Scene.umatools_pony_bones_index
        except Exception:
            pass
    bpy.types.Scene.umatools_pony_bones_index = bpy.props.IntProperty(default=0)
    if hasattr(bpy.types.Scene, "umatools_pony_smooth"):
        try:
            del bpy.types.Scene.umatools_pony_smooth
        except Exception:
            pass
    bpy.types.Scene.umatools_pony_smooth = bpy.props.FloatProperty(
        name="Smooth Factor",
        description="0 = nearest bone only, 1 = broad smooth blend",
        default=0.5,
        min=0.0,
        max=1.0,
        subtype='FACTOR',
    )

    # UI foldouts
    for prop_name, default in (
        ("uma_tools_section_error_checking", True),
        ("uma_tools_section_copy_weights", False),
        ("uma_tools_section_blendshape_fitting", False),
        ("uma_tools_section_pony_weights", False),
        ("uma_tools_section_parenting", True),
        ("uma_tools_section_utilities", True),
        ("uma_tools_section_editing_tools", False),
        ("uma_tools_section_vertex_group_quick_select", False),
        ("uma_tools_section_3d_cursor", False),
        ("uma_tools_section_udim_tools", False),
        ("uma_tools_section_import", True),
        ("uma_tools_section_export", True),
    ):
        if hasattr(bpy.types.WindowManager, prop_name):
            try:
                delattr(bpy.types.WindowManager, prop_name)
            except Exception:
                pass
        setattr(
            bpy.types.WindowManager,
            prop_name,
            bpy.props.BoolProperty(name=prop_name, default=default),
        )


def unregister():
    try:
        bpy.types.OUTLINER_MT_object.remove(draw_outliner_object_context)
    except Exception:
        pass

    if hasattr(bpy.types.WindowManager, "uma_tools_report_lines"):
        try:
            del bpy.types.WindowManager.uma_tools_report_lines
        except Exception:
            pass
    if hasattr(bpy.types.WindowManager, "uma_tools_report_index"):
        try:
            del bpy.types.WindowManager.uma_tools_report_index
        except Exception:
            pass
    if hasattr(bpy.types.WindowManager, "uma_tools_error_check_visible_only"):
        try:
            del bpy.types.WindowManager.uma_tools_error_check_visible_only
        except Exception:
            pass

    if hasattr(bpy.types.Scene, "uma_tools_group_quick_select"):
        try:
            del bpy.types.Scene.uma_tools_group_quick_select
        except Exception:
            pass
    if hasattr(bpy.types.Scene, "uma_tools_group_quick_select_index"):
        try:
            del bpy.types.Scene.uma_tools_group_quick_select_index
        except Exception:
            pass

    if hasattr(bpy.types.Scene, "uma_tools_import_material"):
        try:
            del bpy.types.Scene.uma_tools_import_material
        except Exception:
            pass

    if hasattr(bpy.types.Scene, "uma_tools_import_scale"):
        try:
            del bpy.types.Scene.uma_tools_import_scale
        except Exception:
            pass

    if hasattr(bpy.types.Scene, "uma_tools_import_location"):
        try:
            del bpy.types.Scene.uma_tools_import_location
        except Exception:
            pass

    if hasattr(bpy.types.Scene, "uma_tools_import_rotation"):
        try:
            del bpy.types.Scene.uma_tools_import_rotation
        except Exception:
            pass

    # Ponytail / Hair Weighting - Scene properties
    if hasattr(bpy.types.Scene, "umatools_pony_bones"):
        try:
            del bpy.types.Scene.umatools_pony_bones
        except Exception:
            pass
    if hasattr(bpy.types.Scene, "umatools_pony_bones_index"):
        try:
            del bpy.types.Scene.umatools_pony_bones_index
        except Exception:
            pass
    if hasattr(bpy.types.Scene, "umatools_pony_smooth"):
        try:
            del bpy.types.Scene.umatools_pony_smooth
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_weights_source"):
        try:
            del bpy.types.WindowManager.uma_tools_weights_source
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_smooth_weights"):
        try:
            del bpy.types.WindowManager.uma_tools_smooth_weights
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_weight_mapping"):
        try:
            del bpy.types.WindowManager.uma_tools_weight_mapping
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_blendshape_source"):
        try:
            del bpy.types.WindowManager.uma_tools_blendshape_source
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_blendshape_from"):
        try:
            del bpy.types.WindowManager.uma_tools_blendshape_from
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_blendshape_to"):
        try:
            del bpy.types.WindowManager.uma_tools_blendshape_to
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_export_uma2_format"):
        try:
            del bpy.types.WindowManager.uma_tools_export_uma2_format
        except Exception:
            pass

    if hasattr(bpy.types.WindowManager, "uma_tools_rename_prepend"):
        try:
            del bpy.types.WindowManager.uma_tools_rename_prepend
        except Exception:
            pass
    if hasattr(bpy.types.WindowManager, "uma_tools_rename_append"):
        try:
            del bpy.types.WindowManager.uma_tools_rename_append
        except Exception:
            pass

    for prop_name in (
        "uma_tools_section_error_checking",
        "uma_tools_section_copy_weights",
        "uma_tools_section_blendshape_fitting",
        "uma_tools_section_pony_weights",
        "uma_tools_section_parenting",
        "uma_tools_section_utilities",
        "uma_tools_section_editing_tools",
        "uma_tools_section_vertex_group_quick_select",
        "uma_tools_section_3d_cursor",
        "uma_tools_section_udim_tools",
        "uma_tools_section_import",
        "uma_tools_section_export",
    ):
        if hasattr(bpy.types.WindowManager, prop_name):
            try:
                delattr(bpy.types.WindowManager, prop_name)
            except Exception:
                pass

    for cls in reversed(classes):
        try:
            bpy.utils.unregister_class(cls)
        except Exception:
            pass


if __name__ == "__main__":
    register()

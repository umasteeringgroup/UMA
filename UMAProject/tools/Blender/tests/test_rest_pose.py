"""Run inside Blender against the current source; all test objects are temporary."""
import bpy
import hashlib
import importlib.util
import json
import pathlib
import sys
import math
from mathutils import Matrix

ROOT = pathlib.Path(__file__).resolve().parents[3]
SOURCE = ROOT / "Tools/Blender/UMAToolsNPanel.py"
spec = importlib.util.spec_from_file_location("uma_rest_pose_candidate", SOURCE)
candidate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = candidate
spec.loader.exec_module(candidate)

def key_settings(obj):
    keys = obj.data.shape_keys
    if not keys:
        return None
    return (keys.use_relative, keys.eval_time,
            [(k.name, k.relative_key.name, k.slider_min, k.slider_max, k.value,
              k.mute, k.vertex_group, k.interpolation, k.frame) for k in keys.key_blocks])

def fingerprint(objects):
    result = []
    for obj in objects:
        entry = [obj.name, obj.data.name if obj.data else None,
                 str(obj.matrix_world), obj.parent.name if obj.parent else None]
        if obj.type == 'MESH':
            h = hashlib.sha256(candidate._uma_rest_pose_coords(obj.data.vertices).tobytes())
            if obj.data.shape_keys:
                for key in obj.data.shape_keys.key_blocks:
                    h.update(candidate._uma_rest_pose_coords(key.data).tobytes())
            entry += [h.hexdigest(), key_settings(obj),
                      [(m.name, m.object.name if m.object else None) for m in obj.modifiers if m.type == 'ARMATURE']]
        elif obj.type == 'ARMATURE':
            entry += [[(b.name, str(b.matrix_local)) for b in obj.data.bones],
                      [(b.name, str(b.matrix_basis)) for b in obj.pose.bones]]
        result.append(entry)
    return result

def counts():
    return [len(getattr(bpy.data, name)) for name in
            ("objects", "meshes", "armatures", "shape_keys", "collections", "actions")]

def discard(baked, rig, collection):
    animation = baked.data.shape_keys.animation_data if baked.data.shape_keys else None
    actions = set()
    if animation:
        if animation.action:
            actions.add(animation.action)
        for track in animation.nla_tracks:
            for strip in track.strips:
                if strip.action:
                    actions.add(strip.action)
    candidate._uma_rest_pose_remove([(rig, rig.data), (baked, baked.data)])
    bpy.data.collections.remove(collection)
    for action in actions:
        if action.users == 0:
            bpy.data.actions.remove(action)

def check_bake(obj):
    before = fingerprint([obj, obj.modifiers[0].object])
    count_before = counts()
    baked, rig, collection, report = candidate._uma_bake_pose_to_rest(bpy.context, obj)
    try:
        assert baked.data != obj.data
        assert rig.data != obj.modifiers[0].object.data
        assert baked.parent == rig
        assert baked.modifiers[0].object == rig
        if obj.data.shape_keys:
            assert baked.data.shape_keys != obj.data.shape_keys
        assert key_settings(baked) == key_settings(obj), "Key metadata changed"
        if obj.data.shape_keys and obj.data.shape_keys.animation_data:
            source_animation = obj.data.shape_keys.animation_data
            target_animation = baked.data.shape_keys.animation_data
            assert target_animation is not None
            if source_animation.action:
                assert target_animation.action != source_animation.action
            for curve in target_animation.drivers:
                for var in curve.driver.variables:
                    for target in var.targets:
                        assert target.id not in (obj, obj.data.shape_keys, obj.modifiers[0].object)
        # Skinning is still live after conversion.
        neutral = candidate._uma_rest_pose_evaluate(bpy.context, baked)[0]
        rig.pose.bones[0].location.x += 0.1
        posed = candidate._uma_rest_pose_evaluate(bpy.context, baked)[0]
        assert float(abs(posed - neutral).max()) > 1e-5, "Copied rig no longer deforms the mesh"
        assert fingerprint([obj, obj.modifiers[0].object]) == before, "Originals changed"
        rig.pose.bones[0].location.x -= 0.1
        assert all(max(abs(x - y) for row_a, row_b in zip(b.matrix_basis, Matrix.Identity(4))
                       for x, y in zip(row_a, row_b)) < 1e-5 for b in rig.pose.bones), "Rig is not neutral"
        return report
    finally:
        discard(baked, rig, collection)
        assert counts() == count_before, ("Leaked data", count_before, counts())

def make_fixture(collection, owned):
    arm_data = bpy.data.armatures.new("_UMA_TestRigData")
    arm = bpy.data.objects.new("_UMA_TestRig", arm_data)
    collection.objects.link(arm)
    owned.append((arm, arm_data))
    for obj in list(bpy.context.selected_objects):
        obj.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    bone = arm_data.edit_bones.new("RootBone")
    bone.head = (0, 0, 0)
    bone.tail = (0, 1, 0)
    child = arm_data.edit_bones.new("ChildBone")
    child.head = (0, 1, 0)
    child.tail = (0, 2, 0)
    child.parent = bone
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh = bpy.data.meshes.new("_UMA_TestMeshData")
    mesh.from_pydata([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], [], [(0, 1, 2, 3)])
    obj = bpy.data.objects.new("_UMA_TestMesh", mesh)
    collection.objects.link(obj)
    owned.append((obj, mesh))
    group = obj.vertex_groups.new(name="RootBone")
    group.add([0, 1, 2, 3], 0.7, 'REPLACE')
    group = obj.vertex_groups.new(name="ChildBone")
    group.add([0, 1, 2, 3], 0.3, 'REPLACE')
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    basis = obj.shape_key_add(name="Basis")
    first = obj.shape_key_add(name="First")
    first.data[0].co.x += 0.3
    second = obj.shape_key_add(name="Relative child")
    second.relative_key = first
    second.data[1].co.z += 0.2
    first.slider_min = -0.5
    first.slider_max = 1.7
    first.value = 0.25
    mask = obj.vertex_groups.new(name="Mask")
    mask.add([0, 1], 0.4, 'REPLACE')
    second.vertex_group = mask.name
    second.value = 0.8
    second.mute = True
    first.interpolation = 'KEY_LINEAR'
    arm.location = (2, 3, 1)
    arm.scale = (1.1, 0.8, 1.3)
    obj.location = (2.4, 3, 1.2)
    obj.rotation_euler = (0.1, 0.3, -0.2)
    obj.scale = (1.2, 0.9, 1.05)
    pb = arm.pose.bones[0]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (0.2, 0.3, 0.7)
    pb.scale = (1.2, 0.8, 1.1)
    pb.location = (0.1, -0.3, 0.2)
    arm.pose.bones[1].rotation_mode = 'XYZ'
    arm.pose.bones[1].rotation_euler = (0.3, 0.1, -0.2)
    bpy.context.view_layer.update()
    return obj, arm

def expect_failure(obj, text):
    before = counts(), fingerprint([obj, obj.modifiers[0].object])
    try:
        candidate._uma_bake_pose_to_rest(bpy.context, obj)
    except ValueError as error:
        assert text in str(error), str(error)
    else:
        raise AssertionError("Expected failure: " + text)
    assert before == (counts(), fingerprint([obj, obj.modifiers[0].object]))

def run():
    old_active = bpy.context.view_layer.objects.active
    old_selected = list(bpy.context.selected_objects)
    original_objects = list(bpy.data.objects)
    original_fingerprint = fingerprint(original_objects)
    original_counts = counts()
    project_version = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ", 1)[1]
    settings = ROOT / "Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset"
    raw = settings.read_bytes()
    assert raw.startswith(b"%YAML 1.1"), "UMASettings must be inspected as YAML"
    uma_version = next(line.split(": ", 1)[1] for line in raw.decode("utf-8").splitlines() if line.strip().startswith("UMAVersion:"))
    fixture = bpy.data.collections.new("_UMA_RestPose_Test")
    bpy.context.scene.collection.children.link(fixture)
    owned = []
    results = {}
    try:
        obj, arm = make_fixture(fixture, owned)
        results["relative_rotation_scale_masks"] = check_bake(obj)
        obj.modifiers[0].use_deform_preserve_volume = True
        results["preserve_volume"] = check_bake(obj)
        obj.modifiers[0].use_deform_preserve_volume = False
        # Retained downstream modifier and full-stack evaluation.
        modifier = obj.modifiers.new("Subdivision", 'SUBSURF')
        modifier.levels = 1
        results["downstream_subdivision"] = check_bake(obj)
        obj.modifiers.remove(modifier)
        # Key actions and driver targets must stay independent.
        keys = obj.data.shape_keys
        first = keys.key_blocks[1]
        first.keyframe_insert(data_path="value", frame=bpy.context.scene.frame_current)
        action = keys.animation_data.action
        results["shape_key_action"] = check_bake(obj)
        keys.animation_data_clear()
        if action.users == 0:
            bpy.data.actions.remove(action)
        keys["test_strength"] = 0.4
        curve = first.driver_add("value")
        variable = curve.driver.variables.new()
        variable.name = "strength"
        variable.type = 'SINGLE_PROP'
        variable.targets[0].id_type = 'KEY'
        variable.targets[0].id = keys
        variable.targets[0].data_path = '["test_strength"]'
        curve.driver.expression = "strength"
        bpy.context.view_layer.update()
        results["self_referencing_key_driver"] = check_bake(obj)
        keys.animation_data_clear()
        constraint = arm.pose.bones[0].constraints.new('LIMIT_ROTATION')
        constraint.use_limit_x = True
        constraint.min_x = -0.1
        constraint.max_x = 0.1
        results["constrained_rig_snapshot"] = check_bake(obj)
        arm.pose.bones[0].constraints.remove(constraint)
        # Absolute keys must retain frames/interpolation and work between keys.
        obj.data.shape_keys.use_relative = False
        obj.data.shape_keys.eval_time = 7
        results["absolute_keys"] = check_bake(obj)
        obj.data.shape_keys.use_relative = True
        obj.modifiers[0].use_bone_envelopes = True
        expect_failure(obj, "envelopes")
        obj.modifiers[0].use_bone_envelopes = False
        arm.data.bones[0].bbone_segments = 2
        expect_failure(obj, "B-Bones")
        arm.data.bones[0].bbone_segments = 1
        # Inject a comparison failure after output allocation to test rollback.
        original_compare = candidate._uma_rest_pose_compare
        def fail(*args):
            raise ValueError("Injected geometry failure")
        candidate._uma_rest_pose_compare = fail
        try:
            expect_failure(obj, "Injected geometry failure")
        finally:
            candidate._uma_rest_pose_compare = original_compare
        results["rejection_and_rollback"] = "passed"
        obj.shape_key_clear()
        results["mesh_without_keys"] = check_bake(obj)
        # Use the current connected project; never load a stale test project.
        live = bpy.data.objects.get("UMA30_Body.001")
        if live:
            results["live_23_key_mesh"] = check_bake(live)
    finally:
        if bpy.context.object and bpy.context.object.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        candidate._uma_rest_pose_remove(owned)
        bpy.data.collections.remove(fixture)
        for item in list(bpy.context.selected_objects):
            item.select_set(False)
        for item in old_selected:
            item.select_set(True)
        bpy.context.view_layer.objects.active = old_active
    assert fingerprint(original_objects) == original_fingerprint, "Scene originals changed"
    assert counts() == original_counts, ("Scene data counts changed", original_counts, counts())
    report = {
        "blender_version": bpy.app.version_string, "blend": bpy.data.filepath,
        "unity_project": str(ROOT), "unity_version": project_version,
        "installed_uma_version": uma_version, "originals_unchanged": True,
        "temporary_data_removed": True, "results": results
    }
    target = ROOT / "Build/Validation/BlenderRestPose/results.json"
    target.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: ({"keys": v["shape_keys"], "checks": len(v["checks"]),
                          "max_error": v["max_error"]} if isinstance(v, dict) else v)
                      for k, v in results.items()}, indent=2))
    print("Originals unchanged; temporary data removed.")
    return report

if __name__ == "__main__":
    run()

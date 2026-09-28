"""Export raw CURVES paths and a source mesh to the UMA guide interchange.

Requires a host application providing bpy and mathutils; not standalone Python.
Example (replace <authoring-app> with that application's executable path):
"<authoring-app>" --background --factory-startup --disable-autoexec --python-exit-code 1 \
  --python export_authored_guides.py -- --blend style.blend --source UMA30_Body \
  --guides Pointy --growth-mesh scalp_pointy.001 --output PointyGuides.json

The .blend is opened read-only in effect: scripts are disabled and it is never saved.
Output must not already exist. No third-party hair-generation add-on is required.
"""
import argparse
import json
import sys
from collections import Counter
from pathlib import Path

import bpy
from mathutils.bvhtree import BVHTree


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--blend', required=True)
    parser.add_argument('--source', required=True, help='Unmodified mesh object, not evaluated hair cards')
    parser.add_argument('--guides', required=True, help='Raw CURVES object with ordered root-to-tip points')
    parser.add_argument('--growth-mesh', help='Separate scalp mesh defining the growth footprint')
    parser.add_argument('--growth-group', help='Alternatively, a vertex group on the source mesh')
    parser.add_argument('--evaluated-growth', action='store_true', help='Use the growth mesh after its surface-conforming modifiers; never evaluates guide generators')
    parser.add_argument('--output', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    output = Path(args.output)
    if output.exists():
        raise ValueError('Output already exists; choose another filename: ' + str(output))
    if args.growth_mesh and args.growth_group:
        raise ValueError('Choose a growth mesh OR a source vertex group')
    bpy.ops.wm.open_mainfile(filepath=args.blend, load_ui=False, use_scripts=False)
    body = bpy.data.objects.get(args.source)
    hair = bpy.data.objects.get(args.guides)
    if body is None or body.type != 'MESH' or hair is None or hair.type != 'CURVES':
        raise ValueError('Source must be a mesh; guides must be a raw CURVES object')
    unit = bpy.context.scene.unit_settings.scale_length
    if unit <= 0:
        raise ValueError('Scene unit scale must be positive')

    def position(p):
        return {'x': float(p.x * unit), 'y': float(p.z * unit), 'z': float(p.y * unit)}

    vertices = [body.matrix_world @ v.co for v in body.data.vertices]
    normal_matrix = body.matrix_world.to_3x3().inverted().transposed()
    normals = [(normal_matrix @ v.normal).normalized() for v in body.data.vertices]
    body.data.calc_loop_triangles()
    # Source Z-up to Unity Y-up swaps two axes and handedness. Reflect winding too.
    reflected = body.matrix_world.to_3x3().determinant() > 0
    triangles = [i for t in body.data.loop_triangles for i in
                 ((t.vertices[0], t.vertices[2], t.vertices[1]) if reflected else tuple(t.vertices))]
    uv = [{'x': 0., 'y': 0.} for _ in vertices]
    if body.data.uv_layers.active:
        for loop in body.data.loops:
            p = body.data.uv_layers.active.data[loop.index].uv
            uv[loop.vertex_index] = {'x': float(p.x), 'y': float(p.y)}
    growth = [0.] * len(vertices)
    if args.growth_group:
        group = body.vertex_groups.get(args.growth_group)
        if group is None:
            raise ValueError('Growth vertex group was not found')
        for v in body.data.vertices:
            growth[v.index] = next((min(1., max(0., g.weight)) for g in v.groups if g.group == group.index), 0.)
    elif args.growth_mesh:
        scalp = bpy.data.objects.get(args.growth_mesh)
        if scalp is None or scalp.type != 'MESH':
            raise ValueError('Growth mesh was not found')
        if args.evaluated_growth:
            scalp = scalp.evaluated_get(bpy.context.evaluated_depsgraph_get())
        sv = [scalp.matrix_world @ v.co for v in scalp.data.vertices]
        polygons = [tuple(p.vertices) for p in scalp.data.polygons]
        tree = BVHTree.FromPolygons(sv, polygons)
        uses = Counter(tuple(sorted((p[i - 1], p[i]))) for p in polygons for i in range(len(p)))
        boundary = [(sv[a], sv[b]) for (a, b), count in uses.items() if count == 1]

        def on_border(p):
            for a, b in boundary:
                edge = b - a
                t = max(0., min(1., (p - a).dot(edge) / max(edge.length_squared, 1e-20)))
                if (p - a - edge * t).length_squared * unit * unit < 1e-10:
                    return True
            return False

        for i, (p, n) in enumerate(zip(vertices, normals)):
            nearest = tree.find_nearest(p)
            ray = tree.ray_cast(p - n * (.025 / unit), n, .075 / unit)
            interior = nearest[0] is not None and nearest[3] * unit < .03 and nearest[1].dot(n) > .2 and not on_border(nearest[0])
            growth[i] = 1. if ray[0] is not None or interior else 0.
    else:
        print('No growth footprint supplied: paint Growth / Density after importing.')
    guides = []
    for index, curve in enumerate(hair.data.curves):
        if not 2 <= curve.points_length <= 256:
            raise ValueError('Each raw guide needs 2–256 points; resample it in the source application first')
        guides.append({'name': 'Imported guide %03d' % (index + 1), 'points': [
            position(hair.matrix_world @ hair.data.points[p].position)
            for p in range(curve.first_point_index, curve.first_point_index + curve.points_length)]})
    payload = {'version': 1, 'name': hair.name, 'vertices': [position(v) for v in vertices],
               'normals': [{'x': float(n.x), 'y': float(n.z), 'z': float(n.y)} for n in normals],
               'triangles': triangles, 'uv': uv, 'growth': growth, 'guides': guides}
    # Exclusive creation: do not overwrite somebody's groom interchange file.
    with output.open('x', encoding='utf-8') as stream:
        json.dump(payload, stream, separators=(',', ':'), allow_nan=False)
    print('Exported %d authored guides and %d source vertices to %s' % (len(guides), len(vertices), output))


if __name__ == '__main__':
    main()

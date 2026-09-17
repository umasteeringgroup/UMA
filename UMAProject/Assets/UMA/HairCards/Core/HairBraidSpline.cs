using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public enum HairRailAttachment { Free, Surface, Helper }

    [Serializable]
    public sealed class HairRailEnd
    {
        public HairRailAttachment attachment;
        public string helperId;
        public Vector3 helperOffset;
        public float surfaceOffset = .01f;
        [Range(.01f, 1)] public float influence = .25f;
        public void EnsureIntegrity()
        {
            surfaceOffset = HairGatherSettings.Finite(surfaceOffset, 0, 1);
            influence = HairGatherSettings.Finite(influence, .01f, 1);
            if (!float.IsFinite(helperOffset.sqrMagnitude)) helperOffset = Vector3.zero;
        }
    }

    [Serializable]
    public sealed class HairRailSettings
    {
        // Points remain source-local, as in existing rails. Parenting adds only the
        // change since binding, preserving existing geometry and all point tools.
        public string parentHelperId;
        public Matrix4x4 parentBindMatrix = Matrix4x4.identity;
        public bool followSurface;
        public float surfaceOffset = .01f;
        public Mesh surfaceMesh;
        public Vector3 surfacePosition;
        public Quaternion surfaceRotation = Quaternion.identity;
        public Vector3 surfaceScale = Vector3.one;
        public HairRailEnd root = new HairRailEnd(), tip = new HairRailEnd();
        public Matrix4x4 SurfaceMatrix => surfaceMesh == null ? Matrix4x4.identity :
            Matrix4x4.TRS(surfacePosition, surfaceRotation, surfaceScale);
        public void EnsureIntegrity()
        {
            surfaceOffset = HairGatherSettings.Finite(surfaceOffset, 0, 1);
            root ??= new HairRailEnd(); tip ??= new HairRailEnd(); root.EnsureIntegrity(); tip.EnsureIntegrity();
        }
    }

    public static class HairRailUtility
    {
        public static bool IsRail(HairHelper h) => h != null && (h.type == HairHelperType.BraidRail || h.type == HairHelperType.CurveRail);
        public static bool IsTarget(HairHelper h) => h != null && !IsRail(h) && h.type != HairHelperType.GuideGrid;
        public static Matrix4x4 TargetMatrix(HairGroomAsset groom, HairHelper h) => h.type == HairHelperType.Bun ? HairBunUtility.Matrix(groom, h) : h.LocalToSource;
        public static Matrix4x4 PointMatrix(HairGroomAsset groom, HairHelper rail)
        {
            var parent = groom.FindHelper(rail.rail.parentHelperId);
            if (!IsTarget(parent) || !HairCoordinateUtility.IsInvertibleAffine(rail.rail.parentBindMatrix)) return Matrix4x4.identity;
            return TargetMatrix(groom, parent) * rail.rail.parentBindMatrix.inverse;
        }
        public static void BindParent(HairGroomAsset groom, HairHelper rail, string id)
        {
            // Reparent without a visible jump. No implicit rebake of surface constraints.
            Matrix4x4 old = PointMatrix(groom, rail);
            for (int i = 0; i < rail.points.Count; i++) rail.points[i] = old.MultiplyPoint3x4(rail.points[i]);
            rail.position = old.MultiplyPoint3x4(rail.position); rail.rotation = old.rotation * rail.rotation;
            var parent = groom.FindHelper(id);
            rail.rail.parentHelperId = IsTarget(parent) ? id : null;
            rail.rail.parentBindMatrix = IsTarget(parent) ? TargetMatrix(groom, parent) : Matrix4x4.identity;
        }
        public static HairHelper CreateBunBraid(HairGroomAsset groom, HairHelper bun)
        {
            if (bun?.type != HairHelperType.Bun) throw new ArgumentException("A Bun helper is required.");
            var rail = HairHelper.Derived(bun.Id + ":" + HairBunPart.SurroundingBraid + ":0");
            rail.type = HairHelperType.BraidRail; rail.name = "Braid spline - " + bun.name;
            var matrix = HairBunUtility.Matrix(groom, bun);
            rail.position = matrix.MultiplyPoint3x4(new Vector3(0, bun.bun.braidHeight, 0));
            rail.rotation = matrix.rotation; rail.braidScale = bun.braidScale;
            for (int i = 0; i <= 16; i++)
            {
                float a = (i / 16f * (1 + bun.bun.braidOverlap) + .5f) * Mathf.PI * 2;
                rail.points.Add(matrix.MultiplyPoint3x4(new Vector3(Mathf.Sin(a) * bun.bun.braidRadius, bun.bun.braidHeight, -Mathf.Cos(a) * bun.bun.braidRadius)));
            }
            rail.rail.parentHelperId = bun.Id; rail.rail.parentBindMatrix = matrix;
            rail.EnsureIntegrity(); return rail;
        }
    }

    /// <summary>Non-destructive rail placement shared by generation and scene editing.</summary>
    public sealed class HairRailWorkspace
    {
        internal readonly List<Vector3> controls = new List<Vector3>();
        internal Matrix4x4 pointMatrix = Matrix4x4.identity;
        internal Quaternion frame = Quaternion.identity;
        public IReadOnlyList<Vector3> Controls => controls;
        public Matrix4x4 PointMatrix => pointMatrix;
        public Quaternion Frame => frame;
        private readonly HairGroomEvaluator.SourceMeshReadCache editorSource = new HairGroomEvaluator.SourceMeshReadCache();
        private readonly HairGroomEvaluator.SourceMeshReadCache customMesh = new HairGroomEvaluator.SourceMeshReadCache();
        private HairMeshRaycaster surface;
        private Matrix4x4 surfaceMatrix, inverseSurface, normalMatrix;
        private HairRailSettings settings;
        private Vector3 rootTarget, tipTarget, rootSurfaceDelta, tipSurfaceDelta;
        private bool rootAttached, tipAttached;
        private readonly List<float> distances = new List<float>();

        public void Clear() { controls.Clear(); distances.Clear(); customMesh.Clear(); editorSource.Clear(); surface = null; settings = null; }

        public void Prepare(HairGroomAsset groom, HairHelper helper, List<string> warnings = null)
        {
            editorSource.Begin(groom.SourceMesh);
            Prepare(groom, helper, editorSource, warnings);
        }

        internal void Prepare(HairGroomAsset groom, HairHelper helper, HairGroomEvaluator.SourceMeshReadCache source, List<string> warnings = null)
        {
            settings = helper.rail; pointMatrix = HairRailUtility.PointMatrix(groom, helper);
            frame = pointMatrix.rotation * helper.rotation; controls.Clear(); distances.Clear();
            foreach (var point in helper.points) controls.Add(pointMatrix.MultiplyPoint3x4(point));
            if (!string.IsNullOrEmpty(settings.parentHelperId) && !HairRailUtility.IsTarget(groom.FindHelper(settings.parentHelperId)))
                warnings?.Add($"{helper.name}: missing/invalid spline parent. Using saved source-local controls.");
            surface = null; surfaceMatrix = settings.SurfaceMatrix;
            if (settings.followSurface || settings.root.attachment == HairRailAttachment.Surface || settings.tip.attachment == HairRailAttachment.Surface)
            {
                if (HairCoordinateUtility.IsInvertibleAffine(surfaceMatrix))
                {
                    inverseSurface = surfaceMatrix.inverse; normalMatrix = inverseSurface.transpose;
                    if (settings.surfaceMesh == null) surface = source.Surface();
                    else { customMesh.Begin(settings.surfaceMesh); surface = customMesh.Surface(); }
                }
                if (surface?.TriangleCount == 0) surface = null;
                if (surface == null) warnings?.Add($"{helper.name}: surface placement needs a readable mesh and invertible surface transform; keeping freeform placement.");
            }
            rootAttached = tipAttached = false;
            if (controls.Count == 0) return;
            rootAttached = ResolveEnd(settings.root, controls[0], out rootTarget);
            tipAttached = ResolveEnd(settings.tip, controls[^1], out tipTarget);
            float length = 0; distances.Add(0);
            for (int i = 1; i < controls.Count; i++) { length += Vector3.Distance(controls[i - 1], controls[i]); distances.Add(length); }
            Vector3 rootDelta = rootTarget - controls[0], tipDelta = tipTarget - controls[^1];
            for (int i = 0; i < controls.Count; i++)
            {
                float t = length > 1e-8f ? distances[i] / length : i / Mathf.Max(1f, controls.Count - 1f);
                if (rootAttached) controls[i] += rootDelta * Mathf.SmoothStep(1, 0, t / settings.root.influence);
                if (tipAttached) controls[i] += tipDelta * Mathf.SmoothStep(1, 0, (1 - t) / settings.tip.influence);
            }
            if (rootAttached) controls[0] = rootTarget;
            if (tipAttached) controls[^1] = tipTarget;
            rootSurfaceDelta = tipSurfaceDelta = Vector3.zero;
            if (settings.followSurface)
            {
                if (rootAttached && Project(rootTarget, settings.surfaceOffset, out var rootOnSurface, out _)) rootSurfaceDelta = rootTarget - rootOnSurface;
                if (tipAttached && Project(tipTarget, settings.surfaceOffset, out var tipOnSurface, out _)) tipSurfaceDelta = tipTarget - tipOnSurface;
            }

            bool ResolveEnd(HairRailEnd end, Vector3 free, out Vector3 target)
            {
                target = free;
                if (end.attachment == HairRailAttachment.Surface) return Project(free, end.surfaceOffset, out target, out _);
                if (end.attachment != HairRailAttachment.Helper) return false;
                var anchor = groom.FindHelper(end.helperId);
                if (!HairRailUtility.IsTarget(anchor))
                { warnings?.Add($"{helper.name}: assign a valid {(ReferenceEquals(end, settings.root) ? "root" : "tip")} attachment helper; endpoint remains free."); return false; }
                target = HairRailUtility.TargetMatrix(groom, anchor).MultiplyPoint3x4(end.helperOffset); return true;
            }
        }

        public bool Project(Vector3 point, float offset, out Vector3 projected, out Vector3 normal)
        {
            projected = point; normal = Vector3.up;
            if (surface == null || !surface.ClosestPoint(inverseSurface.MultiplyPoint3x4(point), out var hit)) return false;
            // Smooth normals avoid a stepped offset path at every scalp triangle edge.
            var localNormal = surface.TryGetSurfaceNormal(hit.TriangleIndex, hit.Barycentric, out var smoothNormal) ? smoothNormal : hit.Normal;
            normal = normalMatrix.MultiplyVector(localNormal).normalized;
            projected = surfaceMatrix.MultiplyPoint3x4(hit.Point) + normal * offset;
            return true;
        }

        public Vector3 Sample(float t, bool smooth)
        {
            Vector3 point = HairFormUtility.RailPoint(controls, t, smooth);
            if (settings.followSurface && Project(point, settings.surfaceOffset, out var projected, out _)) point = projected;
            // Endpoint attachments take priority over whole-spine snapping. Blend out of
            // the scalp route smoothly when an endpoint is attached to an off-scalp helper.
            if (settings.followSurface)
            {
                if (rootAttached) point += rootSurfaceDelta * Mathf.SmoothStep(1, 0, t / settings.root.influence);
                if (tipAttached) point += tipSurfaceDelta * Mathf.SmoothStep(1, 0, (1 - t) / settings.tip.influence);
            }
            if (t <= 0 && rootAttached) return rootTarget;
            if (t >= 1 && tipAttached) return tipTarget;
            return point;
        }
    }
}

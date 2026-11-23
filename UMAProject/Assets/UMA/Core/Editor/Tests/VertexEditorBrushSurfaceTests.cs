#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Vertex Editor's brush: which surface the ray finds, which way the brush faces on two-layer
    /// cloth, and the normals the Normals tools work out.
    /// </summary>
    public sealed class VertexEditorBrushSurfaceTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private static object Call(string method, params object[] arguments)
        {
            MethodInfo info = typeof(VertexEditorStage).GetMethod(method, PrivateStatic);
            Assert.That(info, Is.Not.Null, "VertexEditorStage." + method);
            return info.Invoke(null, arguments);
        }

        [Test]
        public void RayHitsOnlyTheFrontOfATriangle()
        {
            // Cross(b - a, c - a) points to -Z: the triangle faces a ray travelling +Z.
            Vector3 a = Vector3.zero, b = Vector3.up, c = Vector3.right;
            var arguments = new object[] { new Vector3(0.2f, 0.2f, -1f), Vector3.forward, a, b, c, 0f };
            Assert.That((bool)Call("RayHitsFrontOfTriangle", arguments), Is.True);
            Assert.That((float)arguments[5], Is.EqualTo(1f).Within(1e-5f));

            arguments = new object[] { new Vector3(0.2f, 0.2f, 1f), Vector3.back, a, b, c, 0f };
            Assert.That((bool)Call("RayHitsFrontOfTriangle", arguments), Is.False, "Back faces are not hit.");

            arguments = new object[] { new Vector3(2f, 2f, -1f), Vector3.forward, a, b, c, 0f };
            Assert.That((bool)Call("RayHitsFrontOfTriangle", arguments), Is.False, "Outside the triangle.");
        }

        [Test]
        public void BrushFacesOutOnTwoLayerCloth()
        {
            // An outer layer facing +Z and, 3 mm behind it, a denser inner layer facing -Z, as on the
            // UMA 2 chainmail helmet. Every normal within the radius averages to pointing inward.
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            for (int x = -2; x <= 2; x++)
            {
                vertices.Add(new Vector3(x * 0.005f, 0f, 0f));
                normals.Add(Vector3.forward);
            }
            for (int x = -4; x <= 4; x++)
            {
                vertices.Add(new Vector3(x * 0.0025f, 0f, -0.003f));
                normals.Add(Vector3.back);
            }
            Vector3 all = Vector3.zero;
            foreach (Vector3 normal in normals) all += normal;
            Assert.That(all.z, Is.LessThan(0f), "Averaging both layers points the brush inward.");

            Func<float, float> falloff = x => 1f - x;
            var brush = (Vector3)Call("AverageFacingNormal",
                vertices, normals, 0, vertices.Count, Vector3.zero, 0.028f, Vector3.forward, falloff);
            Assert.That(Vector3.Dot(brush, Vector3.forward), Is.GreaterThan(0.999f));

            // Nothing on the hit side within the radius: the brush faces the hit face.
            var none = (Vector3)Call("AverageFacingNormal",
                vertices, normals, 0, vertices.Count, new Vector3(1f, 0f, 0f), 0.028f, Vector3.up, falloff);
            Assert.That(none, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void TangentsStayPerpendicularAndKeepTheirHandedness()
        {
            Vector3 normal = new Vector3(0f, 0.6f, 0.8f);
            var tangent = (Vector4)Call("OrthogonalTangent", new Vector4(1f, 1f, 0f, -1f), normal);
            Assert.That(Vector3.Dot(new Vector3(tangent.x, tangent.y, tangent.z), normal), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(new Vector3(tangent.x, tangent.y, tangent.z).magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(tangent.w, Is.EqualTo(-1f));
        }

        [Test]
        public void VertexNormalAdjustmentKeepsTangentHandedness()
        {
            var adjustment = new VertexNormalAdjustment
            {
                vertexIndex = 0,
                weight = 1f,
                normal = Vector3.up,
                tangent = Vector3.right,
                rotation = Quaternion.identity
            };
            var mesh = new MeshDetails
            {
                normals = new[] { Vector3.forward },
                tangents = new[] { new Vector4(0f, 1f, 0f, -1f) }
            };
            adjustment.Apply(mesh, mesh);
            Assert.That(mesh.normals[0], Is.EqualTo(Vector3.up));
            Assert.That(mesh.tangents[0], Is.EqualTo(new Vector4(1f, 0f, 0f, -1f)));

            adjustment.weight = 0.5f;
            mesh.tangents[0] = new Vector4(0f, 1f, 0f, -1f);
            adjustment.Apply(mesh, mesh);
            Assert.That(mesh.tangents[0].w, Is.EqualTo(-1f));
        }
    }
}
#endif

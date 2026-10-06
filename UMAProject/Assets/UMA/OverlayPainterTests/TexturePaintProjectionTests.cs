#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintProjectionTests
    {
        private const string Folder = "Assets/UMAProjectData/Tests/OverlayPainter/GeneratedProjectionTests";
        private readonly List<Object> objects = new List<Object>();
        private readonly List<TextureStore> stores = new List<TextureStore>();
        private readonly List<TextureSet> sets = new List<TextureSet>();
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static float ProjectionGizmoScale => (float)typeof(TexturePaintStageWindow)
            .GetField("projectionGizmoScale", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        private T Own<T>(T value) where T : Object { objects.Add(value); return value; }
        [TearDown] public void TearDown()
        {
            foreach (var store in stores) store.Dispose();
            foreach (var set in sets) set.Dispose();
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            TexturePaintSpriteSource.ClearCache();
            objects.Clear(); stores.Clear(); sets.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }
        private Texture2D Image(Color color)
        {
            var texture = Own(new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[256]; Array.Fill(pixels, color); texture.SetPixels(pixels); texture.Apply(); return texture;
        }
        private Mesh Quad(float minX = -0.5f, float maxX = 0.5f, float z = 0, bool reverse = false)
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { new Vector3(minX,-0.5f,z),new Vector3(maxX,-0.5f,z),new Vector3(maxX,0.5f,z),new Vector3(minX,0.5f,z) };
            mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
            mesh.triangles = reverse ? new[] { 0,2,1,0,3,2 } : new[] { 0,1,2,0,2,3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private TextureSet Set(Mesh mesh = null)
        {
            var set = new TextureSet { persistentId = Guid.NewGuid().ToString("N"), surface = new ReconstructedSurface
            { mesh = mesh != null ? mesh : Quad(), index = sets.Count, slotName = "Body", slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" }, triangleIslands = new[] { 0,0 } } };
            AddChannel(set, TexturePaintChannel.Albedo); sets.Add(set); return set;
        }
        private static void AddChannel(TextureSet set, TexturePaintChannel channel, int index = 0)
        {
            set.channels[channel] = new TextureChannelTarget { channel = channel, umaChannelIndex = index,
                materialProperty = "_" + channel, sourceKeyword = channel.ToString(), format = RenderTextureFormat.ARGBHalf,
                editable = new EditableTextureTarget("Projection test",64,64,RenderTextureFormat.ARGBHalf,null,Color.clear) };
        }
        private TextureStore Store(params TextureSet[] members)
        {
            var store = new TextureStore();
            var list = (List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store);
            list.AddRange(members); foreach(var set in members) sets.Remove(set); stores.Add(store); return store;
        }
        private TexturePaintProjectionSettings Definition(Texture2D image = null) => new TexturePaintProjectionSettings
        { texture = image != null ? image : Image(Color.red), placed = true, width = 1, height = 1, depth = 0.4f,
            fade = TexturePaintProjectionFade.None, depthFade = 0, visibilityResolution = 64 };
        private static Color Pixel(TexturePaintLayer layer, int x = 32, int y = 32, TexturePaintChannel channel = TexturePaintChannel.Albedo)
        {
            RenderTexture previous = RenderTexture.active; var read = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try { RenderTexture.active = layer.channels[channel].Front; read.ReadPixels(new Rect(x,y,1,1),0,0); read.Apply(); return read.GetPixel(0,0); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(read); }
        }
        private static void Generate(TexturePaintProjectionSettings settings, TextureSet[] all, TextureSet[] targets, params TexturePaintLayer[] layers)
        {
            using var renderer = new TexturePaintProjectionRenderer();
            Assert.That(renderer.Generate(all, targets, layers, settings, out string error), Is.True, error);
        }
        private static object Invoke(TexturePaintStageWindow stage, string method, params object[] args)
            => typeof(TexturePaintStageWindow).GetMethod(method, Private).Invoke(stage,args);

        private void WithStage(TextureStore store, Action<TexturePaintStageWindow> action,
            TexturePaintLogicalLayerController logical = null, MeshReconstructionResult reconstruction = null)
        {
            var controller = new TexturePaintStageController();
            typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller, store);
            typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller, logical);
            typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller, reconstruction);
            var stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            typeof(TexturePaintStageWindow).GetField("controller", Private).SetValue(stage, controller);
            typeof(TexturePaintStageWindow).GetField("transientBrush", Private).SetValue(stage, Own(ScriptableObject.CreateInstance<BrushPreset>()));
            try { action(stage); }
            finally
            {
                Invoke(stage, "ClearLightweightHistory");
                typeof(TexturePaintStageWindow).GetField("controller", Private).SetValue(stage, null);
            }
        }

        [Test] public void WarpSubdivisionPreservesShapeAndPinsAndRefitSkipsPinnedPoints()
        {
            var settings=Definition();settings.mode=TexturePaintProjectionMode.Wrapped;
            settings.points[4]=new Vector3(.08f,.03f,.1f);settings.pinned[4]=true;
            var before=settings.Clone();settings.ResizeGrid(5);settings.ResizeGrid(9);
            Assert.That(settings.pinned[40],Is.True);
            for(int y=0;y<=16;y++)for(int x=0;x<=16;x++)
                Assert.That(Vector3.Distance(before.Evaluate(x/16f,y/16f),settings.Evaluate(x/16f,y/16f)),Is.LessThan(.00001f));
            var pinned=settings.points[40];var set=Set();new TexturePaintProjectionGeometry(new[]{set}).Fit(settings);
            Assert.That(settings.points[40],Is.EqualTo(pinned));Assert.That(settings.points[0].z,Is.EqualTo(0).Within(.00001));
            var clone=settings.Clone();clone.pinned[40]=false;clone.points[40]=Vector3.one;
            Assert.That(settings.pinned[40],Is.True);Assert.That(settings.points[40],Is.EqualTo(pinned));
            var mirrored=TexturePaintSymmetry.TransformProjection(settings,Matrix4x4.Scale(new Vector3(-1,1,1)));
            Assert.That(mirrored.points.Length,Is.EqualTo(81));Assert.That(mirrored.pinned[40],Is.True);
        }
        [Test] public void WarpDragIsOneUndoStepAndRestoresPinsAndPixels()
        {
            var set=Set();var layer=set.AddProjectionLayer();var settings=Definition();settings.mode=TexturePaintProjectionMode.Wrapped;
            Generate(settings,new[]{set},new[]{set},layer);
            WithStage(Store(set),stage=>
            {
                for(int i=1;i<=4;i++)
                {
                    settings.points[4].x=i*.01f;settings.pinned[0]=true;
                    Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,true),Is.True);
                }
                Invoke(stage,"FinishProjectionEdit");
                Assert.That(set.layers[0].projectionSettings.points[4].x,Is.EqualTo(.04f));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);
                Assert.That(set.layers[0],Is.SameAs(layer));Assert.That(layer.projectionSettings.pinned[0],Is.False);
                Assert.That(layer.projectionSettings.points[4].x,Is.Zero);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.False);Assert.That(Invoke(stage,"RedoLightweight"),Is.True);
                Assert.That(set.layers[0].projectionSettings.pinned[0],Is.True);Assert.That(Pixel(set.layers[0]).a,Is.GreaterThan(.99));
            });
        }
        [Test] public void CylinderWrapCoversEverySideAndKeepsChannelsAligned()
        {
            var settings=Definition();settings.mode=TexturePaintProjectionMode.Cylindrical;
            settings.width=2*Mathf.PI;settings.height=1;settings.depth=.15f;settings.depthFromSurface=true;
            settings.fade=TexturePaintProjectionFade.Rectangle;settings.edgeWidth=.1f;
            settings.firstSurfaceOnly=true;settings.connectedSurfaceOnly=false;
            Assert.That(Vector3.Distance(settings.Evaluate(0,.5f),settings.Evaluate(1,.5f)),Is.LessThan(.00001));
            Assert.That(Vector3.Angle(settings.Normal(.5f,.5f),Vector3.forward),Is.LessThan(.01));
            Assert.That(Vector3.Angle(settings.Normal(0,.5f),Vector3.back),Is.LessThan(.01));
            const int segments=64;var vertices=new Vector3[(segments+1)*2];var uv=new Vector2[vertices.Length];var triangles=new int[segments*6];
            for(int x=0;x<=segments;x++)for(int y=0;y<2;y++)
            {int i=x*2+y;uv[i]=new Vector2((float)x/segments,y);vertices[i]=settings.Evaluate(uv[i].x,uv[i].y);}
            for(int x=0;x<segments;x++) {int p=x*2,t=x*6;triangles[t]=p;triangles[t+1]=p+2;triangles[t+2]=p+3;triangles[t+3]=p;triangles[t+4]=p+3;triangles[t+5]=p+1;}
            var mesh=Own(new Mesh {vertices=vertices,uv=uv,triangles=triangles});mesh.RecalculateNormals();mesh.RecalculateBounds();
            var set=Set(mesh);AddChannel(set,TexturePaintChannel.Normal);
            settings.UpgradeChannelSources();settings.GetChannelSource(TexturePaintChannel.Normal,true).texture=Image(new Color(.5f,.5f,1,1));
            var layer=set.AddProjectionLayer();Generate(settings,new[]{set},new[]{set},layer);
            for(int x=2;x<62;x+=3)
            {
                Assert.That(Pixel(layer,x,32).a,Is.GreaterThan(.98),"Uncovered cylinder UV column "+x);
                Assert.That(Pixel(layer,x,32,TexturePaintChannel.Normal).a,Is.EqualTo(Pixel(layer,x,32).a).Within(.01));
            }
        }

        [Test] public void SurfacePlacementAlignsNormalAndTransportsExistingSpin()
        {
            var settings = new TexturePaintProjectionSettings { width = .31f, height = .17f, depth = .08f };
            settings.PlaceOnSurface(new Vector3(3, 2, 1), Vector3.up, "first", 2);
            Assert.That(Vector3.Angle(settings.rotation * Vector3.forward, Vector3.up), Is.LessThan(.01f));
            settings.rotation *= Quaternion.AngleAxis(37f, Vector3.forward);
            Quaternion before = settings.rotation;
            Vector3 normal = new Vector3(1, 2, 3).normalized;
            settings.points[4].z = .1f;
            settings.PlaceOnSurface(Vector3.one, normal, "second", 7);
            Assert.That(Quaternion.Angle(settings.rotation, Quaternion.FromToRotation(Vector3.up, normal) * before), Is.LessThan(.05f));
            Assert.That(Vector3.Angle(settings.rotation * Vector3.forward, normal), Is.LessThan(.01f));
            Assert.That(settings.position, Is.EqualTo(Vector3.one));
            Assert.That(settings.width, Is.EqualTo(.31f)); Assert.That(settings.height, Is.EqualTo(.17f));
            Assert.That(settings.BackDepth, Is.EqualTo(-.08f)); Assert.That(settings.FrontDepth, Is.EqualTo(.08f));
            Assert.That(settings.depthFromSurface, Is.True); Assert.That(settings.points[4], Is.EqualTo(Vector3.zero));
            Assert.That(settings.regionSurfaceId, Is.EqualTo("second")); Assert.That(settings.regionTriangle, Is.EqualTo(7));
        }

        [Test] public void CreatingProjectionFindsTransformedSurfaceAndIsOneUndoStep()
        {
            var set = Set(); var go = Own(new GameObject("Projection surface"));
            go.transform.SetPositionAndRotation(new Vector3(12, 7, -4), Quaternion.Euler(25, 40, 15));
            go.transform.localScale = new Vector3(2, 3, 1); set.surface.gameObject = go;
            WithStage(Store(set), stage =>
            {
                Invoke(stage, "AddProjectionLayer", set);
                Assert.That(set.layers.Count, Is.EqualTo(1));
                var settings = set.layers[0].projectionSettings.Clone();
                Assert.That(settings.placed, Is.True); Assert.That(settings.lockAspect, Is.False);
                Vector3 local = go.transform.InverseTransformPoint(settings.position);
                Assert.That(Mathf.Abs(local.z), Is.LessThan(.00001f));
                Assert.That(local.x, Is.InRange(-.5001f, .5001f)); Assert.That(local.y, Is.InRange(-.5001f, .5001f));
                Assert.That(Vector3.Angle(settings.rotation * Vector3.forward, go.transform.forward), Is.LessThan(.05f));
                Assert.That(settings.regionSurfaceId, Is.EqualTo(set.persistentId));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Assert.That(set.layers, Is.Empty);
                Assert.That(Invoke(stage, "UndoLightweight"), Is.False, "Placement belongs to the layer creation operation.");
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(settings.position));
                Assert.That(set.layers[0].projectionSettings.rotation, Is.EqualTo(settings.rotation));
            });
        }

        [Test] public void RepeatedSurfaceClicksMoveImmediatelyAndUndoIndividually()
        {
            var set = Set(); var other = Set(); set.AddProjectionLayer();
            WithStage(Store(set, other), stage =>
            {
                foreach (float x in new[] { -.2f, .2f })
                    Assert.That(Invoke(stage, "PlaceProjectionAtSurface", set, set.layers[0], set,
                        new Vector3(x, 0, 0), Vector3.forward, 0), Is.True);
                Assert.That(set.layers[0].projectionSettings.position.x, Is.EqualTo(.2f));
                Assert.That(Invoke(stage, "PlaceProjectionAtSurface", set, set.layers[0], other,
                    Vector3.zero, Vector3.up, 0), Is.False);
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0].projectionSettings.position.x, Is.EqualTo(-.2f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0].projectionSettings.placed, Is.False);
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                Assert.That(set.layers[0].projectionSettings.position.x, Is.EqualTo(.2f));
            });
        }

        [Test] public void ClickAcrossUdimMembersMovesTheWholeLogicalProjection()
        {
            var first = Set(Quad(-.5f, 0)); var second = Set(Quad(0, .5f)); var store = Store(first, second);
            var reconstruction = new MeshReconstructionResult(); reconstruction.logicalTargets.Rebuild(new[] { first.surface });
            var target = reconstruction.logicalTargets.Targets[0]; target.isUdim = true;
            target.members[0].udimTileNumber = 1001; target.members[0].textureSets.Add(first);
            var member = new TexturePaintLogicalTargetMember { slotName = "Body2", udimTileNumber = 1002 };
            member.textureSets.Add(second); target.members.Add(member);
            var logical = new TexturePaintLogicalLayerController(reconstruction.logicalTargets);
            WithStage(store, stage =>
            {
                Invoke(stage, "AddProjectionLayer", first);
                Assert.That(first.layers.Count, Is.EqualTo(1)); Assert.That(second.layers.Count, Is.EqualTo(1));
                var original = first.layers[0].projectionSettings.position;
                Assert.That(Invoke(stage, "PlaceProjectionAtSurface", first, first.layers[0], second,
                    new Vector3(.2f, .1f, 0), Vector3.forward, 1), Is.True);
                foreach (var set in new[] { first, second })
                {
                    Assert.That(set.layers[0].projectionSettings.regionSurfaceId, Is.EqualTo(second.persistentId));
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(new Vector3(.2f, .1f, 0)));
                    Assert.That(set.layers[0].projectionSettings.depthFromSurface, Is.True);
                }
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(first.layers[0].projectionSettings.position, Is.EqualTo(original));
                Assert.That(second.layers[0].projectionSettings.position, Is.EqualTo(original));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(first.layers, Is.Empty); Assert.That(second.layers, Is.Empty);
            }, logical, reconstruction);
        }

        [TestCase(false)] [TestCase(true)]
        public void ResizePreservesSurfaceFrameAndOnlyChangesRequestedAxes(bool locked)
        {
            var settings = Definition(); settings.width = 2; settings.height = 1; settings.lockAspect = locked;
            settings.position = Vector3.one; settings.rotation = Quaternion.Euler(31, 42, 53);
            var rotation = settings.rotation;
            typeof(TexturePaintStageWindow).GetMethod("ResizeProjection", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { settings, 4f, 1f, .7f });
            Assert.That(settings.width, Is.EqualTo(4)); Assert.That(settings.height, Is.EqualTo(1));
            Assert.That(settings.depth, Is.EqualTo(.7f)); Assert.That(settings.position, Is.EqualTo(Vector3.one));
            Assert.That(settings.rotation, Is.EqualTo(rotation));
        }

        [TestCase(0f, 1f)] [TestCase(-.3f, 1f)] [TestCase(.1f, 1f)]
        [TestCase(.3f, 1f)] [TestCase(-.5f, 0f)] [TestCase(.5f, 0f)]
        public void SurfaceDepthIncludesBothSidesAndRejectsBeyondItsFullLength(float z, float expectedAlpha)
        {
            var set = Set(Quad(z:z)); var layer = set.AddProjectionLayer(); var settings = Definition();
            settings.depthFromSurface = true; settings.firstSurfaceOnly = false;
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(Pixel(layer).a, Is.EqualTo(expectedAlpha).Within(.001f));
        }

        [Test] public void SurfaceDepthFadeKeepsContactOpaqueAndFadesAtTheBack()
        {
            var front = Set(); var rear = Set(Quad(z:-.3f)); var a = front.AddProjectionLayer(); var b = rear.AddProjectionLayer();
            var settings = Definition(); settings.depthFromSurface = true; settings.depthFade = 1;
            Generate(settings, new[] { front, rear }, new[] { front, rear }, a, b);
            Assert.That(Pixel(a).a, Is.GreaterThan(.99f)); Assert.That(Pixel(b).a, Is.LessThan(.001f));
            settings.firstSurfaceOnly = false;
            Generate(settings, new[] { front, rear }, new[] { front, rear }, a, b);
            Assert.That(Pixel(a).a, Is.GreaterThan(.99f)); Assert.That(Pixel(b).a, Is.InRange(.05f, .4f));
        }

        [TestCase(false)] [TestCase(true)]
        public void RaisedAndRecessedFacetsReceiveContinuousCoverage(bool firstSurfaceOnly)
        {
            var mesh = Own(new Mesh()); var positions = new Vector3[25]; var uvs = new Vector2[25];
            var indices = new List<int>();
            for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
            {
                int i = y * 5 + x; float u = x / 4f, v = y / 4f;
                positions[i] = new Vector3(u - .5f, v - .5f, .005f * Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI) - .002f);
                uvs[i] = new Vector2(u, v);
                if (x < 4 && y < 4) indices.AddRange(new[] { i, i + 1, i + 6, i, i + 6, i + 5 });
            }
            mesh.vertices = positions; mesh.uv = uvs; mesh.triangles = indices.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var set = Set(mesh); var layer = set.AddProjectionLayer(); var settings = Definition(Image(new Color(1, 0, 0, .7f)));
            settings.depth = .04f; settings.depthFromSurface = true; settings.firstSurfaceOnly = firstSurfaceOnly;
            Generate(settings, new[] { set }, new[] { set }, layer);
            for (int y = 8; y <= 56; y += 8) for (int x = 8; x <= 56; x += 8)
                Assert.That(Pixel(layer, x, y).a, Is.EqualTo(.7f).Within(.015f), $"Facet coverage at ({x}, {y})");
        }

        [TestCase(-.03f)] [TestCase(.03f)]
        public void SurfaceDepthFadesAtBothVolumeBoundaries(float z)
        {
            var set = Set(Quad(z: z)); var layer = set.AddProjectionLayer(); var settings = Definition();
            settings.depth = .04f; settings.depthFromSurface = true; settings.depthFade = 1f;
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(Pixel(layer).a, Is.InRange(.05f, .4f));
        }

        [Test] public void BackFacingPolygonDoesNotOccludeAnAcceptedSurface()
        {
            var front = Set(); var backFacing = Set(Quad(z: .01f, reverse: true)); var layer = front.AddProjectionLayer();
            var settings = Definition(); settings.depth = .04f; settings.depthFromSurface = true;
            Generate(settings, new[] { front, backFacing }, new[] { front }, layer);
            Assert.That(Pixel(layer).a, Is.GreaterThan(.99f));
            settings.frontFacesOnly = false;
            Generate(settings, new[] { front, backFacing }, new[] { front }, layer);
            Assert.That(Pixel(layer).a, Is.LessThan(.001f), "When both sides are eligible, the nearer polygon occludes the rear.");
        }

        [Test] public void PolygonGatheringKeepsIntersectingFacesAndRejectsBackfacesAndDistantFaces()
        {
            var mesh = Own(new Mesh());
            mesh.CombineMeshes(new[]
            {
                new CombineInstance { mesh = Quad(-.5f, 0, -.005f), transform = Matrix4x4.identity },
                new CombineInstance { mesh = Quad(0, .5f, .005f), transform = Matrix4x4.identity },
                new CombineInstance { mesh = Quad(.45f, .65f, .002f), transform = Matrix4x4.identity },
                new CombineInstance { mesh = Quad(z: .002f, reverse: true), transform = Matrix4x4.identity },
                new CombineInstance { mesh = Quad(z: .2f), transform = Matrix4x4.identity }
            }, true, true);
            var set = Set(mesh); var settings = Definition(); settings.depth = .04f; settings.depthFromSurface = true;
            var geometry = new TexturePaintProjectionGeometry(new[] { set });
            var gathered = geometry.RestrictedMesh(set, -1, out bool ownsMesh, settings);
            Assert.That(ownsMesh, Is.True); Own(gathered);
            Assert.That(gathered.triangles.Length, Is.EqualTo(18), "Both raised/recessed faces and footprint-crossing triangles must survive.");
            Assert.That(mesh.triangles.Length, Is.EqualTo(30), "Gathering must not change the source mesh.");
        }

        [TestCase(false)] [TestCase(true)]
        public void SceneHandlesResizeWithoutRepositioningAndRingKeepsTheNormal(bool lockNumericAspect)
        {
            var set = Set(); set.AddProjectionLayer();
            set.layers[0].projectionSettings = Definition();
            set.layers[0].projectionSettings.depthFromSurface = true;
            set.layers[0].projectionSettings.lockAspect = lockNumericAspect;
            var view = Own(ScriptableObject.CreateInstance<SceneView>());
            Tool previousTool = Tools.current;
            WithStage(Store(set), stage =>
            {
                Vector2 widthHandle = default, heightHandle = default, heightDestination = default, depthHandle = default, depthDestination = default;
                Vector2 ringHandle = default, ringDestination = default, modelClick = default;
                int draws = 0, capturedControl = 0;
                Vector2 sentPoint = default;
                var inputTrace = new List<string>();
                void Draw(SceneView drawing)
                {
                    if (drawing != view) return;
                    var settings = set.layers[0].projectionSettings;
                    float size = HandleUtility.GetHandleSize(settings.position) * .055f * ProjectionGizmoScale;
                    float radius = Mathf.Sqrt(settings.width * settings.width + settings.height * settings.height) * .5f + size * 2;
                    widthHandle = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * Vector3.right * settings.width * .5f);
                    heightHandle = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * Vector3.up * settings.height * .5f);
                    heightDestination = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * Vector3.up * (settings.height * .5f + .2f));
                    depthHandle = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * Vector3.forward * settings.BackDepth);
                    depthDestination = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * Vector3.forward * (settings.BackDepth - .2f));
                    modelClick = HandleUtility.WorldToGUIPoint(new Vector3(-.3f, -.3f, 0));
                    ringHandle = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * Vector3.up * radius);
                    ringDestination = HandleUtility.WorldToGUIPoint(settings.position + settings.rotation * new Vector3(.5f, .8660254f, 0) * radius);
                    Vector2 eventOffset = sentPoint - Event.current.mousePosition;
                    widthHandle += eventOffset; heightHandle += eventOffset; heightDestination += eventOffset;
                    depthHandle += eventOffset; depthDestination += eventOffset; modelClick += eventOffset;
                    ringHandle += eventOffset; ringDestination += eventOffset;
                    draws++;
                    EventType inputType = Event.current.type;
                    Invoke(stage, "HandleProjectionScene", view, Event.current, true);
                    capturedControl = GUIUtility.hotControl;
                    inputTrace.Add($"{inputType}: {Event.current.mousePosition}, width {widthHandle}, hot {capturedControl}, nearest {HandleUtility.nearestControl}, viewTool {Tools.viewToolActive}");
                }
                void Send(EventType type, Vector2 point, Vector2 delta = default)
                {
                    sentPoint = point;
                    view.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = delta });
                }
                try
                {
                    Tools.current = Tool.None;
                    view.position = new Rect(100, 100, 640, 640); view.ShowUtility(); view.Focus();
                    view.orthographic = true; view.LookAtDirect(Vector3.zero, Quaternion.Euler(20, 160, 0), 1.5f);
                    typeof(TexturePaintStageWindow).GetField("hoverSurface", Private).SetValue(stage, set.surface);
                    typeof(TexturePaintStageWindow).GetField("hoverHit", Private).SetValue(stage, new RaycastHit { point = new Vector3(.3f,.3f,0), normal = Vector3.forward });
                    SceneView.duringSceneGui += Draw;
                    // The first layout initializes the new Scene-view camera and GUI clipping.
                    Send(EventType.Layout, new Vector2(320, 320));
                    Send(EventType.Layout, new Vector2(320, 320));
                    Assert.That(draws, Is.GreaterThan(0));
                    Vector2 start = widthHandle;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start);
                    Assert.That(capturedControl, Is.Not.EqualTo(0), "Width handle must capture the mouse before placement.\n" + string.Join("\n", inputTrace));
                    Send(EventType.MouseDrag, start + Vector2.left * 30, Vector2.left * 30);
                    Send(EventType.MouseUp, start + Vector2.left * 30);
                    Assert.That(set.layers[0].projectionSettings.width, Is.GreaterThan(1.05f));
                    Assert.That(set.layers[0].projectionSettings.height, Is.EqualTo(1f).Within(.00001f));
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(Vector3.zero));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                    Assert.That(set.layers[0].projectionSettings.width, Is.EqualTo(1f).Within(.00001f));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.False, "One drag creates exactly one undo step.");
                    Send(EventType.Layout, new Vector2(320,320)); start = heightHandle; Vector2 end = heightDestination;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start);
                    Assert.That(capturedControl, Is.Not.EqualTo(0), "Y handle captures the mouse.");
                    Send(EventType.MouseDrag, end, end - start); Send(EventType.MouseUp, end);
                    Assert.That(set.layers[0].projectionSettings.height, Is.EqualTo(1.4f).Within(.03f));
                    Assert.That(set.layers[0].projectionSettings.width, Is.EqualTo(1f).Within(.00001f));
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(Vector3.zero));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                    Send(EventType.Layout, new Vector2(320,320)); start = depthHandle; end = depthDestination;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start);
                    Assert.That(capturedControl, Is.Not.EqualTo(0), "Z handle captures the mouse.");
                    Send(EventType.MouseDrag, end, end - start); Send(EventType.MouseUp, end);
                    Assert.That(set.layers[0].projectionSettings.depth, Is.EqualTo(.6f).Within(.03f));
                    Assert.That(set.layers[0].projectionSettings.width, Is.EqualTo(1f).Within(.00001f));
                    Assert.That(set.layers[0].projectionSettings.height, Is.EqualTo(1f).Within(.00001f));
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(Vector3.zero));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                    Send(EventType.Layout, new Vector2(320,320)); start = ringHandle; end = ringDestination;
                    Quaternion before = set.layers[0].projectionSettings.rotation;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start);
                    Assert.That(capturedControl, Is.Not.EqualTo(0), "The rotation ring must capture the mouse.\n" + string.Join("\n", inputTrace));
                    Send(EventType.MouseDrag, end, end - start); Send(EventType.MouseUp, end);
                    Assert.That(Quaternion.Angle(before, set.layers[0].projectionSettings.rotation), Is.GreaterThan(1f));
                    Assert.That(Vector3.Angle(set.layers[0].projectionSettings.rotation * Vector3.forward, Vector3.forward), Is.LessThan(.05f));
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(Vector3.zero));
                    typeof(TexturePaintStageWindow).GetField("hoverHit", Private).SetValue(stage,
                        new RaycastHit { point = new Vector3(.3f, .3f, 0), normal = Vector3.up });
                    Send(EventType.Layout, new Vector2(320,320)); start = modelClick;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start); Send(EventType.MouseUp, start);
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(new Vector3(.3f,.3f,0)));
                    Assert.That(Vector3.Angle(set.layers[0].projectionSettings.rotation * Vector3.forward, Vector3.up), Is.LessThan(.05f));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                    Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(Vector3.zero));
                }
                finally
                {
                    SceneView.duringSceneGui -= Draw;
                    Invoke(stage, "ReleaseProjectionHandle"); Tools.current = previousTool; view.Close();
                }
            });
        }

        [TestCase(TexturePaintProjectionMode.Planar)]
        [TestCase(TexturePaintProjectionMode.Wrapped)]
        [TestCase(TexturePaintProjectionMode.Cylindrical)]
        public void SceneMoveHandleFollowsSurfaceNormalPreservesSizeAndCommitsOneUndo(TexturePaintProjectionMode mode)
        {
            // Two joined faces with different normals, plus an unrelated closer surface that
            // must not intercept this target's drag. No colliders or hover hits are needed.
            var mesh = Own(new Mesh
            {
                vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(0,-.5f,0), new Vector3(.5f,-.5f,.25f),
                    new Vector3(-.5f,.5f,0), new Vector3(0,.5f,0), new Vector3(.5f,.5f,.25f) },
                uv = new[] { Vector2.zero, new Vector2(.5f,0), Vector2.right, Vector2.up, new Vector2(.5f,1), Vector2.one },
                triangles = new[] { 0,1,4,0,4,3,1,2,5,1,5,4 }
            });
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var set = Set(mesh); set.surface.triangleSlotNames = new[] { "Body", "Body", "Body", "Body" };
            set.surface.triangleIslands = new[] { 0,0,0,0 };
            var unrelated = Set(Quad(z: .7f));
            var initial = set.AddProjectionLayer(); var definition = Definition();
            definition.mode = mode; definition.width = .18f; definition.height = .2f; definition.depth = .15f;
            definition.PlaceOnSurface(new Vector3(-.25f,0,0), Vector3.forward, set.persistentId, 0);
            definition.rotation *= Quaternion.AngleAxis(27, Vector3.forward);
            definition.pinned[0] = true;
            Generate(definition, new[] { set }, new[] { set }, initial);
            var view = Own(ScriptableObject.CreateInstance<SceneView>()); Tool previousTool = Tools.current;
            WithStage(Store(set, unrelated), stage =>
            {
                Vector2 sentPoint = default, moveHandle = default, destination = default;
                Vector3 desiredPoint = default; int captured = 0;
                void Draw(SceneView drawing)
                {
                    if (drawing != view) return;
                    Vector2 offset = sentPoint - Event.current.mousePosition;
                    moveHandle = HandleUtility.WorldToGUIPoint(set.layers[0].projectionSettings.position) + offset;
                    destination = HandleUtility.WorldToGUIPoint(desiredPoint) + offset;
                    Invoke(stage, "HandleProjectionScene", view, Event.current, false);
                    captured = GUIUtility.hotControl;
                }
                void Send(EventType type, Vector2 point, Vector2 delta = default)
                {
                    sentPoint = point;
                    view.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = delta });
                }
                void DragTo(Vector3 point)
                {
                    desiredPoint = point; Send(EventType.Layout, sentPoint);
                    Vector2 next = destination; Send(EventType.MouseDrag, next, next - sentPoint);
                }
                void AssertPlacement(Vector3 point, Vector3 normal)
                {
                    var settings = set.layers[0].projectionSettings;
                    Assert.That(Vector3.Distance(settings.position, point), Is.LessThan(.002f));
                    Assert.That(Vector3.Angle(settings.rotation * Vector3.forward, normal), Is.LessThan(.04f));
                    Assert.That(Quaternion.Angle(settings.rotation, Quaternion.FromToRotation(Vector3.forward, normal) * definition.rotation), Is.LessThan(.06f));
                    Assert.That(settings.regionSurfaceId, Is.EqualTo(set.persistentId));
                    Assert.That(settings.width, Is.EqualTo(definition.width));
                    Assert.That(settings.height, Is.EqualTo(definition.height));
                    Assert.That(settings.depth, Is.EqualTo(definition.depth));
                    Assert.That(settings.mode, Is.EqualTo(mode));
                }
                try
                {
                    Tools.current = Tool.None; view.position = new Rect(100,100,640,640); view.ShowUtility(); view.Focus();
                    view.orthographic = true; view.LookAtDirect(Vector3.zero, Quaternion.Euler(0,180,0), 1.2f);
                    SceneView.duringSceneGui += Draw;
                    Send(EventType.Layout, new Vector2(320,320)); Send(EventType.Layout, new Vector2(320,320));
                    Vector2 start = moveHandle;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start);
                    Assert.That(captured, Is.Not.Zero, "Move must capture the mouse without click placement.");
                    Assert.That(set.layers[0], Is.SameAs(initial), "Grabbing the handle must not snap or add history.");
                    DragTo(new Vector3(-.1f,0,0)); AssertPlacement(new Vector3(-.1f,0,0), Vector3.forward);
                    Vector3 normal = new Vector3(-.5f,0,1).normalized;
                    DragTo(new Vector3(.15f,0,.075f)); AssertPlacement(new Vector3(.15f,0,.075f), normal);
                    var cachedGeometry = typeof(TexturePaintStageWindow).GetField("projectionMoveGeometry", Private).GetValue(stage);
                    string beforeMiss = JsonUtility.ToJson(set.layers[0].projectionSettings);
                    DragTo(new Vector3(.75f,0,0));
                    Assert.That(JsonUtility.ToJson(set.layers[0].projectionSettings), Is.EqualTo(beforeMiss), "Missing the model must preserve the last valid placement.");
                    Assert.That(captured, Is.Not.Zero, "A ray miss must not end the drag.");
                    DragTo(new Vector3(.3f,0,.15f)); AssertPlacement(new Vector3(.3f,0,.15f), normal);
                    Assert.That(typeof(TexturePaintStageWindow).GetField("projectionMoveGeometry", Private).GetValue(stage), Is.SameAs(cachedGeometry));
                    Assert.That(set.layers[0].projectionSettings.pinned, Is.All.False, "Dragging repositions/refits just like clicking.");
                    var moved = set.layers[0]; Send(EventType.MouseUp, sentPoint);
                    Assert.That(captured, Is.Zero);
                    Assert.That(typeof(TexturePaintStageWindow).GetField("projectionMoveGeometry", Private).GetValue(stage), Is.Null);
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Assert.That(set.layers[0], Is.SameAs(initial));
                    Assert.That(initial.projectionSettings.pinned[0], Is.True);
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.False, "The complete drag must be one undo action.");
                    Assert.That(Invoke(stage, "RedoLightweight"), Is.True); Assert.That(set.layers[0], Is.SameAs(moved));
                    // A cancelled second gesture restores the last committed placement and its pixels.
                    Send(EventType.Layout, sentPoint); start = moveHandle;
                    Send(EventType.MouseMove, start); Send(EventType.Layout, start); Send(EventType.MouseDown, start);
                    DragTo(new Vector3(-.1f,0,0)); AssertPlacement(new Vector3(-.1f,0,0), Vector3.forward);
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                    Assert.That(captured, Is.Zero); Assert.That(set.layers[0], Is.SameAs(moved));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Assert.That(set.layers[0], Is.SameAs(initial));
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
                }
                finally
                {
                    SceneView.duringSceneGui -= Draw;
                    Invoke(stage, "ReleaseProjectionHandle"); Tools.current = previousTool; view.Close();
                }
            });
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void RotationRingFollowsTracedCircleAcrossTurnsAndDirectionChanges(bool orthographic, bool fromBack)
        {
            var set = Set(); var initial = set.AddProjectionLayer();
            initial.projectionSettings = Definition();
            initial.projectionSettings.position = new Vector3(.1f, -.1f, 0);
            initial.projectionSettings.rotation = Quaternion.Euler(19, 27, 11);
            Vector3 center = initial.projectionSettings.position;
            Quaternion initialRotation = initial.projectionSettings.rotation;
            Vector3 normal = initialRotation * Vector3.forward;
            var view = Own(ScriptableObject.CreateInstance<SceneView>());
            Tool previousTool = Tools.current;
            WithStage(Store(set), stage =>
            {
                float pointerAngle = 23, pointerRadius = 1;
                Vector2 sentPoint = default, ringPoint = default, centerPoint = default;
                int captured = 0;
                void Draw(SceneView drawing)
                {
                    if (drawing != view) return;
                    var settings = set.layers[0].projectionSettings;
                    float size = HandleUtility.GetHandleSize(center) * .055f * ProjectionGizmoScale;
                    float radius = Mathf.Sqrt(settings.width * settings.width + settings.height * settings.height) * .5f + size * 2;
                    Vector3 direction = initialRotation * Quaternion.AngleAxis(pointerAngle, Vector3.forward) * Vector3.right;
                    Vector2 eventOffset = sentPoint - Event.current.mousePosition;
                    ringPoint = HandleUtility.WorldToGUIPoint(center + direction * radius * pointerRadius) + eventOffset;
                    centerPoint = HandleUtility.WorldToGUIPoint(center) + eventOffset;
                    Invoke(stage, "HandleProjectionScene", view, Event.current, true);
                    captured = GUIUtility.hotControl;
                }
                void Send(EventType type, Vector2 point, Vector2 delta = default)
                {
                    sentPoint = point;
                    view.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = delta });
                }
                void AssertAngle(float degrees)
                {
                    var settings = set.layers[0].projectionSettings;
                    Assert.That(Quaternion.Angle(settings.rotation,
                        initialRotation * Quaternion.AngleAxis(degrees, Vector3.forward)), Is.LessThan(.08f),
                        "The grabbed ring point must follow the cursor at " + degrees + " degrees.");
                    Assert.That(Vector3.Angle(settings.rotation * Vector3.forward, normal), Is.LessThan(.04f));
                    Assert.That(settings.position, Is.EqualTo(center));
                    Assert.That(settings.width, Is.EqualTo(1)); Assert.That(settings.height, Is.EqualTo(1));
                }
                try
                {
                    Tools.current = Tool.None;
                    view.position = new Rect(100, 100, 640, 640); view.ShowUtility(); view.Focus();
                    view.orthographic = orthographic;
                    view.LookAtDirect(center, initialRotation * Quaternion.Euler(22, fromBack ? 35 : 145, 0), 1.5f);
                    typeof(TexturePaintStageWindow).GetField("hoverSurface", Private).SetValue(stage, set.surface);
                    typeof(TexturePaintStageWindow).GetField("hoverHit", Private).SetValue(stage,
                        new RaycastHit { point = new Vector3(2, 3, 4), normal = Vector3.up });
                    SceneView.duringSceneGui += Draw;
                    Send(EventType.Layout, new Vector2(320, 320)); Send(EventType.Layout, new Vector2(320, 320));
                    Vector2 cursor = ringPoint;
                    Send(EventType.MouseMove, cursor); Send(EventType.Layout, cursor); Send(EventType.MouseDown, cursor);
                    Assert.That(captured, Is.Not.Zero); AssertAngle(0); // No jump when grabbing away from an axis.
                    for (int angle = 30; angle <= 420; angle += 30)
                    {
                        pointerAngle = 23 + angle;
                        Send(EventType.Layout, cursor); Vector2 next = ringPoint;
                        Send(EventType.MouseDrag, next, next - cursor); cursor = next;
                        AssertAngle(angle);
                    }
                    // Small circular movements stay precise after a complete revolution.
                    for (int step = 1; step <= 24; step++)
                    {
                        float angle = 420 + step * .25f; pointerAngle = 23 + angle;
                        Send(EventType.Layout, cursor); Vector2 next = ringPoint;
                        Send(EventType.MouseDrag, next, next - cursor); cursor = next; AssertAngle(angle);
                    }
                    // Repeated outward/inward movement must not accumulate angle from rounding noise.
                    for (int step = 0; step < 20; step++)
                    {
                        pointerRadius = .8f + step * .025f;
                        Send(EventType.Layout, cursor); Vector2 radial = ringPoint;
                        Send(EventType.MouseDrag, radial, radial - cursor); cursor = radial; AssertAngle(426);
                    }
                    pointerRadius = 1;
                    for (int angle = 390; angle >= -60; angle -= 30)
                    {
                        pointerAngle = 23 + angle;
                        Send(EventType.Layout, cursor); Vector2 next = ringPoint;
                        Send(EventType.MouseDrag, next, next - cursor); cursor = next;
                        AssertAngle(angle);
                    }
                    // The center has no defined angle; crossing into its dead zone must not spin.
                    Send(EventType.MouseDrag, centerPoint, centerPoint - cursor); AssertAngle(-60);
                    Send(EventType.MouseDrag, cursor, cursor - centerPoint); AssertAngle(-60);
                    Send(EventType.MouseUp, cursor); Assert.That(captured, Is.Zero);
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True); AssertAngle(0);
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.False, "A complete circular drag is one undo step.");
                    Assert.That(Invoke(stage, "RedoLightweight"), Is.True); AssertAngle(-60);
                    // A second grab must preserve the current spin, and Escape cancels all its movement.
                    Send(EventType.Layout, cursor); cursor = ringPoint;
                    Send(EventType.MouseMove, cursor); Send(EventType.Layout, cursor); Send(EventType.MouseDown, cursor);
                    Assert.That(captured, Is.Not.Zero); AssertAngle(-60);
                    pointerAngle += 45;
                    Send(EventType.Layout, cursor); Vector2 end = ringPoint;
                    Send(EventType.MouseDrag, end, end - cursor); AssertAngle(-15);
                    view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                    Assert.That(captured, Is.Zero); AssertAngle(-60);
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True); AssertAngle(0);
                }
                finally
                {
                    SceneView.duringSceneGui -= Draw;
                    Invoke(stage, "ReleaseProjectionHandle"); Tools.current = previousTool; view.Close();
                }
            });
        }

        [Test] public void LayerDiscriminatorsRemainBackwardCompatibleAndCloneIsIndependent()
        {
            Assert.That((int)TexturePaintLayerKind.Plugin, Is.EqualTo(4));
            Assert.That((int)TexturePaintLayerKind.Projection, Is.EqualTo(5));
            var settings = Definition(); settings.mode = TexturePaintProjectionMode.Wrapped;
            settings.position = new Vector3(1,2,3); settings.rotation = Quaternion.Euler(20,35,42);
            settings.points[4].z = 0.1f;
            var clone = settings.Clone(); clone.points[4].z = 0.5f; clone.falloff.AddKey(0.5f,0.1f);
            Assert.That(settings.points[4].z, Is.EqualTo(0.1f)); Assert.That(settings.falloff.length, Is.EqualTo(2));
            for(int y=0;y<3;y++) for(int x=0;x<3;x++)
                Assert.That(Vector3.Distance(settings.Evaluate(x*0.5f,y*0.5f),settings.PointToWorld(settings.points[y*3+x])), Is.LessThan(1e-5f));
        }
        [Test] public void MovingProjectionReplacesOutputAndMissingSourceClearsIt()
        {
            var set = Set(); var layer = set.AddProjectionLayer(); var settings = Definition(); settings.width = settings.height = 0.3f;
            Generate(settings,new[]{set},new[]{set},layer); Assert.That(Pixel(layer).a,Is.GreaterThan(0.99f));
            settings.position = new Vector3(0.3f,0,0);
            Generate(settings,new[]{set},new[]{set},layer); Assert.That(Pixel(layer).a,Is.LessThan(0.001f)); Assert.That(Pixel(layer,51,32).a,Is.GreaterThan(0.99f));
            settings.texture = null; Generate(settings,new[]{set},new[]{set},layer); Assert.That(Pixel(layer,51,32).a,Is.LessThan(0.001f));
        }
        [TestCase(TexturePaintProjectionFade.None)]
        [TestCase(TexturePaintProjectionFade.Ellipse)]
        [TestCase(TexturePaintProjectionFade.Rectangle)]
        [TestCase(TexturePaintProjectionFade.AlphaOutline)]
        public void FadeKeepsSourceAlphaSingleAndSoftensEdges(TexturePaintProjectionFade fade)
        {
            var set = Set(); var layer = set.AddProjectionLayer(); var settings = Definition(Image(new Color(1,0,0,0.5f)));
            settings.fade = fade; settings.edgeWidth = 0.25f;
            Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(Pixel(layer).a,Is.EqualTo(0.5f).Within(0.015f));
            float edge = Pixel(layer,4,32).a;
            if(fade == TexturePaintProjectionFade.None) Assert.That(edge,Is.EqualTo(0.5f).Within(0.015f));
            else Assert.That(edge,Is.LessThan(0.25f));
            if(fade == TexturePaintProjectionFade.Ellipse) Assert.That(Pixel(layer,4,4).a,Is.LessThan(0.001f));
        }
        [Test] public void AlphaOutlineFadesAroundAnInteriorHole()
        {
            Texture2D image = Image(Color.white);
            for(int y=6;y<10;y++) for(int x=6;x<10;x++) image.SetPixel(x,y,Color.clear);
            image.Apply(); var set=Set();var layer=set.AddProjectionLayer();var settings=Definition(image);
            settings.fade=TexturePaintProjectionFade.AlphaOutline;settings.edgeWidth=0.15f;
            Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(Pixel(layer).a,Is.LessThan(0.001f));
            Assert.That(Pixel(layer,19,32).a,Is.LessThan(Pixel(layer,13,32).a));
        }
        [TestCase("bounds", false)] [TestCase("bounds", true)]
        [TestCase("depth", false)] [TestCase("depth", true)]
        [TestCase("footprint", false)] [TestCase("footprint", true)]
        [TestCase("backface", false)] [TestCase("backface", true)]
        [TestCase("visibility", false)] [TestCase("visibility", true)]
        [TestCase("angle", false)] [TestCase("angle", true)]
        [TestCase("alpha", false)] [TestCase("alpha", true)]
        [TestCase("fade", false)] [TestCase("fade", true)]
        public void RejectedFacesSharingUvsDoNotEraseProjection(string rejection, bool rejectedFirst)
        {
            var front = Quad(); var other = Quad(reverse: rejection == "backface");
            Vector3 offset = rejection == "bounds" ? Vector3.right * 3f :
                rejection == "depth" ? Vector3.forward * .41f :
                rejection == "footprint" ? Vector3.right * .6f :
                rejection == "visibility" ? Vector3.back * .1f :
                rejection == "alpha" ? Vector3.right * .3f :
                rejection == "fade" ? new Vector3(.46f, .46f, 0) : Vector3.zero;
            var positions = other.vertices;
            for (int i = 0; i < positions.Length; i++) positions[i] += offset;
            other.vertices = positions;
            if (rejection == "backface" || rejection == "angle")
            {
                var normals = other.normals;
                Array.Fill(normals, rejection == "backface" ? Vector3.back : new Vector3(1, 0, .01f).normalized);
                other.normals = normals;
            }
            var combined = Own(new Mesh());
            combined.CombineMeshes(new[]
            {
                new CombineInstance { mesh = rejectedFirst ? other : front, transform = Matrix4x4.identity },
                new CombineInstance { mesh = rejectedFirst ? front : other, transform = Matrix4x4.identity }
            }, true, true);
            var set = Set(combined); var layer = set.AddProjectionLayer();
            var source = Image(new Color(1, 0, 0, .5f));
            if (rejection == "alpha")
            {
                for (int y = 0; y < 16; y++) for (int x = 10; x < 16; x++) source.SetPixel(x, y, Color.clear);
                source.Apply();
            }
            var settings = Definition(source); settings.depthFromSurface = true;
            if (rejection == "fade") { settings.fade = TexturePaintProjectionFade.Ellipse; settings.edgeWidth = .1f; }
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(Pixel(layer).r, Is.GreaterThan(.99f), "A rejected face must leave the projected face's color intact.");
            Assert.That(Pixel(layer).a, Is.EqualTo(.5f).Within(.015f),
                "Shared UVs must preserve source alpha regardless of triangle order.");
        }

        [Test] public void FirstSurfaceRejectsHiddenGeometryAndProjectThroughIncludesIt()
        {
            var front=Set();var rear=Set(Quad(z:-0.1f)); var a=front.AddProjectionLayer();var b=rear.AddProjectionLayer();var settings=Definition();
            Generate(settings,new[]{front,rear},new[]{front,rear},a,b);
            Assert.That(Pixel(a).a,Is.GreaterThan(0.99f));Assert.That(Pixel(b).a,Is.LessThan(0.001f));
            settings.firstSurfaceOnly=false;Generate(settings,new[]{front,rear},new[]{front,rear},a,b);
            Assert.That(Pixel(b).a,Is.GreaterThan(0.99f));
            settings.depth=0.1f;Generate(settings,new[]{front,rear},new[]{front,rear},a,b);
            Assert.That(Pixel(b).a,Is.LessThan(0.001f));
        }
        [Test] public void BackfaceAngleAndDepthFadesAreIndependent()
        {
            var set=Set(Quad(reverse:true));var layer=set.AddProjectionLayer();var settings=Definition();
            Generate(settings,new[]{set},new[]{set},layer);Assert.That(Pixel(layer).a,Is.LessThan(0.001f));
            settings.frontFacesOnly=false;Generate(settings,new[]{set},new[]{set},layer);Assert.That(Pixel(layer).a,Is.GreaterThan(0.99f));
            settings.position=new Vector3(0,0,0.15f);settings.depthFade=1;
            Generate(settings,new[]{set},new[]{set},layer);Assert.That(Pixel(layer).a,Is.InRange(0.05f,0.4f));
            settings.position=Vector3.zero;settings.rotation=Quaternion.Euler(0,80,0);settings.depthFade=0;
            Generate(settings,new[]{set},new[]{set},layer);Assert.That(Pixel(layer).a,Is.InRange(0.1f,0.4f));
        }
        [Test] public void ProjectionCrossesUdimBoundaryButRejectsDisconnectedSurface()
        {
            var left=Set(Quad(-0.5f,0));var right=Set(Quad(0,0.5f));var rear=Set(Quad(z:-0.1f));
            var a=left.AddProjectionLayer();var b=right.AddProjectionLayer();var c=rear.AddProjectionLayer();var settings=Definition();
            settings.regionSurfaceId=left.persistentId;settings.regionTriangle=0;settings.firstSurfaceOnly=false;
            Generate(settings,new[]{left,right,rear},new[]{left,right,rear},a,b,c);
            Assert.That(Pixel(a,60,32).a,Is.GreaterThan(0.99f));Assert.That(Pixel(b,3,32).a,Is.GreaterThan(0.99f));
            Assert.That(Pixel(c).a,Is.LessThan(0.001f));
        }
        [TestCase(false)] [TestCase(true)] public void WrappedPatchFitsCurvedSurfaceAndRemainsFixedWhenModelMoves(bool fromSurface)
        {
            var mesh=Own(new Mesh());var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            const int n=16;
            for(int y=0;y<=n;y++) for(int x=0;x<=n;x++) { float u=(float)x/n,v=(float)y/n;vertices.Add(new Vector3(u-0.5f,v-0.5f,-0.8f*(u-0.5f)*(u-0.5f)));uv.Add(new Vector2(u,v)); }
            for(int y=0;y<n;y++) for(int x=0;x<n;x++) { int i=y*(n+1)+x;triangles.AddRange(new[]{i,i+1,i+n+2,i,i+n+2,i+n+1}); }
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var set=Set(mesh);var layer=set.AddProjectionLayer();var settings=Definition();settings.depth=0.5f;settings.depthFromSurface=fromSurface;
            Assert.That(new TexturePaintProjectionGeometry(new[]{set}).Fit(settings),Is.EqualTo(9));
            Assert.That(settings.points[0].z,Is.EqualTo(-0.2f).Within(0.001f));
            settings.depth=0.05f;Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(Pixel(layer,7,32).a,Is.GreaterThan(0.85f));Assert.That(Pixel(layer).a,Is.GreaterThan(0.85f));
            for (int x = 7; x <= 56; x += 4)
                Assert.That(Pixel(layer,x,32).a,Is.GreaterThan(.85f), "Wrapped coverage at x=" + x);
            var original=settings.Evaluate(0.2f,0.5f);
            for(int i=0;i<vertices.Count;i++) vertices[i]+=Vector3.forward;
            mesh.SetVertices(vertices);mesh.RecalculateBounds();Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(Pixel(layer).a,Is.LessThan(0.001f));Assert.That(settings.Evaluate(0.2f,0.5f),Is.EqualTo(original));
        }
        [Test] public void RotatedNormalProjectionReorientsIntoDestinationUvBasis()
        {
            var set=Set();AddChannel(set,TexturePaintChannel.Normal,1);var layer=set.AddProjectionLayer();
            var settings=Definition(Image(new Color(0.8f,0.5f,0.9f,1)));settings.channel=TexturePaintChannel.Normal;
            settings.rotation=Quaternion.Euler(0,0,90);Generate(settings,new[]{set},new[]{set},layer);
            Color normal=Pixel(layer,32,32,TexturePaintChannel.Normal);
            Assert.That(normal.r,Is.EqualTo(0.5f).Within(0.025f));Assert.That(normal.g,Is.GreaterThan(0.77f));Assert.That(normal.b,Is.GreaterThan(0.88f));
            settings.flipX=true;Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(Pixel(layer,32,32,TexturePaintChannel.Normal).g,Is.LessThan(0.23f));
        }
        [TestCase(TexturePaintProjectionMode.Planar, true, false)]
        [TestCase(TexturePaintProjectionMode.Planar, false, true)]
        [TestCase(TexturePaintProjectionMode.Planar, true, true)]
        [TestCase(TexturePaintProjectionMode.Wrapped, true, false)]
        [TestCase(TexturePaintProjectionMode.Wrapped, false, true)]
        [TestCase(TexturePaintProjectionMode.Wrapped, true, true)]
        [TestCase(TexturePaintProjectionMode.Cylindrical, true, false)]
        [TestCase(TexturePaintProjectionMode.Cylindrical, false, true)]
        [TestCase(TexturePaintProjectionMode.Cylindrical, true, true)]
        public void TextureMirroringKeepsEveryChannelAndSilhouetteAligned(TexturePaintProjectionMode mode, bool flipX, bool flipY)
        {
            var settings = Definition(); settings.mode = mode;
            Mesh mesh = null;
            if (mode == TexturePaintProjectionMode.Cylindrical)
            {
                settings.width = 2 * Mathf.PI; settings.depth = .15f; settings.depthFromSurface = true;
                const int segments = 64;
                var vertices = new Vector3[(segments + 1) * 2]; var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
                for (int x = 0; x <= segments; x++) for (int y = 0; y < 2; y++)
                { int i = x * 2 + y; uv[i] = new Vector2((float)x / segments, y); vertices[i] = settings.Evaluate(uv[i].x, uv[i].y); }
                for (int x = 0; x < segments; x++)
                { int i = x * 2, t = x * 6; triangles[t] = i; triangles[t+1] = i+2; triangles[t+2] = i+3; triangles[t+3] = i; triangles[t+4] = i+3; triangles[t+5] = i+1; }
                mesh = Own(new Mesh { vertices = vertices, uv = uv, triangles = triangles }); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            }
            var set = Set(mesh); var layer = set.AddProjectionLayer();
            var originalImages = new Dictionary<Texture2D, Color[]>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (channel != TexturePaintChannel.Albedo) AddChannel(set, channel, (int)channel);
                var image = Image(Color.white); image.filterMode = FilterMode.Point; var pixels = new Color[256];
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                {
                    int quadrant = (x >= 8 ? 1 : 0) + (y >= 8 ? 2 : 0);
                    if (channel == TexturePaintChannel.Normal)
                    {
                        float nx = .1f + quadrant * .1f, ny = .35f - quadrant * .1f;
                        pixels[y * 16 + x] = new Color(nx * .5f + .5f, ny * .5f + .5f,
                            Mathf.Sqrt(1 - nx * nx - ny * ny) * .5f + .5f, 1);
                    }
                    else pixels[y * 16 + x] = new Color(.1f + quadrant * .18f, .15f + quadrant * .12f,
                        .9f - quadrant * .2f, channel == TexturePaintChannel.Albedo ? .3f + quadrant * .2f : 1);
                }
                image.SetPixels(pixels); image.Apply(); originalImages.Add(image, image.GetPixels());
                var source = settings.GetChannelSource(channel, true);
                // Exercise both complete textures and Sprite extraction in the same material.
                if ((int)channel % 2 == 0) { source.texture = null; source.sprite = Own(Sprite.Create(image, new Rect(0,0,16,16), Vector2.one * .5f)); }
                else source.texture = image;
            }
            Generate(settings, new[] { set }, new[] { set }, layer);
            var before = new Dictionary<TexturePaintChannel, Color[]>();
            foreach (var channel in layer.channels.Keys)
            {
                var samples = new Color[4];
                for (int q = 0; q < 4; q++) samples[q] = Pixel(layer, (q & 1) == 0 ? 16 : 47, (q & 2) == 0 ? 16 : 47, channel);
                before.Add(channel, samples);
            }
            settings.flipX = flipX; settings.flipY = flipY;
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(layer.channels.Count, Is.EqualTo(Enum.GetValues(typeof(TexturePaintChannel)).Length));
            foreach (var pair in before) for (int q = 0; q < 4; q++)
            {
                int mirrored = q ^ (flipX ? 1 : 0) ^ (flipY ? 2 : 0);
                Color expected = pair.Value[mirrored];
                if (pair.Key == TexturePaintChannel.Normal)
                { if (flipX) expected.r = 1 - expected.r; if (flipY) expected.g = 1 - expected.g; }
                Color actual = Pixel(layer, (q & 1) == 0 ? 16 : 47, (q & 2) == 0 ? 16 : 47, pair.Key);
                string label = mode + " " + pair.Key + " quadrant " + q;
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(.035f), label + " R");
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(.035f), label + " G");
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(.035f), label + " B");
                Assert.That(actual.a, Is.EqualTo(before[TexturePaintChannel.Albedo][mirrored].a).Within(.015f), label + " silhouette");
                Assert.That(actual.a, Is.GreaterThan(.25f), label + " must contain projected pixels");
            }
            foreach (var pair in originalImages) CollectionAssert.AreEqual(pair.Value, pair.Key.GetPixels(), "Mirroring must not edit source images.");
        }

        [TestCase(TexturePaintProjectionMode.Planar)] [TestCase(TexturePaintProjectionMode.Wrapped)]
        public void ChannelImagesSharePlacementAlbedoAlphaAndCorrectNormalConvention(TexturePaintProjectionMode mode)
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Normal, 1); AddChannel(set, TexturePaintChannel.Roughness, 2);
            var layer = set.AddProjectionLayer();
            var settings = Definition(Image(new Color(1, 0, 0, .4f)));
            settings.mode = mode; settings.rotation = Quaternion.Euler(0, 0, 90);
            var normal = settings.GetChannelSource(TexturePaintChannel.Normal, true);
            normal.texture = Image(new Color(.5f, .8f, .9f, .1f)); normal.normalConvention = TexturePaintNormalConvention.DirectX;
            settings.GetChannelSource(TexturePaintChannel.Roughness, true).texture = Image(new Color(.3f, .3f, .3f, .8f));
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(layer.channels.Count, Is.EqualTo(3));
            Assert.That(Pixel(layer).r, Is.GreaterThan(.99f));
            Assert.That(Pixel(layer).a, Is.EqualTo(.4f).Within(.015f));
            Color projectedNormal = Pixel(layer, channel: TexturePaintChannel.Normal);
            Assert.That(projectedNormal.r, Is.EqualTo(.8f).Within(.025f));
            Assert.That(projectedNormal.g, Is.EqualTo(.5f).Within(.025f));
            Assert.That(projectedNormal.a, Is.EqualTo(.4f).Within(.015f));
            Assert.That(Pixel(layer, channel: TexturePaintChannel.Roughness).r, Is.EqualTo(.3f).Within(.015f));
            Assert.That(Pixel(layer, channel: TexturePaintChannel.Roughness).a, Is.EqualTo(.4f).Within(.015f));
        }

        [Test] public void ChannelSpritesUseIndependentAtlasRectsAndAlbedoSilhouette()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Normal, 1); var layer = set.AddProjectionLayer();
            var atlas = Image(Color.green);
            for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++) atlas.SetPixel(x, y, new Color(1, 0, 0, .5f));
            atlas.Apply();
            var normalAtlas = Image(new Color(.8f, .5f, .9f, 1));
            for (int y = 0; y < 16; y++) for (int x = 8; x < 16; x++) normalAtlas.SetPixel(x, y, new Color(.2f, .5f, .9f, 1));
            normalAtlas.Apply();
            var settings = Definition();
            var albedo = settings.GetChannelSource(TexturePaintChannel.Albedo, true);
            albedo.texture = null; albedo.sprite = Own(Sprite.Create(atlas, new Rect(0, 0, 8, 16), Vector2.one * .5f));
            var normal = settings.GetChannelSource(TexturePaintChannel.Normal, true);
            normal.sprite = Own(Sprite.Create(normalAtlas, new Rect(8, 0, 8, 16), Vector2.one * .5f));
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(Pixel(layer).r, Is.GreaterThan(.99f)); Assert.That(Pixel(layer).g, Is.LessThan(.01f));
            Assert.That(Pixel(layer, channel: TexturePaintChannel.Normal).r, Is.EqualTo(.2f).Within(.025f));
            Assert.That(Pixel(layer, channel: TexturePaintChannel.Normal).a, Is.EqualTo(.5f).Within(.015f));
            Assert.That(Pixel(layer).a, Is.EqualTo(.5f).Within(.015f));
        }

        [Test] public void ChannelSourceEditsUndoWithoutReplacingOtherMapsOrPlacement()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Normal, 1); var layer = set.AddProjectionLayer();
            var settings = Definition(); Generate(settings, new[] { set }, new[] { set }, layer);
            WithStage(Store(set), stage =>
            {
                var edit = layer.projectionSettings.Clone();
                edit.GetChannelSource(TexturePaintChannel.Normal, true).texture = Image(new Color(.5f, .8f, .9f, 1));
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, layer, edit, false), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).g, Is.GreaterThan(.77f));
                Assert.That(set.layers[0].projectionSettings.GetChannelSource(TexturePaintChannel.Albedo).texture, Is.SameAs(settings.texture));
                Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(settings.position));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Normal), Is.False);
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                edit = set.layers[0].projectionSettings.Clone();
                edit.GetChannelSource(TexturePaintChannel.Normal).texture = null;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, set.layers[0], edit, false), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).a, Is.LessThan(.001f));
                Assert.That(Pixel(set.layers[0]).r, Is.GreaterThan(.99f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).g, Is.GreaterThan(.77f));
            });
        }

        [Test] public void SharedChannelControlsAddEditRemoveAndUndoProjectionMaps()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Normal, 1);
            var layer = set.AddProjectionLayer(); var definition = Definition();
            Generate(definition, new[] { set }, new[] { set }, layer);
            WithStage(Store(set), stage =>
            {
                Assert.That(Invoke(stage, "AddLayerChannelWithHistory", set, set.layers[0], TexturePaintChannel.Normal), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).a, Is.LessThan(.001f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Normal), Is.False);
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                var source = new TexturePaintChannelSourceSettings { source = TexturePaintBrushSource.Texture,
                    sourceTexture = Image(new Color(.5f, .8f, .9f, 1)), normalConvention = TexturePaintNormalConvention.DirectX };
                Assert.That(Invoke(stage, "ChangeLayerChannelSources", set, set.layers[0],
                    new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings> { [TexturePaintChannel.Normal] = source }), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).g, Is.EqualTo(.2f).Within(.025f));
                Assert.That(set.layers[0].GetChannelSettings(TexturePaintChannel.Normal).sourceSettings.sourceTexture, Is.SameAs(source.sourceTexture));
                Assert.That(set.layers[0].projectionSettings.position, Is.EqualTo(definition.position));
                Assert.That(Pixel(set.layers[0]).r, Is.GreaterThan(.99f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).a, Is.LessThan(.001f));
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                Assert.That(Invoke(stage, "RemoveLayerChannelWithHistory", set, set.layers[0], TexturePaintChannel.Normal), Is.True);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Normal), Is.False);
                Assert.That(set.layers[0].channelSettings.ContainsKey(TexturePaintChannel.Normal), Is.False);
                Assert.That(set.layers[0].projectionSettings.GetChannelSource(TexturePaintChannel.Normal), Is.Null);
                var moved = set.layers[0].projectionSettings.Clone(); moved.position.x += .1f;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, set.layers[0], moved, false), Is.True);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Normal), Is.False, "Regeneration must not resurrect a removed map.");
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Normal).g, Is.EqualTo(.2f).Within(.025f));
            });
        }

        [Test] public void ChannelOptionsPersistThroughSourceEditsAndColorMapsShareCoverage()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Roughness, 1);
            var layer = set.AddProjectionLayer(); var settings = Definition(Image(new Color(1, 0, 0, .4f)));
            settings.GetChannelSource(TexturePaintChannel.Roughness, true);
            Generate(settings, new[] { set }, new[] { set }, layer);
            WithStage(Store(set), stage =>
            {
                Invoke(stage, "ChangeLayerChannel", set, layer, TexturePaintChannel.Roughness,
                    false, false, 1f, .35f, TexturePaintBlendMode.Multiply);
                var assignments = new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings>
                {
                    [TexturePaintChannel.Albedo] = new TexturePaintChannelSourceSettings
                    { source = TexturePaintBrushSource.Texture, sourceTexture = settings.texture, invert = true },
                    [TexturePaintChannel.Roughness] = new TexturePaintChannelSourceSettings
                    { source = TexturePaintBrushSource.Color, color = new Color(.3f, .3f, .3f, 1) }
                };
                Assert.That(Invoke(stage, "ChangeLayerChannelSources", set, set.layers[0], assignments), Is.True);
                var options = set.layers[0].GetChannelSettings(TexturePaintChannel.Roughness);
                Assert.That(options.enabled, Is.False); Assert.That(options.opacity, Is.EqualTo(.35f));
                Assert.That(options.blendMode, Is.EqualTo(TexturePaintBlendMode.Multiply));
                Assert.That(Pixel(set.layers[0]).r, Is.LessThan(.01f)); Assert.That(Pixel(set.layers[0]).g, Is.GreaterThan(.99f));
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Roughness).r, Is.EqualTo(.3f).Within(.015f));
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Roughness).a, Is.EqualTo(.4f).Within(.015f));
                assignments[TexturePaintChannel.Albedo] = new TexturePaintChannelSourceSettings
                    { source = TexturePaintBrushSource.Color, color = new Color(.7f, .2f, .1f, .25f) };
                Assert.That(Invoke(stage, "ChangeLayerChannelSources", set, set.layers[0], assignments), Is.True);
                Assert.That(Pixel(set.layers[0]).r, Is.EqualTo(.7f).Within(.015f));
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Roughness).a, Is.EqualTo(.25f).Within(.015f));
                var saved = JsonUtility.FromJson<TexturePaintProjectionSettings>(JsonUtility.ToJson(set.layers[0].projectionSettings));
                Assert.That(saved.GetChannelSource(TexturePaintChannel.Albedo).color.a, Is.EqualTo(.25f));
                Assert.That(saved.GetChannelSource(TexturePaintChannel.Roughness).source, Is.EqualTo(TexturePaintBrushSource.Color));
            });
        }

        [Test] public void LegacyOverlayCanEditAndRemoveIndividualChannelsWithoutChangingOtherMaps()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Roughness, 1);
            var layer = set.AddProjectionLayer(); var overlay = Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
            overlay.textureList = new Texture[] { Image(new Color(1, 0, 0, .6f)), Image(new Color(.3f, .3f, .3f, 1)) };
            var definition = Definition(); definition.source = TexturePaintBrushSource.Overlay; definition.overlay = overlay;
            Generate(definition, new[] { set }, new[] { set }, layer);
            WithStage(Store(set), stage =>
            {
                var source = layer.projectionSettings.GetChannelSourceSettings(TexturePaintChannel.Roughness);
                Assert.That(source.sourceOverlay, Is.SameAs(overlay)); source.invert = true;
                Assert.That(Invoke(stage, "ChangeLayerChannelSources", set, layer,
                    new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings> { [TexturePaintChannel.Roughness] = source }), Is.True);
                Assert.That(Pixel(set.layers[0]).r, Is.GreaterThan(.99f));
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Roughness).r, Is.EqualTo(.7f).Within(.015f));
                Assert.That(Pixel(set.layers[0], channel: TexturePaintChannel.Roughness).a, Is.EqualTo(.6f).Within(.015f));
                var restored = JsonUtility.FromJson<TexturePaintProjectionSettings>(JsonUtility.ToJson(set.layers[0].projectionSettings));
                Assert.That(restored.GetChannelSource(TexturePaintChannel.Roughness).overlay, Is.SameAs(overlay));
                Assert.That(restored.GetChannelSource(TexturePaintChannel.Roughness).invert, Is.True);
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0].projectionSettings.source, Is.EqualTo(TexturePaintBrushSource.Overlay));
                Assert.That(Invoke(stage, "RemoveLayerChannelWithHistory", set, set.layers[0], TexturePaintChannel.Roughness), Is.True);
                Assert.That(set.layers[0].channels.Count, Is.EqualTo(1));
                Generate(set.layers[0].projectionSettings, new[] { set }, new[] { set }, set.layers[0]);
                Assert.That(set.layers[0].channels.Count, Is.EqualTo(1));
                Assert.That(Pixel(set.layers[0]).a, Is.EqualTo(.6f).Within(.015f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0].channels.Count, Is.EqualTo(2));
            });
        }

        [Test] public void ProjectionHistoryEvictionRetainsSnapshotsNeededByLaterCommands()
        {
            var set = Set(); var layer = set.AddProjectionLayer(); var definition = Definition();
            definition.firstSurfaceOnly = false;
            Generate(definition, new[] { set }, new[] { set }, layer);
            WithStage(Store(set), stage =>
            {
                int capacity = (int)typeof(TexturePaintStageWindow).GetField("LightweightHistoryCapacity",
                    BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
                for (int i = 0; i < capacity + 2; i++)
                {
                    var source = new TexturePaintChannelSourceSettings { source = TexturePaintBrushSource.Color,
                        color = new Color((i + 1f) / (capacity + 3f), .2f, .1f, 1) };
                    Assert.That(Invoke(stage, "ChangeLayerChannelSources", set, set.layers[0],
                        new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings> { [TexturePaintChannel.Albedo] = source }), Is.True);
                }
                for (int i = 0; i < capacity; i++)
                {
                    Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                    Assert.That(Pixel(set.layers[0]).a, Is.GreaterThan(.99f));
                }
                Assert.That(Pixel(set.layers[0]).r, Is.EqualTo(2f / (capacity + 3f)).Within(.003f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
            });
        }

        [Test] public void UnsupportedChannelOverlayLeavesProjectionAndUndoUnchanged()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Roughness, 1);
            var layer = set.AddProjectionLayer(); var definition = Definition();
            definition.GetChannelSource(TexturePaintChannel.Roughness, true);
            Generate(definition, new[] { set }, new[] { set }, layer);
            WithStage(Store(set), stage =>
            {
                var overlay = Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
                overlay.textureList = new Texture[] { Image(Color.blue) };
                Assert.That(Invoke(stage, "ChangeLayerChannelSources", set, layer,
                    new Dictionary<TexturePaintChannel, TexturePaintChannelSourceSettings>
                    { [TexturePaintChannel.Roughness] = new TexturePaintChannelSourceSettings
                        { source = TexturePaintBrushSource.Overlay, sourceOverlay = overlay } }), Is.False);
                Assert.That(set.layers[0], Is.SameAs(layer)); Assert.That(Pixel(layer).r, Is.GreaterThan(.99f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
            });
        }

        [Test] public void LegacySourceMigratesOnceAndClearingMapsDoesNotResurrectIt()
        {
            var settings = Definition(); settings.channel = TexturePaintChannel.Normal;
            settings.normalConvention = TexturePaintNormalConvention.DirectX;
            settings.UpgradeChannelSources(); settings.UpgradeChannelSources();
            Assert.That(settings.channelSources.Count, Is.EqualTo(1));
            Assert.That(settings.GetChannelSource(TexturePaintChannel.Normal).texture, Is.SameAs(settings.texture));
            Assert.That(settings.GetChannelSource(TexturePaintChannel.Normal).normalConvention, Is.EqualTo(TexturePaintNormalConvention.DirectX));
            var copy = settings.Clone(); copy.GetChannelSource(TexturePaintChannel.Normal).texture = null;
            Assert.That(settings.GetChannelSource(TexturePaintChannel.Normal).HasSource, Is.True);
            copy.channelSources.Clear(); copy.Normalize();
            var restored = JsonUtility.FromJson<TexturePaintProjectionSettings>(JsonUtility.ToJson(copy));
            Assert.That(restored.PreferredSource(), Is.Null);
            var set = Set(); var layer = set.AddProjectionLayer();
            Generate(restored, new[] { set }, new[] { set }, layer);
            Assert.That(layer.channels, Is.Empty);
        }

        [Test] public void ChannelValidationLeavesEarlierTargetsUnchangedWhenALaterTargetCannotRender()
        {
            var first = Set(); AddChannel(first, TexturePaintChannel.Normal, 1); var second = Set();
            var a = first.AddProjectionLayer(); var b = second.AddProjectionLayer(); var settings = Definition();
            settings.firstSurfaceOnly = false;
            Generate(settings, new[] { first, second }, new[] { first, second }, a, b);
            var previous = a.channels[TexturePaintChannel.Albedo];
            settings.UpgradeChannelSources(); settings.channelSources.Clear();
            settings.GetChannelSource(TexturePaintChannel.Normal, true).texture = Image(new Color(.5f, .5f, 1, 1));
            using var renderer = new TexturePaintProjectionRenderer();
            Assert.That(renderer.Generate(new[] { first, second }, new[] { first, second }, new[] { a, b }, settings, out string error), Is.False);
            Assert.That(error, Does.Contain("no compatible channels"));
            Assert.That(a.channels[TexturePaintChannel.Albedo], Is.SameAs(previous));
            Assert.That(Pixel(a).r, Is.GreaterThan(.99f)); Assert.That(Pixel(b).r, Is.GreaterThan(.99f));
        }

        [Test] public void MultipleChannelMapsCrossUdimBoundaryTogether()
        {
            var left = Set(Quad(-.5f, 0)); var right = Set(Quad(0, .5f));
            AddChannel(left, TexturePaintChannel.Normal, 1); AddChannel(right, TexturePaintChannel.Normal, 1);
            var a = left.AddProjectionLayer(); var b = right.AddProjectionLayer(); var settings = Definition();
            settings.GetChannelSource(TexturePaintChannel.Normal, true).texture = Image(new Color(.8f, .5f, .9f, 1));
            Generate(settings, new[] { left, right }, new[] { left, right }, a, b);
            foreach (var layer in new[] { a, b })
            {
                Assert.That(Pixel(layer).r, Is.GreaterThan(.99f));
                Assert.That(Pixel(layer, channel: TexturePaintChannel.Normal).r, Is.GreaterThan(.77f));
            }
            Assert.That(a.projectionSettings.channelSources, Is.Not.SameAs(b.projectionSettings.channelSources));
            Assert.That(a.projectionSettings.GetChannelSource(TexturePaintChannel.Normal), Is.Not.SameAs(b.projectionSettings.GetChannelSource(TexturePaintChannel.Normal)));
        }

        [Test] public void LinkedProjectionInheritsEveryMapAndRetainsOwnPlacement()
        {
            var set = Set(); AddChannel(set, TexturePaintChannel.Normal, 1); var source = set.AddProjectionLayer();
            var settings = Definition(); settings.width = .4f;
            settings.GetChannelSource(TexturePaintChannel.Normal, true).texture = Image(new Color(.8f, .5f, .9f, 1));
            Generate(settings, new[] { set }, new[] { set }, source);
            var instance = set.CloneLayer(source, "Instance"); set.layers.Add(instance);
            instance.projectionSettings.position = new Vector3(.2f, 0, 0);
            instance.links = new TexturePaintLayerLinks { instance = TexturePaintLayerReference.To(set, source) };
            var store = Store(set); store.RefreshLayerLinks();
            Assert.That(Pixel(instance, 44, 32).r, Is.GreaterThan(.99f));
            source.projectionSettings.GetChannelSource(TexturePaintChannel.Normal).texture = Image(new Color(.2f, .5f, .9f, 1));
            store.RefreshLayerLinks();
            Assert.That(instance.linkError, Is.Null);
            Assert.That(Pixel(instance, 44, 32, TexturePaintChannel.Normal).r, Is.LessThan(.23f));
            Assert.That(Pixel(instance, 44, 32).r, Is.GreaterThan(.99f));
            Assert.That(instance.projectionSettings.position.x, Is.EqualTo(.2f));
        }

        [Test] public void OverlayProjectsAllCompatibleChannelsWithCommonCoverage()
        {
            var set=Set();AddChannel(set,TexturePaintChannel.Roughness,1);var layer=set.AddProjectionLayer();
            var overlay=Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
            overlay.textureList=new Texture[]{Image(new Color(1,0,0,0.6f)),Image(new Color(0.3f,0.3f,0.3f,1))};
            var settings=Definition();settings.source=TexturePaintBrushSource.Overlay;settings.overlay=overlay;
            Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(layer.channels.Count,Is.EqualTo(2));Assert.That(Pixel(layer).a,Is.EqualTo(0.6f).Within(0.02f));
            Assert.That(Pixel(layer,32,32,TexturePaintChannel.Roughness).a,Is.EqualTo(0.6f).Within(0.02f));
        }

        [TestCase(TexturePaintProjectionMode.Planar, false, false)]
        [TestCase(TexturePaintProjectionMode.Planar, false, true)]
        [TestCase(TexturePaintProjectionMode.Wrapped, false, false)]
        [TestCase(TexturePaintProjectionMode.Wrapped, false, true)]
        [TestCase(TexturePaintProjectionMode.Planar, true, false)]
        [TestCase(TexturePaintProjectionMode.Planar, true, true)]
        [TestCase(TexturePaintProjectionMode.Wrapped, true, false)]
        [TestCase(TexturePaintProjectionMode.Wrapped, true, true)]
        public void SpriteAndOverlayChannelsShareAlbedoHolesAndPartialCoverage(
            TexturePaintProjectionMode mode, bool useOverlay, bool opaqueSecondaryMaps)
        {
            var set = Set(); var layer = set.AddProjectionLayer(); var settings = Definition(); settings.mode = mode;
            var expectedColors = new Dictionary<TexturePaintChannel, Color>();
            var overlayImages = new List<Texture>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                // Overlay assets do not supply the painter's auxiliary normal-control channel.
                if (useOverlay && TexturePaintChannelUtility.IsAuxiliary(channel)) continue;
                if (channel != TexturePaintChannel.Albedo) AddChannel(set, channel, overlayImages.Count);
                Color color = channel == TexturePaintChannel.Normal
                    ? new Color(.65f, .7f, Mathf.Sqrt(.75f) * .5f + .5f, opaqueSecondaryMaps ? 1 : 0)
                    : new Color(.2f + (int)channel * .03f, .3f + (int)channel * .025f,
                        .8f - (int)channel * .04f, opaqueSecondaryMaps ? 1 : 0);
                expectedColors[channel] = TexturePaintChannelUtility.ConstrainColor(channel, color);
                var image = Image(Color.green); image.filterMode = FilterMode.Point;
                // Cropped sprites use different atlas rectangles; opaque pixels outside the crop
                // must not become coverage for any channel.
                var rect = useOverlay ? new Rect(0, 0, 16, 16) : new Rect((int)channel % 2 * 8, 4, 8, 8);
                for (int y = 0; y < rect.height; y++) for (int x = 0; x < rect.width; x++)
                {
                    Color sample = color;
                    if (channel == TexturePaintChannel.Albedo)
                    {
                        bool hole = x >= rect.width / 4 && x < rect.width * 3 / 4 &&
                            y >= rect.height / 4 && y < rect.height * 3 / 4;
                        sample.a = hole ? 0 : x < rect.width / 2 ? 1 : .4f;
                    }
                    image.SetPixel((int)rect.x + x, (int)rect.y + y, sample);
                }
                image.Apply(); overlayImages.Add(image);
                if (!useOverlay)
                {
                    var source = settings.GetChannelSource(channel, true);
                    source.texture = null;
                    source.sprite = Own(Sprite.Create(image, rect, Vector2.one * .5f));
                }
            }
            if (useOverlay)
            {
                settings.source = TexturePaintBrushSource.Overlay;
                settings.overlay = Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
                settings.overlay.textureList = overlayImages.ToArray();
            }
            Generate(settings, new[] { set }, new[] { set }, layer);
            Assert.That(layer.channels.Count, Is.EqualTo(expectedColors.Count));
            Assert.That(Pixel(layer, 16, 8).a, Is.GreaterThan(.99f), "Opaque albedo region");
            Assert.That(Pixel(layer, 48, 8).a, Is.EqualTo(.4f).Within(.015f), "Partial albedo region");
            Assert.That(Pixel(layer).a, Is.LessThan(.001f), "Transparent albedo hole");
            Color[] coverage = Pixels(layer.channels[TexturePaintChannel.Albedo].Front);
            foreach (var expected in expectedColors)
            {
                Color[] actual = Pixels(layer.channels[expected.Key].Front);
                float alphaError = 0, rgbError = 0;
                for (int i = 0; i < actual.Length; i++)
                {
                    alphaError = Mathf.Max(alphaError, Mathf.Abs(actual[i].a - coverage[i].a));
                    if (coverage[i].a <= .05f) continue;
                    rgbError = Mathf.Max(rgbError, Mathf.Abs(actual[i].r - expected.Value.r));
                    rgbError = Mathf.Max(rgbError, Mathf.Abs(actual[i].g - expected.Value.g));
                    rgbError = Mathf.Max(rgbError, Mathf.Abs(actual[i].b - expected.Value.b));
                }
                Assert.That(alphaError, Is.LessThan(.002f), expected.Key + " must use the complete albedo silhouette");
                Assert.That(rgbError, Is.LessThan(.025f), expected.Key + " must retain its RGB values in visible pixels");
            }
        }

        private static Color[] Pixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var read = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); read.Apply();
                return read.GetPixels();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(read); }
        }

        [TestCase(false, false, false)] [TestCase(false, true, false)]
        [TestCase(true, false, false)] [TestCase(true, true, false)]
        [TestCase(false, false, true)] [TestCase(false, true, true)]
        [TestCase(true, false, true)] [TestCase(true, true, true)]
        public void GpuVisibilityMatchesRaycastDepthForPlanarAndWrappedPatches(bool wrapped, bool frontOnly, bool mirrored)
        {
            if (!SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RFloat))
                Assert.Ignore("RFloat blending is unavailable; this device uses CPU visibility.");
            var set = Set(Quad(-.8f, .8f, -.05f));
            var raised = Set(Quad(-.73f, -.137f, .035f));
            var reversed = Set(Quad(.13f, .79f, .09f, true));
            var outside = Set(Quad(-.8f, .8f, .7f));
            var all = new[] { set, raised, reversed, outside };
            if (mirrored)
            {
                var go = Own(new GameObject("Mirrored projection surface"));
                go.transform.localScale = new Vector3(-1, 1.3f, 1);
                foreach (var member in all) member.surface.gameObject = go;
            }
            var settings = Definition(); settings.width = .8f; settings.height = .6f;
            settings.rotation = Quaternion.Euler(7, -9, 13); settings.frontFacesOnly = frontOnly;
            settings.depthFromSurface = true;
            if (wrapped) { settings.mode = TexturePaintProjectionMode.Wrapped; settings.points[4].z = .08f; }
            using var renderer = new TexturePaintProjectionRenderer();
            var layer = set.AddProjectionLayer();
            Assert.That(renderer.Generate(all, new[] { set }, new[] { layer }, settings, out string error), Is.True, error);
            var gpu = (RenderTexture)typeof(TexturePaintProjectionRenderer).GetField("visibilityTarget", Private).GetValue(renderer);
            object[] patchArgs = { settings, null };
            object patch = typeof(TexturePaintProjectionRenderer).GetMethod("BuildPatch", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, patchArgs);
            var cpu = (Texture2D)typeof(TexturePaintProjectionRenderer).GetMethod("BuildVisibilityCPU", Private)
                .Invoke(renderer, new[] { settings, new TexturePaintProjectionGeometry(all), patch });
            var actual = Pixels(gpu); var expected = cpu.GetPixels();
            int hits = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                bool hit = Mathf.Abs(expected[i].r) < 1e9f;
                Assert.That(Mathf.Abs(actual[i].r) < 1e9f, Is.EqualTo(hit), "Visibility coverage at pixel " + i);
                if (hit) { hits++; Assert.That(actual[i].r, Is.EqualTo(expected[i].r).Within(.00003f), "Depth at pixel " + i); }
            }
            Assert.That(hits, Is.GreaterThan(actual.Length / 10));
        }

        [TestCase(TexturePaintProjectionMode.Planar)] [TestCase(TexturePaintProjectionMode.Wrapped)]
        public void ReusingProjectionTargetsMatchesFreshRenderingAndClearsPreviousPlacement(TexturePaintProjectionMode mode)
        {
            var set = Set(); var layer = set.AddProjectionLayer(); var reference = set.AddProjectionLayer();
            var settings = Definition(); settings.mode = mode;
            if (mode == TexturePaintProjectionMode.Wrapped) settings.points[4].z = .05f;
            settings.fade = TexturePaintProjectionFade.AlphaOutline;
            using var renderer = new TexturePaintProjectionRenderer(stableGeometry: new[] { set });
            Assert.That(renderer.Generate(new[] { set }, new[] { set }, new[] { layer }, settings, out _), Is.True);
            var target = layer.channels[TexturePaintChannel.Albedo]; long revision = target.Revision;
            settings.width = .37f; settings.height = .81f; settings.rotation = Quaternion.Euler(0, 0, 31);
            Assert.That(renderer.Generate(new[] { set }, new[] { set }, new[] { layer }, settings, out _), Is.True);
            Assert.That(layer.channels[TexturePaintChannel.Albedo], Is.SameAs(target), "Dragging must not allocate a full texture pair per update.");
            Assert.That(target.Revision, Is.GreaterThan(revision), "Compositing caches must see the changed pixels.");
            Generate(settings, new[] { set }, new[] { set }, reference);
            Color[] actual = Pixels(target.Front), expected = Pixels(reference.channels[TexturePaintChannel.Albedo].Front);
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i]), "Pixel " + i);
            Assert.That(Pixel(layer, 5, 5).a, Is.Zero, "Previous placement must be cleared.");
            settings.texture = null;
            Assert.That(renderer.Generate(new[] { set }, new[] { set }, new[] { layer }, settings, out _), Is.True);
            Assert.That(Pixel(layer).a, Is.Zero, "Removing a source clears reused targets.");
        }

        [Test] public void FailedDragUpdateKeepsLastValidResultAndUndoSnapshot()
        {
            var set = Set(); var initial = set.AddProjectionLayer(); var settings = Definition();
            Generate(settings, new[] { set }, new[] { set }, initial);
            WithStage(Store(set), stage =>
            {
                settings.width = .5f;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, initial, settings, true), Is.True);
                var working = set.layers[0]; var previous = Pixels(working.channels[TexturePaintChannel.Albedo].Front);
                var invalid = settings.Clone(); invalid.regionSurfaceId = "missing"; invalid.regionTriangle = 0;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, working, invalid, true), Is.False);
                Assert.That(set.layers[0], Is.SameAs(working));
                Assert.That(working.projectionSettings.regionSurfaceId, Is.EqualTo(settings.regionSurfaceId));
                CollectionAssert.AreEqual(previous, Pixels(working.channels[TexturePaintChannel.Albedo].Front));
                settings.width = .7f;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, working, settings, true), Is.True);
                Invoke(stage, "FinishProjectionEdit");
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Assert.That(set.layers[0], Is.SameAs(initial));
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                Assert.That(set.layers[0].projectionSettings.width, Is.EqualTo(.7f));
            });
        }

        [Test] public void RepeatedDragUpdatesPreserveOriginalPixelsAndCommitOneUndoStep()
        {
            var set = Set(); var initial = set.AddProjectionLayer(); var settings = Definition();
            Generate(settings, new[] { set }, new[] { set }, initial);
            WithStage(Store(set), stage =>
            {
                EditableTextureTarget workingTarget = null;
                for (int i = 0; i < 12; i++)
                {
                    settings.position = Vector3.right * (i + 1) / 10f;
                    Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, set.layers[0], settings, true), Is.True);
                    var target = set.layers[0].channels[TexturePaintChannel.Albedo];
                    if (workingTarget == null) workingTarget = target;
                    else Assert.That(target, Is.SameAs(workingTarget));
                    Assert.That(Pixel(initial).a, Is.GreaterThan(.99f), "The undo snapshot stays immutable during the drag.");
                }
                Assert.That(Pixel(set.layers[0]).a, Is.Zero);
                Invoke(stage, "FinishProjectionEdit");
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(set.layers[0], Is.SameAs(initial));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
                Assert.That(Pixel(set.layers[0]).a, Is.Zero);
                settings.position = Vector3.zero;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, set.layers[0], settings, true), Is.True);
                settings.position = Vector3.left * .1f;
                Assert.That(Invoke(stage, "ChangeProjectionWithHistory", set, set.layers[0], settings, true), Is.True);
                Invoke(stage, "CancelProjectionEdit");
                Assert.That(Pixel(set.layers[0]).a, Is.Zero, "Escape restores the committed result after multiple updates.");
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
                Assert.That(Pixel(set.layers[0]).a, Is.GreaterThan(.99f));
            });
        }

        [Test] public void GestureUndoRedoDuplicationAndRasterizationKeepIndependentDefinitions()
        {
            var set=Set();set.AddProjectionLayer();var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());var settings=Definition();
            Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,true),Is.True);
            settings.position=new Vector3(0.2f,0,0);Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,true),Is.True);
            Invoke(stage,"FinishProjectionEdit");
            Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers[0].projectionSettings.placed,Is.False);
            Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(set.layers[0].projectionSettings.position.x,Is.EqualTo(0.2f));
            Invoke(stage,"DuplicateLayerWithHistory",set,0);
            Assert.That(set.layers.Count,Is.EqualTo(2));Assert.That(set.layers[1].projectionSettings,Is.Not.SameAs(set.layers[0].projectionSettings));
            Assert.That(Invoke(stage,"RasterizeFillLayerWithHistory",set,set.layers[0]),Is.True);
            Assert.That(set.layers[0].kind,Is.EqualTo(TexturePaintLayerKind.Paint));Assert.That(set.layers[0].projectionSettings,Is.Null);
            Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers[0].kind,Is.EqualTo(TexturePaintLayerKind.Projection));
            Invoke(stage,"ClearLightweightHistory");
        }
        [Test] public void FailedProjectionEditLeavesExistingPixelsAndHistoryIntact()
        {
            var set=Set();set.AddProjectionLayer();var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());var settings=Definition();
            Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,false),Is.True);
            var original=set.layers[0];settings.regionSurfaceId="missing";settings.regionTriangle=0;
            Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,false),Is.False);
            Assert.That(set.layers[0],Is.SameAs(original));Assert.That(Pixel(original).a,Is.GreaterThan(0.99f));
            Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers[0].projectionSettings.placed,Is.False);
            Invoke(stage,"ClearLightweightHistory");
        }
        [Test] public void LogicalUdimEditsMasksDuplicationAndRepairStayTogether()
        {
            var first=Set(Quad(-0.5f,0));var second=Set(Quad(0,0.5f));var store=Store(first,second);
            var reconstruction=new MeshReconstructionResult();reconstruction.logicalTargets.Rebuild(new[]{first.surface});
            var target=reconstruction.logicalTargets.Targets[0];target.isUdim=true;target.members[0].udimTileNumber=1001;target.members[0].textureSets.Add(first);
            var member=new TexturePaintLogicalTargetMember {slotName="Body2",udimTileNumber=1002};member.textureSets.Add(second);target.members.Add(member);
            var logical=new TexturePaintLogicalLayerController(reconstruction.logicalTargets);
            var layer=first.AddProjectionLayer();Assert.That(logical.LinkAndRepair(target,first,layer,null,out _),Is.True);
            var controller=new TexturePaintStageController();
            typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller,reconstruction);
            typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller,logical);
            typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,controller);
            try
            {
                var settings=Definition();settings.regionSurfaceId=first.persistentId;settings.regionTriangle=0;
                first.AddLayerMask(first.layers[0],0.35f);second.AddLayerMask(second.layers[0],0.65f);
                Assert.That(Invoke(stage,"ChangeProjectionWithHistory",first,first.layers[0],settings,false),Is.True);
                Assert.That(Pixel(first.layers[0]).a,Is.GreaterThan(0.99f));Assert.That(Pixel(second.layers[0]).a,Is.GreaterThan(0.99f));
                Assert.That(first.layers[0].layerMask.baseValue,Is.EqualTo(0.35f));Assert.That(second.layers[0].layerMask.baseValue,Is.EqualTo(0.65f));
                settings.position=Vector3.right*2;Assert.That(Invoke(stage,"ChangeProjectionWithHistory",first,first.layers[0],settings,false),Is.True);
                Assert.That(Pixel(first.layers[0]).a,Is.LessThan(0.001f));Assert.That(Pixel(second.layers[0]).a,Is.LessThan(0.001f));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(Pixel(first.layers[0]).a,Is.GreaterThan(0.99f));Assert.That(Pixel(second.layers[0]).a,Is.GreaterThan(0.99f));
                Invoke(stage,"DuplicateLayerWithHistory",first,0);Assert.That(first.layers.Count,Is.EqualTo(2));Assert.That(second.layers.Count,Is.EqualTo(2));
                Assert.That(first.layers[1].projectionSettings,Is.Not.SameAs(first.layers[0].projectionSettings));
                Invoke(stage,"ClearLightweightHistory");
                var removed=second.layers[1];second.layers.RemoveAt(1);removed.Dispose();
                Assert.That(logical.LinkAndRepair(target,first,first.layers[1],null,out _),Is.True);
                Assert.That(Pixel(second.layers[1]).a,Is.GreaterThan(0.99f),"A repaired tile regenerates its projection.");
            }
            finally { Invoke(stage,"ClearLightweightHistory");typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,null); }
        }
        [Test] public void CancelGestureRestoresAllSettingsAndPixelsWithoutAddingHistory()
        {
            var set=Set();set.AddProjectionLayer();var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());var settings=Definition();
            Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,false),Is.True);
            settings.position=Vector3.right*2;Assert.That(Invoke(stage,"ChangeProjectionWithHistory",set,set.layers[0],settings,true),Is.True);
            Invoke(stage,"CancelProjectionEdit");Assert.That(Pixel(set.layers[0]).a,Is.GreaterThan(0.99f));
            Assert.That(set.layers[0].projectionSettings.position,Is.EqualTo(Vector3.zero));
            Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers[0].projectionSettings.placed,Is.False);
            Invoke(stage,"ClearLightweightHistory");
        }
        [Test] public void SpriteRegionUsesItsOwnCoverageAndOrientation()
        {
            var image=Image(Color.blue);for(int y=0;y<16;y++) for(int x=0;x<8;x++) image.SetPixel(x,y,new Color(1,0,0,0.5f));image.Apply();
            var sprite=Own(Sprite.Create(image,new Rect(0,0,8,16),Vector2.one*0.5f));
            var settings=Definition();settings.texture=null;settings.sprite=sprite;
            var set=Set();var layer=set.AddProjectionLayer();Generate(settings,new[]{set},new[]{set},layer);
            Assert.That(Pixel(layer).r,Is.GreaterThan(0.99f));Assert.That(Pixel(layer).b,Is.LessThan(0.01f));Assert.That(Pixel(layer).a,Is.EqualTo(0.5f).Within(0.02f));
        }
        [TestCase(false)] [TestCase(true)] public async System.Threading.Tasks.Task MaterialPresetPreservesAndRegeneratesWorldSpaceProjection(bool multipleChannels)
        {
            var source=Set();var layer=source.AddProjectionLayer();var settings=Definition();
            if (multipleChannels)
            {
                AddChannel(source, TexturePaintChannel.Normal, 1);
                settings.GetChannelSource(TexturePaintChannel.Normal, true).texture = Image(new Color(.5f, .5f, 1, 1));
            }
            Generate(settings,new[]{source},new[]{source},layer);
            var preset=Own(ScriptableObject.CreateInstance<TexturePaintMaterialPreset>());
            TexturePaintMaterialPresetStorage.Capture(preset,source,new[]{layer},false,null);
            Assert.That((preset.portability & TexturePaintPresetPortability.MeshDependent)!=0,Is.True);
            var destination=Set(Quad(z:1)); if (multipleChannels) AddChannel(destination, TexturePaintChannel.Normal, 1);
            var store=Store(destination);
            var result=await TexturePaintMaterialPresetStorage.ApplyAsync(preset,store,new[]{destination},null,null,
                new TexturePaintMaterialPresetApplyOptions{wrapInGroup=false},null,System.Threading.CancellationToken.None);
            Assert.That(result.warnings,Is.Empty);Assert.That(result.created[0].layer,Is.SameAs(destination.layers[0]));
            Assert.That(destination.layers[0].projectionSettings.texture,Is.SameAs(settings.texture));
            if (multipleChannels)
            {
                Assert.That(destination.layers[0].projectionSettings.GetChannelSource(TexturePaintChannel.Normal).texture,
                    Is.SameAs(settings.GetChannelSource(TexturePaintChannel.Normal).texture));
                Assert.That(Pixel(destination.layers[0], channel: TexturePaintChannel.Normal).a, Is.LessThan(.001f));
            }
            Assert.That(Pixel(destination.layers[0]).a,Is.LessThan(0.001f),"A world-fixed projection does not copy UV pixels to a moved surface.");
        }
        [TestCase(false)] [TestCase(true)] public void SavedProjectionRestoresSourcePatchAndRegeneratesForCurrentGeometry(bool multipleChannels)
        {
            string parent="Assets";
            foreach(string part in Folder.Substring(7).Split('/')) { if(!AssetDatabase.IsValidFolder(parent+"/"+part)) AssetDatabase.CreateFolder(parent,part);parent+="/"+part; }
            var image=Image(Color.red);AssetDatabase.CreateAsset(image,Folder+"/Source.asset");objects.Remove(image);
            var set=Set();var store=Store(set);var layer=set.AddProjectionLayer();var settings=Definition(image);
            if (multipleChannels)
            {
                AddChannel(set, TexturePaintChannel.Normal, 1);
                var normalImage = Image(new Color(.5f, .5f, 1, 1)); AssetDatabase.CreateAsset(normalImage, Folder + "/Normal.asset"); objects.Remove(normalImage);
                var normalSprite = Sprite.Create(normalImage, new Rect(0, 0, 16, 16), Vector2.one * .5f);
                AssetDatabase.AddObjectToAsset(normalSprite, normalImage); AssetDatabase.SaveAssetIfDirty(normalImage);
                var map = settings.GetChannelSource(TexturePaintChannel.Normal, true);
                map.sprite = normalSprite; map.normalConvention = TexturePaintNormalConvention.DirectX;
            }
            settings.mode=TexturePaintProjectionMode.Wrapped;settings.points[4].z=0.01f;settings.depthFromSurface=true;
            settings.flipX = true; settings.flipY = true;
            Generate(settings,new[]{set},new[]{set},layer);
            var document=ScriptableObject.CreateInstance<TexturePaintDocument>();string path=Folder+"/Projection.asset";AssetDatabase.CreateAsset(document,path);
            TexturePaintDocumentStorage.Save(document,store);AssetDatabase.SaveAssetIfDirty(document);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);document=AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(path);
            TexturePaintRecoveryStore.RecoveryFolderOverride=Folder+"/Recovery";
            try
            {
                TexturePaintRecoveryStore.SaveImmediate(document,"projection-test");
                Assert.That(TexturePaintRecoveryStore.TryLoad("projection-test",out TexturePaintDocument recovery,out string error),Is.True,error);
                Own(recovery);
                Assert.That(AssetDatabase.GetAssetPath(recovery.surfaces[0].layers[0].projectionSettings.texture),Is.EqualTo(Folder+"/Source.asset"));
                Assert.That(recovery.surfaces[0].layers[0].projectionSettings.points[4].z,Is.EqualTo(0.01f));
                Assert.That(recovery.surfaces[0].layers[0].projectionSettings.depthFromSurface,Is.True);
                Assert.That(recovery.surfaces[0].layers[0].projectionSettings.flipX, Is.True);
                Assert.That(recovery.surfaces[0].layers[0].projectionSettings.flipY, Is.True);
                if (multipleChannels)
                {
                    var recovered = recovery.surfaces[0].layers[0].projectionSettings;
                    Assert.That(AssetDatabase.GetAssetPath(recovered.GetChannelSource(TexturePaintChannel.Normal).sprite), Is.EqualTo(Folder + "/Normal.asset"));
                    Assert.That(AssetDatabase.GetAssetPath(recovered.GetChannelSource(TexturePaintChannel.Albedo).texture), Is.EqualTo(Folder + "/Source.asset"));
                }
            }
            finally { TexturePaintRecoveryStore.Delete("projection-test");TexturePaintRecoveryStore.RecoveryFolderOverride=null; }
            var restored=Set(set.surface.mesh); if (multipleChannels) AddChannel(restored, TexturePaintChannel.Normal, 1);
            var restoredStore=Store(restored);TexturePaintDocumentStorage.Restore(document,restoredStore);
            Assert.That(restored.layers[0].kind,Is.EqualTo(TexturePaintLayerKind.Projection));
            Assert.That(restored.layers[0].projectionSettings.depthFromSurface,Is.True);
            Assert.That(restored.layers[0].projectionSettings.flipX, Is.True);
            Assert.That(restored.layers[0].projectionSettings.flipY, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(restored.layers[0].projectionSettings.texture),Is.EqualTo(Folder+"/Source.asset"));
            Assert.That(restored.layers[0].projectionSettings.points[4].z,Is.EqualTo(0.01f));Assert.That(Pixel(restored.layers[0]).a,Is.GreaterThan(0.9f));
            if (multipleChannels)
            {
                Assert.That(AssetDatabase.GetAssetPath(restored.layers[0].projectionSettings.GetChannelSource(TexturePaintChannel.Normal).sprite), Is.EqualTo(Folder + "/Normal.asset"));
                Assert.That(restored.layers[0].projectionSettings.GetChannelSource(TexturePaintChannel.Normal).normalConvention, Is.EqualTo(TexturePaintNormalConvention.DirectX));
                Assert.That(Pixel(restored.layers[0], channel: TexturePaintChannel.Normal).a, Is.GreaterThan(.9f));
            }
            var vertices=set.surface.mesh.vertices;for(int i=0;i<vertices.Length;i++) vertices[i]+=Vector3.forward;
            set.surface.mesh.vertices=vertices;set.surface.mesh.RecalculateBounds();
            TexturePaintDocumentStorage.Restore(document,restoredStore);Assert.That(Pixel(restored.layers[0]).a,Is.LessThan(0.001f));
            Assert.That(restored.layers[0].projectionSettings.position,Is.EqualTo(Vector3.zero));
        }
    }
}
#endif

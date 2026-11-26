#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>A look per piece: fabrics tiled at their real size along the grain, baked as masked overlays on one slot.</summary>
    public sealed class PieceLooksTests
    {
        private const string Folder = "Assets/UMAProjectData/ClothesDesigner/Tests_Temp_Looks";
        private CapsuleBodyFixture capsule;
        private readonly List<Object> made = new List<Object>();
        private readonly List<System.IDisposable> disposables = new List<System.IDisposable>();

        [SetUp]
        public void Make() => capsule = new CapsuleBodyFixture();

        [TearDown]
        public void Clean()
        {
            foreach (System.IDisposable d in disposables) d.Dispose();
            disposables.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            capsule.Dispose();
            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
                if (AssetDatabase.FindAssets("", new[] { "Assets/UMAProjectData/ClothesDesigner" }).Length == 0) AssetDatabase.DeleteAsset("Assets/UMAProjectData/ClothesDesigner");
            }
        }

        private ClothesPattern Vest()
        {
            ClothesPattern pattern = VestPattern.Create();
            made.Add(pattern);
            UvLayout.Repack(pattern);
            return pattern;
        }

        private static PatternMesh Mesh(ClothesPattern pattern) => PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);

        private Texture2D Solid(int size, System.Func<int, int, Color> colour, bool linear)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, linear) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) t.SetPixel(x, y, colour(x, y));
            t.Apply(false);
            made.Add(t);
            return t;
        }

        private Texture2D ReadBack(Texture texture)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(texture)));
            made.Add(t);
            return t;
        }

        private static Color32 At(Texture2D t, Vector2 uv) => t.GetPixel(Mathf.FloorToInt(uv.x * t.width), Mathf.FloorToInt(uv.y * t.height));

        private static Vector2 Centre(PatternPiece piece) => piece.Bounds().center;

        [Test]
        public void DrawnAt_UndoesToUV()
        {
            ClothesPattern pattern = Vest();
            for (int p = 0; p < pattern.pieces.Count; p++)
            {
                foreach (PatternPoint point in pattern.pieces[p].points)
                {
                    Vector2 back = GarmentLooks.DrawnAt(pattern, p, UvLayout.ToUV(pattern, p, point.position));
                    Assert.That((back - point.position).magnitude, Is.LessThan(1e-3f));
                }
            }
        }

        [Test]
        public void LookCoordinate_OneRepeatAlongTheGrain_IsOneTextureHeight()
        {
            var piece = new PatternPiece { grainAngle = 30f };
            var look = new PieceLook { repeatCm = 12f };
            Vector2 along = new Vector2(-Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad)) * 12f;
            Vector2 a = GarmentLooks.LookCoordinate(piece, look, new Vector2(3f, 4f));
            Vector2 b = GarmentLooks.LookCoordinate(piece, look, new Vector2(3f, 4f) + along);
            Assert.That((b - a - Vector2.up).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void OwnerMap_GivesEachIslandToItsPiece_AndTheBorderToNone()
        {
            ClothesPattern pattern = Vest();
            PatternMesh mesh = Mesh(pattern);
            const int size = 256;
            int[] owner = GarmentLooks.OwnerMap(pattern, mesh, size, 2);
            for (int p = 0; p < pattern.pieces.Count; p++)
            {
                Vector2 uv = UvLayout.ToUV(pattern, p, Centre(pattern.pieces[p]));
                Assert.That(owner[Mathf.FloorToInt(uv.y * size) * size + Mathf.FloorToInt(uv.x * size)], Is.EqualTo(p), pattern.pieces[p].name);
            }
            Assert.That(owner[0], Is.EqualTo(-1));
        }

        [Test]
        public void Bake_MakesAMaskedOverlayPerLook_TiledAtItsRealSize_WithItsSharedColour()
        {
            ClothesPattern pattern = Vest();
            // Red on the left half of the texture, blue on the right: the colour changes every half repeat across the grain.
            Texture2D stripes = Solid(8, (x, y) => x < 4 ? Color.red : Color.blue, false);
            // A weave leaning along the texture's x.
            Texture2D weave = Solid(4, (x, y) => new Color(0.8f, 0.5f, 0.9f, 1f), true);
            var denim = new PieceLook { id = ClothesPattern.NewId(), name = "Denim", albedo = stripes, normal = weave, repeatCm = 4f };
            var plain = new PieceLook { id = ClothesPattern.NewId(), name = "Plain", color = new Color(0.2f, 0.6f, 0.3f), sharedColorName = "Shirt2" };
            pattern.looks.Add(denim);
            pattern.looks.Add(plain);
            pattern.pieces[0].lookId = denim.id;
            pattern.pieces[1].lookId = plain.id;

            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            session.Step(60);
            GarmentBaker.EnsureFolder(Folder);
            PublishSettings p = fit.publish;
            p.slotName = "LooksTest";
            p.folder = Folder;
            p.material = DesignerSession.DefaultMaterial();
            Assert.That(p.material, Is.Not.Null, "the project's default UMA material");
            p.createWardrobeRecipe = true;
            // The looks are worn through the recipe, which needs a race.
            string[] races = AssetDatabase.FindAssets("t:RaceData HumanMale30");
            if (races.Length == 0) races = AssetDatabase.FindAssets("t:RaceData");
            if (races.Length == 0) Assert.Ignore("No race in the project to make a wardrobe recipe for.");
            fit.race = AssetDatabase.LoadAssetAtPath<RaceData>(AssetDatabase.GUIDToAssetPath(races[0]));
            p.addToGlobalLibrary = false;
            p.addFacing = false;
            p.bakeNormalMap = false;
            p.addTopstitch = false;
            GarmentBaker.Result result = GarmentBaker.Bake(session);

            Assert.That(result.Looks.Count, Is.EqualTo(2), string.Join(" ", result.Messages));
            GarmentLooks.BakedLook bakedDenim = result.Looks.Find(l => l.look == denim), bakedPlain = result.Looks.Find(l => l.look == plain);
            Assert.That(bakedDenim.pieces, Is.EqualTo(new[] { pattern.pieces[0].name }));
            Assert.That(bakedDenim.overlay.alphaMask, Is.Not.Null);
            Assert.That(bakedDenim.overlay.material, Is.EqualTo(result.Published.Overlay.material));

            // The masks: each look's pieces and nothing else.
            Texture2D denimMask = ReadBack(bakedDenim.overlay.alphaMask), plainMask = ReadBack(bakedPlain.overlay.alphaMask);
            Vector2 front = UvLayout.ToUV(pattern, 0, Centre(pattern.pieces[0])), back = UvLayout.ToUV(pattern, 1, Centre(pattern.pieces[1]));
            Assert.That(At(denimMask, front).a, Is.EqualTo(255));
            Assert.That(At(denimMask, back).a, Is.EqualTo(0));
            Assert.That(At(plainMask, back).a, Is.EqualTo(255));
            Assert.That(At(plainMask, front).a, Is.EqualTo(0));

            // The stripes repeat every 4 cm of cloth across the grain.
            Texture2D colour = ReadBack(bakedDenim.overlay.textureList[0]);
            PatternPiece piece = pattern.pieces[0];
            Vector2 c = Centre(piece);
            Vector2 across = new Vector2(Mathf.Cos(piece.grainAngle * Mathf.Deg2Rad), Mathf.Sin(piece.grainAngle * Mathf.Deg2Rad));
            float offset = GarmentLooks.LookCoordinate(piece, denim, c).x;
            Vector2 red = c + across * ((Mathf.Floor(offset) + 0.25f - offset) * 4f);
            Color32 r0 = At(colour, UvLayout.ToUV(pattern, 0, red));
            Color32 b0 = At(colour, UvLayout.ToUV(pattern, 0, red + across * 2f));
            Color32 r1 = At(colour, UvLayout.ToUV(pattern, 0, red + across * 4f));
            Assert.That(r0.r, Is.GreaterThan(200)); Assert.That(r0.b, Is.LessThan(60));
            Assert.That(b0.b, Is.GreaterThan(200)); Assert.That(b0.r, Is.LessThan(60));
            Assert.That(r1.r, Is.GreaterThan(200));

            // The weave's lean, turned from the texture's axes into the slot's on this piece.
            int normalChannel = -1;
            UMAMaterial material = bakedDenim.overlay.material;
            for (int i = 0; i < material.channels.Length; i++) if (material.channels[i].channelType == UMAMaterial.ChannelType.NormalMap) { normalChannel = i; break; }
            Assert.That(normalChannel, Is.GreaterThanOrEqualTo(0), "the default material has a normal map channel");
            Texture2D normal = ReadBack(bakedDenim.overlay.textureList[normalChannel]);
            Color32 n = At(normal, front);
            Vector2 lean = new Vector2(n.r / 127.5f - 1f, n.g / 127.5f - 1f);
            float turn = GarmentLooks.NormalTurn(piece, denim) * Mathf.Deg2Rad;
            Vector2 expected = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)) * 0.6f;
            Assert.That((lean - expected).magnitude, Is.LessThan(0.05f), "lean " + lean + " expected " + expected);

            // The recipe wears both looks over the clothes' own overlay, the plain one by its shared colour.
            string recipe = result.Published.Recipe.recipeString;
            Assert.That(recipe, Does.Contain(bakedDenim.overlay.overlayName));
            Assert.That(recipe, Does.Contain(bakedPlain.overlay.overlayName));
            Assert.That(recipe, Does.Contain("Shirt2"));
        }

        [Test]
        public void Bake_WithoutLooks_MakesNone()
        {
            ClothesPattern pattern = Vest();
            Assert.That(GarmentLooks.HasLooks(pattern), Is.False);
            pattern.looks.Add(new PieceLook { id = ClothesPattern.NewId() });
            Assert.That(GarmentLooks.HasLooks(pattern), Is.False, "a look no piece uses");
            pattern.pieces[0].lookId = pattern.looks[0].id;
            Assert.That(GarmentLooks.HasLooks(pattern), Is.True);
        }

        [Test]
        public void MirrorCopy_KeepsThePiecesLook()
        {
            ClothesPattern pattern = Vest();
            pattern.pieces[0].lookId = "abc";
            Assert.That(PatternEditing.MirrorCopy(pattern.pieces[0], "Copy").lookId, Is.EqualTo("abc"));
        }
    }
}
#endif

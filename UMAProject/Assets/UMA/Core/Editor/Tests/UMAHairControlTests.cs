#if UNITY_EDITOR

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Editors.Tests
{
    public sealed class UMAHairControlTests
    {
        private const string HairShaderSource =
            "Shader \"Hidden/UMA/Tests/HairControl\" { Properties { _HairCenterWS (\"Center\", Vector) = (0,0,0,0) " +
            "_ID_Map (\"ID Map\", 2D) = \"white\" {} _Other (\"Other\", Float) = 0 } SubShader { Pass { } } }";
        private const string PlainShaderSource =
            "Shader \"Hidden/UMA/Tests/HairControlPlain\" { Properties { _Other (\"Other\", Float) = 0 } SubShader { Pass { } } }";

        private readonly List<Object> created = new List<Object>();
        private Shader hairShader, plainShader;
        private Material hairMaterial, plainMaterial;
        private MaterialPropertyBlock block;

        // A character whose mesh space is Z-up while its root bone is Y-up, as on a UMA.
        private sealed class Rig
        {
            public GameObject avatar;
            public Transform root, head;
            public SkinnedMeshRenderer renderer;
        }

        [SetUp]
        public void SetUp()
        {
            hairShader = Track(ShaderUtil.CreateShaderAsset(HairShaderSource, false));
            plainShader = Track(ShaderUtil.CreateShaderAsset(PlainShaderSource, false));
            hairMaterial = Track(new Material(hairShader));
            // An ID Map as the hair shader expects it: red is white at the scalp (v = 0), green is white at the tip.
            var idMap = Track(new Texture2D(4, 64, TextureFormat.RGBA32, false, true));
            idMap.wrapMode = TextureWrapMode.Clamp;
            idMap.filterMode = FilterMode.Point;
            for (int y = 0; y < 64; y++)
            {
                float v = (y + 0.5f) / 64f;
                for (int x = 0; x < 4; x++)
                {
                    idMap.SetPixel(x, y, new Color(1f - v, v, 0f, 1f));
                }
            }
            idMap.Apply();
            hairMaterial.SetTexture("_ID_Map", idMap);
            UMAHairControl.ForgetStrandMeasurements();
            plainMaterial = Track(new Material(plainShader));
            block = new MaterialPropertyBlock();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.DestroyImmediate(created[i]);
                }
            }
            created.Clear();
        }

        private T Track<T>(T value) where T : Object
        {
            created.Add(value);
            return value;
        }

        private Rig CreateRig(Vector3 position, string headName = "Head", Material hair = null)
        {
            var rig = new Rig();
            rig.avatar = Track(new GameObject("TestAvatar"));
            rig.avatar.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 40f, 0f));

            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(rig.avatar.transform, false);

            rig.root = new GameObject("Global").transform;
            rig.root.SetParent(rig.avatar.transform, false);
            rig.root.localRotation = Quaternion.Euler(90f, 0f, 0f);

            rig.head = new GameObject(headName).transform;
            rig.head.SetParent(rig.root, false);
            rig.head.localPosition = new Vector3(0f, 1.7f, 0.05f);
            rig.head.localRotation = Quaternion.Euler(10f, 200f, 30f);

            var mesh = Track(new Mesh());
            mesh.vertices = new[] { new Vector3(0f, 0f, 1.8f), new Vector3(0.1f, 0f, 1.8f), new Vector3(0f, 0.1f, 1.8f) };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 0, 2, 1 }, 1);
            var weights = new BoneWeight[3];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i].boneIndex0 = 1;
                weights[i].weight0 = 1f;
            }
            mesh.boneWeights = weights;
            Matrix4x4 meshToWorld = rendererObject.transform.localToWorldMatrix;
            mesh.bindposes = new[] { rig.root.worldToLocalMatrix * meshToWorld, rig.head.worldToLocalMatrix * meshToWorld };

            rig.renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            rig.renderer.sharedMesh = mesh;
            rig.renderer.bones = new[] { rig.root, rig.head };
            rig.renderer.rootBone = rig.root;
            rig.renderer.sharedMaterials = new[] { plainMaterial, hair != null ? hair : hairMaterial };
            return rig;
        }

        private Vector4 CenterOn(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.GetVector(UMAHairControl.HairCenterProperty);
        }

        private bool BlockIsEmpty(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.isEmpty;
        }

        private static void AssertPoint(Vector3 expected, Vector4 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), "expected " + expected.ToString("F4") + " but was " + ((Vector3)actual).ToString("F4"));
            Assert.That(actual.w, Is.EqualTo(1f), "w marks the center as valid");
        }

        [Test]
        public void WritesCenterAboveHeadBoneOnlyToMaterialsWithTheProperty()
        {
            Rig rig = CreateRig(new Vector3(3f, 1f, -2f));
            var center = rig.avatar.AddComponent<UMAHairControl>();
            center.centerOffset = new Vector3(0.01f, 0.09f, -0.02f);
            center.UpdateCenter();

            // The offset is in the root bone's space, not in the Z-up mesh space.
            Vector3 expected = rig.head.position + rig.root.TransformVector(center.centerOffset);
            AssertPoint(expected, CenterOn(rig.renderer, 1));
            Assert.That(Vector3.Distance(expected, rig.head.position + rig.renderer.transform.TransformVector(center.centerOffset)), Is.GreaterThan(0.05f),
                "the rig must tell root-bone space and mesh space apart for this test to mean anything");
            Assert.IsTrue(BlockIsEmpty(rig.renderer, 0), "a material without _HairCenterWS is left alone");
            Assert.AreSame(rig.head, center.HeadBone);
            Assert.IsTrue(center.TryGetCenter(out Vector3 reported));
            Assert.That(Vector3.Distance(expected, reported), Is.LessThan(1e-4f));
        }

        [Test]
        public void CenterFollowsHeadPoseAndDoesNotDependOnThePoseItWasSetUpIn()
        {
            Rig rig = CreateRig(Vector3.zero);
            Vector3 offset = new Vector3(0f, 0.09f, 0f);
            Vector3 headLocal = rig.head.InverseTransformVector(rig.root.TransformVector(offset)); // rest pose

            // Pose the head before the component ever sees the character.
            rig.head.localRotation = Quaternion.Euler(-35f, 120f, 75f);
            rig.head.localPosition = new Vector3(0.2f, 1.5f, -0.1f);
            var center = rig.avatar.AddComponent<UMAHairControl>();
            center.centerOffset = offset;
            center.UpdateCenter();
            AssertPoint(rig.head.TransformPoint(headLocal), CenterOn(rig.renderer, 1));

            rig.head.localRotation = Quaternion.Euler(60f, -10f, 5f);
            rig.head.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            rig.avatar.transform.position = new Vector3(-4f, 2f, 9f);
            center.UpdateCenter();
            AssertPoint(rig.head.TransformPoint(headLocal), CenterOn(rig.renderer, 1));
        }

        [Test]
        public void CharactersSharingOneMaterialEachGetTheirOwnCenter()
        {
            Rig first = CreateRig(new Vector3(0f, 0f, 0f));
            Rig second = CreateRig(new Vector3(5f, 0f, 0f));
            Assert.AreSame(first.renderer.sharedMaterials[1], second.renderer.sharedMaterials[1]);
            var a = first.avatar.AddComponent<UMAHairControl>();
            var b = second.avatar.AddComponent<UMAHairControl>();
            a.UpdateCenter();
            b.UpdateCenter();

            AssertPoint(first.head.position + first.root.TransformVector(a.centerOffset), CenterOn(first.renderer, 1));
            AssertPoint(second.head.position + second.root.TransformVector(b.centerOffset), CenterOn(second.renderer, 1));
            Assert.AreEqual(Vector4.zero, hairMaterial.GetVector(UMAHairControl.HairCenterProperty), "the shared material itself is not written");
        }

        [Test]
        public void DisablingRemovesTheCenterAndKeepsValuesOtherCodePutOnTheRenderer()
        {
            Rig clean = CreateRig(Vector3.zero);
            Rig shared = CreateRig(new Vector3(2f, 0f, 0f));
            int other = Shader.PropertyToID("_Other");
            block.Clear();
            block.SetFloat(other, 0.75f);
            shared.renderer.SetPropertyBlock(block, 1);

            var a = clean.avatar.AddComponent<UMAHairControl>();
            var b = shared.avatar.AddComponent<UMAHairControl>();
            a.UpdateCenter();
            b.UpdateCenter();
            shared.renderer.GetPropertyBlock(block, 1);
            Assert.AreEqual(0.75f, block.GetFloat(other), "another component's value survives the write");
            Assert.AreEqual(1f, block.GetVector(UMAHairControl.HairCenterProperty).w);

            a.enabled = false;
            b.enabled = false;
            Assert.IsTrue(BlockIsEmpty(clean.renderer, 1), "a block that was empty before is removed again");
            shared.renderer.GetPropertyBlock(block, 1);
            Assert.AreEqual(0.75f, block.GetFloat(other));
            Assert.AreEqual(0f, block.GetVector(UMAHairControl.HairCenterProperty).w, "w = 0 sends the shader back to the renderer bounds");
        }

        [Test]
        public void MissingHeadBoneWritesNothingAndWarnsOnce()
        {
            Rig rig = CreateRig(Vector3.zero, "Skull");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no bone named 'Head'"));
            var center = rig.avatar.AddComponent<UMAHairControl>();
            center.UpdateCenter();
            center.UpdateCenter();
            Assert.IsTrue(BlockIsEmpty(rig.renderer, 1));
            Assert.IsFalse(center.TryGetCenter(out _));

            center.headBoneName = "Skull";
            center.UpdateCenter();
            AssertPoint(rig.head.position + rig.root.TransformVector(center.centerOffset), CenterOn(rig.renderer, 1));
        }

        // Makes the rig a UMA character whose hair material was built from the given slots.
        private UMAData AddUmaData(Rig rig, params SlotDataAsset[] slots)
        {
            var umaData = rig.avatar.AddComponent<UMAData>();
            var generated = new UMAData.GeneratedMaterial { material = hairMaterial, skinnedMeshRenderer = rig.renderer, materialIndex = 1 };
            for (int i = 0; i < slots.Length; i++)
            {
                generated.materialFragments.Add(new UMAData.MaterialFragment { slotData = new SlotData { asset = slots[i] } });
            }
            umaData.generatedMaterials.materials.Add(generated);
            return umaData;
        }

        private SlotDataAsset CreateSlot(string slotName, bool ready, float start = 0.25f, float angle = 12f)
        {
            var slot = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            slot.name = slotName;
            slot.meshData = CreateCardMeshData();
            slot.subMeshIndex = 0;
            slot.HairControlReady = ready;
            slot.HairControlStart = start;
            slot.HairControlAngle = angle;
            return slot;
        }

        private const float CardLength = 0.12f;

        // One hair card, 4 cm wide and 12 cm long, running along +Y of the mesh. Its vertex color and its UV (v)
        // run from 0 at the scalp end to 1 at the tip. With this normal and tangent the bitangent points to -Y, so
        // the strand runs along minus the bitangent.
        private static UMAMeshData CreateCardMeshData()
        {
            const int rows = 5;
            var data = new UMAMeshData();
            data.vertices = new Vector3[rows * 2];
            data.normals = new Vector3[rows * 2];
            data.tangents = new Vector4[rows * 2];
            data.colors32 = new Color32[rows * 2];
            data.uv = new Vector2[rows * 2];
            var triangles = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                for (int s = 0; s < 2; s++)
                {
                    int i = r * 2 + s;
                    data.vertices[i] = new Vector3(s == 0 ? -0.02f : 0.02f, t * CardLength, 0f);
                    data.normals[i] = new Vector3(0f, 0f, -1f);
                    data.tangents[i] = new Vector4(1f, 0f, 0f, 1f);
                    byte shade = (byte)Mathf.RoundToInt(t * 255f);
                    data.colors32[i] = new Color32(shade, shade, shade, 255);
                    data.uv[i] = new Vector2(s, t);
                }
                if (r < rows - 1)
                {
                    int a = r * 2;
                    triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            data.vertexCount = rows * 2;
            data.subMeshCount = 1;
            data.submeshes = new[] { new SubMeshTriangles(triangles.ToArray()) };
            return data;
        }

        private Vector4 SwayShapeOn(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.GetVector(UMAHairControl.HairSwayShapeProperty);
        }

        [Test]
        public void StrandsAreMeasuredFromTheSlotMesh()
        {
            SlotDataAsset slot = CreateSlot("Hair", true);
            Assert.IsTrue(UMAHairControl.TryMeasureStrands(slot, null, out UMAHairControl.StrandInfo fromColors));
            Assert.That(fromColors.length, Is.EqualTo(CardLength).Within(1e-4f), "the vertex color goes from 0 to 1 over the card's length");
            Assert.That(fromColors.frameDirection.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(fromColors.frameDirection.y, Is.EqualTo(-1f).Within(1e-4f), "scalp to tip is minus the bitangent on this card");
            Assert.That(fromColors.agreement, Is.GreaterThan(0.999f));

            // The same card read through the ID Map, with either channel, gives the same strand.
            foreach (var source in new[] { SlotDataAsset.HairControlSourceType.IDMapRoot, SlotDataAsset.HairControlSourceType.IDMapTip })
            {
                slot.HairControlSource = source;
                Assert.IsTrue(UMAHairControl.TryMeasureStrands(slot, hairMaterial.GetTexture("_ID_Map"), out UMAHairControl.StrandInfo fromMap), source.ToString());
                Assert.That(fromMap.length, Is.EqualTo(CardLength).Within(CardLength * 0.05f), source.ToString());
                Assert.That(fromMap.frameDirection.y, Is.EqualTo(-1f).Within(1e-3f), source.ToString());
            }

            // A slot whose colors do not change has no strands to measure.
            slot.HairControlSource = SlotDataAsset.HairControlSourceType.VertexColor;
            for (int i = 0; i < slot.meshData.colors32.Length; i++)
            {
                slot.meshData.colors32[i] = new Color32(255, 255, 255, 255);
            }
            Assert.IsFalse(UMAHairControl.TryMeasureStrands(slot, null, out _));
        }

        [Test]
        public void ASlotWhoseStrandsCannotBeMeasuredDoesNotSwayAndWarns()
        {
            Rig rig = CreateRig(Vector3.zero);
            SlotDataAsset slot = CreateSlot("FlatColorHair", true);
            for (int i = 0; i < slot.meshData.colors32.Length; i++)
            {
                slot.meshData.colors32[i] = new Color32(255, 255, 255, 255);
            }
            AddUmaData(rig, slot);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'FlatColorHair' does not sway.*could not be measured"));
            var control = AddSteadyControl(rig);
            control.wind = new Vector3(1f, 0f, 0f);
            Simulate(control, 0.5f);
            Assert.AreEqual(Vector4.zero, SwayParamsOn(rig.renderer, 1));
            Assert.AreEqual(Vector3.zero, control.CurrentSway);
        }

        private Vector4 SwayParamsOn(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.GetVector(UMAHairControl.HairSwayParamsProperty);
        }

        private Vector4 SwayOn(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.GetVector(UMAHairControl.HairSwayProperty);
        }

        private static UMAHairControl AddSteadyControl(Rig rig)
        {
            var control = rig.avatar.AddComponent<UMAHairControl>();
            control.useWindZones = false; // the scene the tests run in must not matter
            control.gustiness = 0f;
            return control;
        }

        private static void Simulate(UMAHairControl control, float seconds, System.Action<float> eachStep = null)
        {
            const float step = 1f / 60f;
            for (float t = 0f; t < seconds; t += step)
            {
                eachStep?.Invoke(step);
                control.UpdateHair(step);
            }
        }

        [Test]
        public void NewSlotsAreNotHairControlReady()
        {
            var slot = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            Assert.IsFalse(slot.HairControlReady);
            Assert.AreEqual(SlotDataAsset.HairControlSourceType.VertexColor, slot.HairControlSource);
            Assert.That(slot.HairControlStart, Is.InRange(0f, 1f));
            Assert.That(slot.HairControlAngle, Is.GreaterThan(0f));
        }

        [Test]
        public void SlotValuesReachTheShaderOnlyForSlotsMarkedReady()
        {
            Rig ready = CreateRig(Vector3.zero);
            Rig notReady = CreateRig(new Vector3(3f, 0f, 0f));
            SlotDataAsset readySlot = CreateSlot("ReadyHair", true, 0.4f, 20f);
            AddUmaData(ready, readySlot);
            AddUmaData(notReady, CreateSlot("PlainHair", false, 0.4f, 20f));
            var a = AddSteadyControl(ready);
            var b = AddSteadyControl(notReady);
            a.ripple = 0.2f;
            a.UpdateHair(0f);
            b.UpdateHair(0f);

            Vector4 settings = SwayParamsOn(ready.renderer, 1);
            Assert.That(settings.x, Is.EqualTo(0.4f).Within(1e-6f), "x = vertex color where movement starts");
            Assert.That(settings.y, Is.EqualTo(20f * Mathf.Deg2Rad).Within(1e-6f), "y = angle a strand turns at full sway, in radians");
            Assert.That(settings.z, Is.EqualTo(0.2f).Within(1e-6f), "z = ripple strength");
            Assert.That(settings.w, Is.EqualTo(0f), "w = 0: the place along the strand comes from the vertex color");

            // What was measured from the slot's mesh goes along: the strand length and its direction in the tangent frame.
            Vector4 shape = SwayShapeOn(ready.renderer, 1);
            Assert.That(shape.x, Is.EqualTo(CardLength).Within(1e-4f), "x = length of a strand");
            Assert.That(shape.y, Is.EqualTo(0f).Within(1e-4f), "y = part of the tangent");
            Assert.That(shape.z, Is.EqualTo(-1f).Within(1e-4f), "z = part of the bitangent");
            Assert.AreEqual(Vector4.zero, SwayShapeOn(notReady.renderer, 1));

            // The length is in world units, so it grows with the head bone; a length set on the slot replaces the measured one.
            ready.head.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            a.UpdateHair(0f);
            Assert.That(SwayShapeOn(ready.renderer, 1).x, Is.EqualTo(CardLength * 1.5f).Within(1e-4f));
            readySlot.HairControlLength = 0.3f;
            a.UpdateHair(0f);
            Assert.That(SwayShapeOn(ready.renderer, 1).x, Is.EqualTo(0.45f).Within(1e-4f));
            readySlot.HairControlLength = 0f;
            Assert.AreEqual(Vector4.zero, SwayParamsOn(notReady.renderer, 1), "a slot that is not ready does not move");
            Assert.AreEqual(1f, CenterOn(notReady.renderer, 1).w, "the volume center is still written for it");

            // The slot is read every update, so tuning it while the game runs shows at once.
            readySlot.HairControlAngle = 5f;
            readySlot.HairControlStart = 0.1f;
            a.UpdateHair(0f);
            settings = SwayParamsOn(ready.renderer, 1);
            Assert.That(settings.x, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(settings.y, Is.EqualTo(5f * Mathf.Deg2Rad).Within(1e-6f));

            // The numbers the shader tells the sources apart by.
            readySlot.HairControlSource = SlotDataAsset.HairControlSourceType.IDMapRoot;
            a.UpdateHair(0f);
            Assert.That(SwayParamsOn(ready.renderer, 1).w, Is.EqualTo(1f), "w = 1: red channel of the ID Map, white at the scalp");
            readySlot.HairControlSource = SlotDataAsset.HairControlSourceType.IDMapTip;
            a.UpdateHair(0f);
            Assert.That(SwayParamsOn(ready.renderer, 1).w, Is.EqualTo(2f), "w = 2: green channel of the ID Map, white at the tip");

            a.sway = false;
            a.UpdateHair(0f);
            Assert.AreEqual(Vector4.zero, SwayParamsOn(ready.renderer, 1), "switching sway off on the component stops it");
        }

        [Test]
        public void TheSecondPassOfTwoPassHairGetsTheSameValuesAsTheFirst()
        {
            // UMA draws two-pass hair with a second material slot on the same renderer.
            var secondPass = Track(new Material(hairShader));
            Rig rig = CreateRig(Vector3.zero);
            rig.renderer.sharedMaterials = new[] { secondPass, hairMaterial };
            UMAData umaData = AddUmaData(rig, CreateSlot("Hair", true, 0.3f, 15f));
            umaData.generatedMaterials.materials[0].secondPassMaterial = secondPass;
            var control = AddSteadyControl(rig);
            control.wind = new Vector3(1f, 0f, 0f);
            Simulate(control, 0.5f);

            Vector4 first = SwayParamsOn(rig.renderer, 1);
            Assert.That(first.x, Is.EqualTo(0.3f).Within(1e-6f));
            Assert.That(first.y, Is.EqualTo(15f * Mathf.Deg2Rad).Within(1e-6f));
            Assert.AreEqual(first, SwayParamsOn(rig.renderer, 0), "both passes must move alike, or they come apart");
            Assert.AreEqual(SwayOn(rig.renderer, 1), SwayOn(rig.renderer, 0));
            Assert.That(((Vector3)SwayOn(rig.renderer, 0)).magnitude, Is.GreaterThan(0.1f));
        }

        [Test]
        public void ACharacterThatIsNotAUmaDoesNotSway()
        {
            Rig rig = CreateRig(Vector3.zero);
            var control = AddSteadyControl(rig);
            control.wind = new Vector3(0f, 0f, 2f);
            Simulate(control, 1f);

            Assert.AreEqual(Vector4.zero, SwayParamsOn(rig.renderer, 1));
            Assert.AreEqual(Vector3.zero, control.CurrentSway);
            Assert.AreEqual(1f, CenterOn(rig.renderer, 1).w);
        }

        [Test]
        public void HairTrailsBehindAMovingHeadAndComesToRestWhenItStops()
        {
            Rig rig = CreateRig(Vector3.zero);
            AddUmaData(rig, CreateSlot("Hair", true));
            var control = AddSteadyControl(rig);
            control.movementSway = 0.2f;
            control.UpdateHair(0f);
            Assert.AreEqual(Vector3.zero, control.CurrentSway);

            // Two meters a second along world X.
            Simulate(control, 2f, step => rig.avatar.transform.position += new Vector3(2f * step, 0f, 0f));
            Vector3 moving = control.CurrentSway;
            Assert.That(moving.x, Is.EqualTo(-0.4f).Within(0.05f), "sway = -speed * Movement Sway once it has settled");
            Assert.That(Mathf.Abs(moving.y) + Mathf.Abs(moving.z), Is.LessThan(0.01f));
            Vector4 written = SwayOn(rig.renderer, 1);
            Assert.That(Vector3.Distance(written, moving), Is.LessThan(1e-6f), "the shader gets the sway in world space");

            Simulate(control, 4f);
            Assert.That(control.CurrentSway.magnitude, Is.LessThan(0.01f), "at rest again after the head stops");
        }

        [Test]
        public void WindLeansTheHairAndFullSwayIsTheLimit()
        {
            Rig rig = CreateRig(Vector3.zero);
            AddUmaData(rig, CreateSlot("Hair", true));
            var control = AddSteadyControl(rig);
            control.windSway = 0.5f;
            control.wind = new Vector3(0f, 0f, 1f);
            Simulate(control, 4f);
            Assert.That(Vector3.Distance(control.CurrentSway, new Vector3(0f, 0f, 0.5f)), Is.LessThan(0.02f));

            Vector3 before = UMAHairControl.GlobalWind;
            try
            {
                UMAHairControl.GlobalWind = new Vector3(40f, 0f, 0f);
                float longest = 0f;
                Simulate(control, 3f, step => longest = Mathf.Max(longest, control.CurrentSway.magnitude));
                Assert.That(longest, Is.LessThanOrEqualTo(1f + 1e-4f), "the sway never goes past full sway");
                Assert.That(control.CurrentSway.x, Is.GreaterThan(0.9f));
            }
            finally
            {
                UMAHairControl.GlobalWind = before;
            }
        }

#if UMA_WIND_MODULE
        [Test]
        public void AWindZoneInTheSceneLeansTheHair()
        {
            var zoneObject = Track(new GameObject("Test wind zone"));
            zoneObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
            var zone = zoneObject.AddComponent<WindZone>();
            zone.mode = WindZoneMode.Directional;
            zone.windMain = 0.8f;
            UMAHairControl.RefreshWindZones();
            try
            {
                // The scene the tests run in may have Wind Zones of its own: they count too.
                Vector3 wind = Vector3.zero;
                foreach (WindZone other in Object.FindObjectsByType<WindZone>(FindObjectsSortMode.None))
                {
                    Assume.That(other.mode, Is.EqualTo(WindZoneMode.Directional), "a spherical Wind Zone in the open scene makes the expected value depend on its place");
                    wind += other.transform.forward * other.windMain;
                }

                Rig rig = CreateRig(Vector3.zero);
                AddUmaData(rig, CreateSlot("Hair", true));
                var control = AddSteadyControl(rig);
                control.windSway = 0.5f;
                control.useWindZones = true;
                Simulate(control, 4f);
                Assert.That(Vector3.Distance(control.CurrentSway, Vector3.ClampMagnitude(wind * 0.5f, 1f)), Is.LessThan(0.02f));
                Assert.That(control.CurrentSway.x, Is.GreaterThan(0.05f), "the test zone blows along +X");

                control.useWindZones = false;
                Simulate(control, 4f);
                Assert.That(control.CurrentSway.magnitude, Is.LessThan(0.01f), "without Use Wind Zones the zone is ignored");
            }
            finally
            {
                Object.DestroyImmediate(zoneObject);
                UMAHairControl.RefreshWindZones();
            }
        }
#endif

        private Vector4 SpinOn(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.GetVector(UMAHairControl.HairSpinProperty);
        }

        [Test]
        public void TurningTheCharacterSwingsTheHairTheOtherWayAndStopsWhenItStops()
        {
            Rig rig = CreateRig(Vector3.zero);
            AddUmaData(rig, CreateSlot("Hair", true));
            var control = AddSteadyControl(rig);
            control.turnSway = 0.15f;
            control.movementSway = 0f; // only the turn
            control.UpdateHair(0f);

            // Half a circle a second around world up.
            Simulate(control, 2f, step => rig.avatar.transform.Rotate(0f, 180f * step, 0f, Space.World));
            Vector3 spin = control.CurrentSpin;
            Assert.That(spin.y, Is.EqualTo(-0.15f * Mathf.PI).Within(0.03f), "the lag is the turn rate times Turn Sway, the other way");
            Assert.That(Mathf.Abs(spin.x) + Mathf.Abs(spin.z), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(SpinOn(rig.renderer, 1), spin), Is.LessThan(1e-6f), "the shader gets the lag in world space");

            Simulate(control, 4f);
            Assert.That(control.CurrentSpin.magnitude, Is.LessThan(0.01f), "at rest again after the turn stops");
        }

        [Test]
        public void SettingTheCharacterToANewRotationIsNotATurnOfTheHair()
        {
            Rig rig = CreateRig(Vector3.zero);
            AddUmaData(rig, CreateSlot("Hair", true));
            var control = AddSteadyControl(rig);
            control.movementSway = 0f;
            Simulate(control, 0.5f);

            rig.avatar.transform.Rotate(0f, 170f, 0f, Space.World);
            Simulate(control, 0.5f);
            Assert.That(control.CurrentSpin.magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void ASlotThatIsNotReadyGetsNoTurn()
        {
            Rig rig = CreateRig(Vector3.zero);
            AddUmaData(rig, CreateSlot("PlainHair", false));
            var control = AddSteadyControl(rig);
            Simulate(control, 1f, step => rig.avatar.transform.Rotate(0f, 180f * step, 0f, Space.World));
            Assert.AreEqual(Vector4.zero, SpinOn(rig.renderer, 1));
        }

        [Test]
        public void MovingTheCharacterToAnotherPlaceIsNotAMovementOfTheHair()
        {
            Rig rig = CreateRig(Vector3.zero);
            AddUmaData(rig, CreateSlot("Hair", true));
            var control = AddSteadyControl(rig);
            Simulate(control, 0.5f);

            rig.avatar.transform.position = new Vector3(500f, 0f, -300f);
            Simulate(control, 0.5f);
            Assert.That(control.CurrentSway.magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void AnUnpreparedSlotInTheSameSubmeshStopsTheSwayAndWarnsOnce()
        {
            Rig rig = CreateRig(Vector3.zero);
            SlotDataAsset cap = CreateSlot("HairCap", false);
            AddUmaData(rig, CreateSlot("Hair", true), cap);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("does not sway.*'HairCap'"));
            var control = AddSteadyControl(rig);
            control.wind = new Vector3(1f, 0f, 0f);
            Simulate(control, 0.5f);
            Assert.AreEqual(Vector4.zero, SwayParamsOn(rig.renderer, 1));
            Assert.AreEqual(Vector3.zero, control.CurrentSway);

            cap.HairControlReady = true;
            Simulate(control, 0.5f);
            Assert.That(SwayParamsOn(rig.renderer, 1).y, Is.GreaterThan(0f), "once every slot in the submesh is ready it sways");
        }

        [Test]
        public void DisablingTakesTheSwayOffTheRenderer()
        {
            Rig clean = CreateRig(Vector3.zero);
            Rig shared = CreateRig(new Vector3(2f, 0f, 0f));
            AddUmaData(clean, CreateSlot("Hair", true));
            AddUmaData(shared, CreateSlot("Hair", true));
            int other = Shader.PropertyToID("_Other");
            block.Clear();
            block.SetFloat(other, 0.75f);
            shared.renderer.SetPropertyBlock(block, 1);

            var a = AddSteadyControl(clean);
            var b = AddSteadyControl(shared);
            a.wind = b.wind = new Vector3(1f, 0f, 0f);
            Simulate(a, 0.5f);
            Simulate(b, 0.5f);
            Assert.That(SwayParamsOn(shared.renderer, 1).y, Is.GreaterThan(0f));
            Assert.That(((Vector3)SwayOn(shared.renderer, 1)).magnitude, Is.GreaterThan(0.1f));

            a.enabled = false;
            b.enabled = false;
            Assert.IsTrue(BlockIsEmpty(clean.renderer, 1));
            Assert.AreEqual(Vector4.zero, SwayParamsOn(shared.renderer, 1));
            Assert.AreEqual(Vector4.zero, SwayOn(shared.renderer, 1));
            shared.renderer.GetPropertyBlock(block, 1);
            Assert.AreEqual(0.75f, block.GetFloat(other), "another component's value is kept");
        }

        [Test]
        public void MaterialsReplacedByARebuildArePickedUp()
        {
            Rig rig = CreateRig(Vector3.zero);
            var center = rig.avatar.AddComponent<UMAHairControl>();
            center.UpdateCenter();

            // A rebuild that moves the hair to the other slot, with a new material instance.
            var rebuilt = Track(new Material(hairShader));
            rig.renderer.sharedMaterials = new[] { rebuilt, plainMaterial };
            center.UpdateCenter();

            AssertPoint(rig.head.position + rig.root.TransformVector(center.centerOffset), CenterOn(rig.renderer, 0));
            Assert.IsTrue(BlockIsEmpty(rig.renderer, 1), "the slot that no longer holds hair is cleared");
        }
    }
}

#endif

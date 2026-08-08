#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA;
using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class DynamicExpressionPlayerTests
    {
        [Serializable]
        private sealed class CountingBuildEffect : DNAEffect
        {
            public int afterRecipe;
            public int preApply;
            public int apply;
            public int postApply;
            public float lastValue;
            public DNAInstanceCollection.DNABuildType buildType =
                DNAInstanceCollection.DNABuildType.Texture;

            public override DNAInstanceCollection.DNABuildType AreaEffect =>
                buildType;
            public override ExpressionEffectPhase ExpressionPhases =>
                ExpressionEffectPhase.BuildAfterRecipe |
                ExpressionEffectPhase.BuildPreApply |
                ExpressionEffectPhase.BuildApply |
                ExpressionEffectPhase.BuildPostApply;
            public override string Description => "Test effect";

            public override void AfterRecipeGenerated(UMAData avatar,
                DNA dna, float value)
            {
                afterRecipe++;
                lastValue = value;
            }

            public override void PreApply(UMAData avatar, DNA dna,
                float value)
            {
                preApply++;
                lastValue = value;
            }

            public override void Apply(UMAData avatar, DNA dna, float value)
            {
                apply++;
                lastValue = value;
            }

            public override void PostApply(UMAData avatar, DNA dna,
                float value)
            {
                postApply++;
                lastValue = value;
            }
        }

        [Serializable]
        private sealed class UnsupportedEffect : DNAEffect
        {
            public override string Description => "Unsupported test effect";
        }

        private readonly List<UnityEngine.Object> _objects =
            new List<UnityEngine.Object>();
        private readonly List<string> _assetFolders = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; i++)
                if (_objects[i] != null)
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            for (int i = 0; i < _assetFolders.Count; i++)
                if (AssetDatabase.IsValidFolder(_assetFolders[i]))
                    AssetDatabase.DeleteAsset(_assetFolders[i]);
            _assetFolders.Clear();
            AssetDatabase.Refresh();
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void SourcePriorityBatchingAndStableIdsAreDeterministic()
        {
            DNA dna = NewDNA("smile", 0.5f);
            UMAExpressionGroup group = NewGroup(
                Definition("Smile", dna, blend: ExpressionBlendMode.Override));
            DynamicExpressionPlayer player = NewPlayer(group, out _);

            int changes = 0;
            player.ExpressionChangedAction += _ => changes++;
            player.BeginExpressionBatch();
            Assert.IsTrue(player.SetExpression("smile", 0.2f,
                ExpressionSource.Animation));
            Assert.IsTrue(player.SetExpression("SMILE", 0.8f,
                ExpressionSource.Manual));
            Assert.AreEqual(0, changes);
            player.EndExpressionBatch();

            Assert.IsTrue(player.TryGetExpression("Smile", out float value));
            Assert.AreEqual(0.8f, value, 0.0001f);
            Assert.AreEqual(1, changes);

            player.ResetExpression("smile", ExpressionSource.Manual);
            player.TryGetExpression("smile", out value);
            Assert.AreEqual(0.2f, value, 0.0001f);
            player.ResetExpression("smile", ExpressionSource.Animation);
            player.TryGetExpression("smile", out value);
            Assert.AreEqual(0.5f, value, 0.0001f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void SharedGroupDoesNotShareAvatarRuntimeValues()
        {
            DNA dna = NewDNA("shared", 0.5f);
            UMAExpressionGroup group =
                NewGroup(Definition("shared", dna));
            DynamicExpressionPlayer first = NewPlayer(group, out _);
            DynamicExpressionPlayer second = NewPlayer(group, out _);
            first.SetExpression("shared", 0.1f);
            second.SetExpression("shared", 0.9f);

            first.TryGetExpression("shared", out float firstValue);
            second.TryGetExpression("shared", out float secondValue);
            Assert.AreEqual(0.1f, firstValue, 0.0001f);
            Assert.AreEqual(0.9f, secondValue, 0.0001f);
            Assert.AreEqual(0.5f, dna.defaultValue, 0.0001f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void GroupChangeRetainsOnlyMatchingStableIds()
        {
            UMAExpressionGroup firstGroup = NewGroup(
                Definition("common", NewDNA("firstCommon", 0.5f)),
                Definition("old", NewDNA("oldOnly", 0.5f)));
            DynamicExpressionPlayer player =
                NewPlayer(firstGroup, out _);
            player.SetExpression("common", 0.8f);
            player.SetExpression("old", 0.9f);

            UMAExpressionGroup secondGroup = NewGroup(
                Definition("common", NewDNA("secondCommon", 0.25f)),
                Definition("new", NewDNA("newOnly", 0.4f)));
            player.expressionGroupOverride = secondGroup;
            player.Rebind();

            Assert.IsTrue(player.TryGetExpression("common",
                out float retained));
            Assert.AreEqual(0.8f, retained, 0.0001f);
            Assert.IsFalse(player.TryGetExpression("old", out _));
            Assert.IsTrue(player.TryGetExpression("new",
                out float newValue));
            Assert.AreEqual(0.4f, newValue, 0.0001f);
        }

        [Test]
        public void RebindResolvesRaceAfterRaceSetterCacheIsLost()
        {
            FieldInfo indexerField = typeof(UMAAssetIndexer).GetField(
                "theIndexer",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(indexerField);

            object originalIndexer = indexerField.GetValue(null);
            UMAAssetIndexer testIndexer =
                Track(ScriptableObject.CreateInstance<UMAAssetIndexer>());
            RaceData race = Track(ScriptableObject.CreateInstance<RaceData>());
            UMAExpressionGroup group = NewGroup(
                Definition("smile", NewDNA("Smile", 0.5f)));
            GameObject avatarObject =
                Track(new GameObject("RaceLookupExpressionAvatar"));

            try
            {
                race.name = "ExpressionRace_" +
                    Guid.NewGuid().ToString("N");
                race.expressionGroup = group;
                indexerField.SetValue(null, testIndexer);
                testIndexer.AddAsset(typeof(RaceData), race.raceName,
                    string.Empty, race);

                DynamicCharacterAvatar avatar =
                    avatarObject.AddComponent<DynamicCharacterAvatar>();
                avatar.activeRace.name = race.raceName;
                Assert.IsNull(avatar.activeRace.racedata,
                    "The test requires an empty nonserialized race cache.");

                DynamicExpressionPlayer player =
                    avatarObject.AddComponent<DynamicExpressionPlayer>();
                player.EnableBlinking = false;
                player.EnableSaccades = false;
                player.EnableLookAt = false;
                player.Rebind();

                Assert.AreSame(race, avatar.activeRace.racedata);
                Assert.AreSame(group, player.ResolvedGroup);
                Assert.AreEqual(1, player.ExpressionCount);
                Assert.AreEqual("smile", player.GetExpressionId(0));
            }
            finally
            {
                indexerField.SetValue(null, originalIndexer);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void AdditiveAndMaximumSourcesUseDefinitionBlendMode()
        {
            DNA additiveDNA = NewDNA("additive", 0.5f);
            DNA maximumDNA = NewDNA("maximum", 0.25f);
            UMAExpressionGroup group = NewGroup(
                Definition("add", additiveDNA,
                    blend: ExpressionBlendMode.Additive),
                Definition("max", maximumDNA,
                    blend: ExpressionBlendMode.Maximum));
            DynamicExpressionPlayer player = NewPlayer(group, out _);

            player.BeginExpressionBatch();
            player.SetExpression("add", 0.75f, ExpressionSource.Animation);
            player.SetExpression("add", 0.65f, ExpressionSource.Manual);
            player.SetExpression("max", 0.4f, ExpressionSource.Animation);
            player.SetExpression("max", 0.8f,
                ExpressionSource.ProceduralBlink);
            player.EndExpressionBatch();

            player.TryGetExpression("add", out float additive);
            player.TryGetExpression("max", out float maximum);
            Assert.AreEqual(0.9f, additive, 0.0001f);
            Assert.AreEqual(0.8f, maximum, 0.0001f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ResponseTimeSmoothsInsteadOfSnapping()
        {
            DNA dna = NewDNA("smooth", 0.5f);
            UMAExpressionDefinition definition = Definition("smooth", dna);
            definition.responseTime = 0.2f;
            DynamicExpressionPlayer player = NewPlayer(
                NewGroup(definition), out _);

            player.SetExpression("smooth", 1f);
            player.TryGetExpression("smooth", out float before);
            Assert.AreEqual(0.5f, before, 0.0001f);
            player.AdvanceExpressionSmoothing(0.1f);
            player.TryGetExpression("smooth", out float after);
            Assert.Greater(after, 0.5f);
            Assert.Less(after, 1f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void OverlappingRigEffectsRestoreOnceAndLayerInPriorityOrder()
        {
            const string boneName = "ExpressionBone";
            DNA first = NewDNA("first", 0.5f);
            first.effects.Add(new DNAEffect_BoneRotate
            {
                BoneName = boneName,
                RotationAxis = Vector3.up,
                RotationAngle = 20f
            });
            DNA second = NewDNA("second", 0.5f);
            second.effects.Add(new DNAEffect_BoneRotate
            {
                BoneName = boneName,
                RotationAxis = Vector3.up,
                RotationAngle = 30f
            });
            UMAExpressionGroup group = NewGroup(
                Definition("first", first, priority: 0),
                Definition("second", second, priority: 1));
            DynamicExpressionPlayer player = NewPlayer(group,
                out UMAData data, boneName);
            player.SetExpression("first", 1f);
            player.SetExpression("second", 1f);

            player.ApplyRigExpressionsNow();
            int hash = UMAUtils.StringToHash(boneName);
            Assert.AreEqual(50f,
                Quaternion.Angle(Quaternion.identity,
                    data.skeleton.GetRotation(hash)), 0.01f);

            // A second pass must not accumulate to 100 degrees.
            player.ApplyRigExpressionsNow();
            Assert.AreEqual(50f,
                Quaternion.Angle(Quaternion.identity,
                    data.skeleton.GetRotation(hash)), 0.01f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void GenericJointPolicySuppressesOnlyProtectedBone()
        {
            const string boneName = "GenericJaw";
            DNA dna = NewDNA("jaw", 0.5f);
            dna.effects.Add(new DNAEffect_BoneRotate
            {
                BoneName = boneName,
                RotationAxis = Vector3.right,
                RotationAngle = 30f
            });
            UMAExpressionGroup group = NewGroup(
                Definition("jaw", dna, joints: ExpressionJoint.Jaw));
            DynamicExpressionPlayer player = NewPlayer(group,
                out UMAData data, boneName);
            player.genericBoneJoints.Add(
                new DynamicExpressionPlayer.ExpressionBoneJoint
                {
                    boneName = boneName,
                    joint = ExpressionJoint.Jaw
                });
            player.overrideMecanimJaw = false;
            player.Rebind();
            player.SetExpression("jaw", 1f);

            player.ApplyRigExpressionsNow();
            Assert.AreEqual(0f,
                Quaternion.Angle(Quaternion.identity,
                    data.skeleton.GetRotation(
                        UMAUtils.StringToHash(boneName))), 0.001f);

            player.overrideMecanimJaw = true;
            player.Rebind();
            player.ApplyRigExpressionsNow();
            Assert.AreEqual(30f,
                Quaternion.Angle(Quaternion.identity,
                    data.skeleton.GetRotation(
                        UMAUtils.StringToHash(boneName))), 0.01f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void RigLanesRestoreBeforeAndLayerAfterSimulatedAnimation()
        {
            const string boneName = "AnimatedJaw";
            int hash = UMAUtils.StringToHash(boneName);
            DNA dna = NewDNA("jaw", 0.5f);
            dna.effects.Add(new DNAEffect_BoneRotate
            {
                BoneName = boneName,
                RotationAxis = Vector3.forward,
                RotationAngle = 30f
            });
            DynamicExpressionPlayer player = NewPlayer(
                NewGroup(Definition("jaw", dna,
                    joints: ExpressionJoint.Jaw)),
                out UMAData data, boneName);
            player.SetExpression("jaw", 1f);

            player.overrideMecanimJaw = false;
            player.RestoreRigExpressionsNow();
            data.skeleton.SetRotation(hash,
                Quaternion.Euler(0f, 0f, 12f));
            player.ApplyRigExpressionsAfterAnimationNow();
            Assert.AreEqual(12f, Quaternion.Angle(Quaternion.identity,
                data.skeleton.GetRotation(hash)), 0.01f);

            player.overrideMecanimJaw = true;
            player.RestoreRigExpressionsNow();
            data.skeleton.SetRotation(hash,
                Quaternion.Euler(0f, 0f, 12f));
            player.ApplyRigExpressionsAfterAnimationNow();
            Assert.AreEqual(42f, Quaternion.Angle(Quaternion.identity,
                data.skeleton.GetRotation(hash)), 0.01f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ProceduralRolesUseArbitraryIdsAndDefinitionNeutralValues()
        {
            DNA blinkDNA = NewDNA("blinkDNA", 0.2f);
            UMAExpressionDefinition blink =
                Definition("not_a_legacy_blink_name", blinkDNA);
            blink.roles = ExpressionRole.BlinkLeft |
                ExpressionRole.BlinkRight;
            blink.blinkClosedValue = 1f;

            DNA horizontalDNA = NewDNA("horizontalDNA", 0.3f);
            UMAExpressionDefinition horizontal =
                Definition("stylized_eye_x", horizontalDNA);
            horizontal.roles = ExpressionRole.EyeHorizontal;
            DNA verticalDNA = NewDNA("verticalDNA", 0.7f);
            UMAExpressionDefinition vertical =
                Definition("stylized_eye_y", verticalDNA);
            vertical.roles = ExpressionRole.EyeVertical;

            DynamicExpressionPlayer player = NewPlayer(
                NewGroup(blink, horizontal, vertical), out _);
            player.SetProceduralBlinkAmount(1f);
            player.TryGetExpression(blink.id, out float blinkClosed);
            Assert.AreEqual(1f, blinkClosed, 0.0001f);
            player.SetProceduralBlinkAmount(0f);
            player.TryGetExpression(blink.id, out float blinkOpen);
            Assert.AreEqual(0.2f, blinkOpen, 0.0001f);

            player.SetProceduralGazeDirection(new Vector2(-1f, 0.5f));
            player.TryGetExpression(horizontal.id, out float horizontalValue);
            player.TryGetExpression(vertical.id, out float verticalValue);
            Assert.AreEqual(0f, horizontalValue, 0.0001f);
            Assert.AreEqual(0.85f, verticalValue, 0.0001f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ImmediateBlendshapeAndMaterialEffectsApplyWithoutBuild()
        {
            GameObject avatar = Track(new GameObject("Avatar"));
            UMAData data = avatar.AddComponent<UMAData>();
            GameObject rendererObject = Track(new GameObject("Face"));
            rendererObject.transform.SetParent(avatar.transform);
            SkinnedMeshRenderer renderer =
                rendererObject.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = Track(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            Vector3[] deltas =
                { Vector3.up, Vector3.zero, Vector3.zero };
            mesh.AddBlendShapeFrame("Smile", 100f, deltas,
                new Vector3[3], new Vector3[3]);
            renderer.sharedMesh = mesh;
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Material material = Track(new Material(shader));
            renderer.sharedMaterial = material;
            data.SetRenderers(new[] { renderer });
            int unrelatedId = Shader.PropertyToID("_UnrelatedExpressionTest");
            MaterialPropertyBlock unrelated = new MaterialPropertyBlock();
            unrelated.SetFloat(unrelatedId, 4.25f);
            renderer.SetPropertyBlock(unrelated, 0);
            Texture2D zeroTexture = Track(new Texture2D(1, 1));
            Texture2D oneTexture = Track(new Texture2D(1, 1));

            DNA dna = NewDNA("render", 0f);
            dna.effects.Add(new DNAEffect_BlendShape
            {
                BlendShapeName = "Smile",
                minMapping = 0f,
                maxMapping = 1f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            });
            dna.effects.Add(new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = "_ExpressionTest",
                parameterType =
                    DNAEffect_RuntimeMaterialProperty.ParameterType.Float,
                zeroFloatValue = 0f,
                oneFloatValue = 2f,
                minMapping = 0f,
                maxMapping = 1f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            });
            dna.effects.Add(new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = "_ExpressionColor",
                parameterType =
                    DNAEffect_RuntimeMaterialProperty.ParameterType.Color,
                zeroColorValue = Color.black,
                oneColorValue = new Color(0.2f, 0.4f, 0.6f, 0.8f),
                minMapping = 0f,
                maxMapping = 1f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            });
            dna.effects.Add(new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = "_ExpressionVector",
                parameterType =
                    DNAEffect_RuntimeMaterialProperty.ParameterType.Vector,
                zeroVectorValue = Vector4.zero,
                oneVectorValue = new Vector4(2f, 4f, 6f, 8f),
                minMapping = 0f,
                maxMapping = 1f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            });
            dna.effects.Add(new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = "_ExpressionTexture",
                parameterType =
                    DNAEffect_RuntimeMaterialProperty.ParameterType.Texture,
                zeroTextureValue = zeroTexture,
                oneTextureValue = oneTexture,
                minMapping = 0f,
                maxMapping = 1f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
            });
            UMAExpressionGroup group = NewGroup(Definition("render", dna));
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            player.expressionGroupOverride = group;
            player.Rebind();
            player.SetExpression("render", 0.75f);
            player.EvaluateExpressionsNow();

            Assert.AreEqual(75f, renderer.GetBlendShapeWeight(0), 0.001f);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            Assert.AreEqual(1.5f,
                block.GetFloat(Shader.PropertyToID("_ExpressionTest")),
                0.001f);
            Color expectedColor = Color.Lerp(Color.black,
                new Color(0.2f, 0.4f, 0.6f, 0.8f), 0.75f);
            Color actualColor =
                block.GetColor(Shader.PropertyToID("_ExpressionColor"));
            Assert.AreEqual(expectedColor.r, actualColor.r, 0.0001f);
            Assert.AreEqual(expectedColor.g, actualColor.g, 0.0001f);
            Assert.AreEqual(expectedColor.b, actualColor.b, 0.0001f);
            Assert.AreEqual(expectedColor.a, actualColor.a, 0.0001f);
            Assert.AreEqual(new Vector4(1.5f, 3f, 4.5f, 6f),
                block.GetVector(Shader.PropertyToID("_ExpressionVector")));
            Assert.AreSame(oneTexture,
                block.GetTexture(Shader.PropertyToID("_ExpressionTexture")));
            Assert.AreEqual(4.25f, block.GetFloat(unrelatedId), 0.001f);
            Assert.IsFalse(player.HasPendingBuild);

            GameObject replacementObject =
                Track(new GameObject("ReplacementFace"));
            replacementObject.transform.SetParent(avatar.transform);
            SkinnedMeshRenderer replacement =
                replacementObject.AddComponent<SkinnedMeshRenderer>();
            replacement.sharedMesh = mesh;
            replacement.sharedMaterial = material;
            data.SetRenderers(new[] { replacement });
            data.CharacterUpdated.Invoke(data);

            Assert.AreEqual(75f,
                replacement.GetBlendShapeWeight(0), 0.001f);
            replacement.GetPropertyBlock(block, 0);
            Assert.AreEqual(1.5f,
                block.GetFloat(Shader.PropertyToID("_ExpressionTest")),
                0.001f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void BuildFlagsAggregateAndChangesDuringBuildCoalesce()
        {
            CountingBuildEffect textureEffect = new CountingBuildEffect();
            CountingBuildEffect meshEffect = new CountingBuildEffect
            {
                buildType =
                    DNAInstanceCollection.DNABuildType.MeshModifiers
            };
            DNA textureDNA = NewDNA("texture", 0.5f);
            textureDNA.effects.Add(textureEffect);
            DNA meshDNA = NewDNA("mesh", 0.5f);
            meshDNA.effects.Add(meshEffect);
            DynamicExpressionPlayer player = NewPlayer(
                NewGroup(
                    Definition("texture", textureDNA),
                    Definition("mesh", meshDNA)),
                out UMAData data);

            player.BeginExpressionBatch();
            player.SetExpression("texture", 0.7f);
            player.SetExpression("mesh", 0.8f);
            player.EndExpressionBatch();
            Assert.AreEqual(
                DNAInstanceCollection.DNABuildType.Texture |
                DNAInstanceCollection.DNABuildType.MeshModifiers,
                player.PendingBuildType);

            data.CharacterBegun.Invoke(data);
            Assert.IsTrue(player.HasPendingBuild);
            player.SetExpression("texture", 0.9f);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture,
                player.PendingBuildType);
            data.CharacterUpdated.Invoke(data);
            Assert.IsTrue(player.HasPendingBuild);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture,
                player.PendingBuildType);

            data.CharacterBegun.Invoke(data);
            data.CharacterUpdated.Invoke(data);
            Assert.IsFalse(player.HasPendingBuild);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.None,
                player.PendingBuildType);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void IndexedNoChangeSetterDoesNotAllocate()
        {
            DynamicExpressionPlayer player = NewPlayer(
                NewGroup(Definition("steady",
                    NewDNA("steady", 0.5f))), out _);
            Assert.IsTrue(player.TryGetExpressionIndex("steady",
                out int index));
            player.SetExpression(index, 0.75f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                player.SetExpression(index, 0.75f);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, after - before);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void TransientProviderRunsOnlyDeclaredBuildPhases()
        {
            CountingBuildEffect effect = new CountingBuildEffect();
            DNA dna = NewDNA("build", 0.5f);
            dna.effects.Add(effect);
            UMAExpressionGroup group = NewGroup(Definition("build", dna));
            DynamicExpressionPlayer player = NewPlayer(group,
                out UMAData data);
            player.SetExpression("build", 0.9f);

            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture,
                player.AfterRecipeGenerated(
                    data as UMA.CharacterSystem.DynamicCharacterAvatar));
            // A plain UMAData is valid for all remaining provider phases.
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture,
                player.PreApply(data));
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture,
                player.Apply(data));
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture,
                player.PostApply(data));

            Assert.AreEqual(1, effect.afterRecipe);
            Assert.AreEqual(1, effect.preApply);
            Assert.AreEqual(1, effect.apply);
            Assert.AreEqual(1, effect.postApply);
            Assert.AreEqual(0.9f, effect.lastValue, 0.0001f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void DisabledEffectsNeverExecuteAndAllIncludesMeshModifiers()
        {
            CountingBuildEffect effect = new CountingBuildEffect
            {
                enabled = false
            };
            DNA dna = NewDNA("disabled", 0.5f);
            dna.effects.Add(effect);
            DynamicExpressionPlayer player = NewPlayer(
                NewGroup(Definition("disabled", dna)), out UMAData data);
            player.SetExpression("disabled", 1f);

            Assert.AreEqual(DNAInstanceCollection.DNABuildType.None,
                player.PreApply(data));
            Assert.Zero(effect.preApply);
            Assert.AreNotEqual(0,
                DNAInstanceCollection.DNABuildType.All &
                DNAInstanceCollection.DNABuildType.MeshModifiers);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void BuiltInEffectsDeclareTheirSupportedExecutionLanes()
        {
            ExpressionEffectPhase rig =
                ExpressionEffectPhase.EarlyRestore |
                ExpressionEffectPhase.LateRig;
            AssertPhase(new DNAEffect_BonePose(), rig);
            AssertPhase(new DNAEffect_BoneRotate(), rig);
            AssertPhase(new DNAEffect_BoneTranslate(), rig);
            AssertPhase(new DNAEffect_BoneScale(), rig);
            AssertPhase(new DNAEffect_BoneTransform(), rig);
            AssertPhase(new DNAEffect_BlendShape(),
                ExpressionEffectPhase.LateBlendShape);
            AssertPhase(new DNAEffect_RuntimeMaterialProperty(),
                ExpressionEffectPhase.RuntimeMaterial);
            AssertPhase(new DNAEffect_SharedColor(),
                ExpressionEffectPhase.BuildAfterRecipe);
            AssertPhase(new DNAEffect_SharedColorChannel(),
                ExpressionEffectPhase.BuildAfterRecipe);
            AssertPhase(new DNAEffect_SharedColorProperty(),
                ExpressionEffectPhase.BuildAfterRecipe);
            AssertPhase(new DNAEffect_OverlayUVTransform(),
                ExpressionEffectPhase.BuildAfterRecipe);
            AssertPhase(new DNAEffect_MeshModifier(),
                ExpressionEffectPhase.BuildAfterRecipe);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ValidationRejectsIdentityAndUnsupportedPhaseProblems()
        {
            DNA unsupportedDNA = NewDNA("unsupported", 0.5f);
            unsupportedDNA.effects.Add(new UnsupportedEffect());
            UMAExpressionGroup group = NewGroup(
                Definition("duplicate", unsupportedDNA),
                Definition("DUPLICATE", unsupportedDNA));
            List<ExpressionValidationMessage> messages =
                new List<ExpressionValidationMessage>();

            Assert.IsFalse(group.Validate(messages));
            Assert.IsTrue(messages.Exists(m =>
                m.message.Contains("Duplicate expression ID")));
            Assert.IsTrue(messages.Exists(m =>
                m.message.Contains("does not declare an expression phase")));
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ConverterCreatesExactNeutralPrimaryAndInverseCurves()
        {
            UMAExpressionSet set =
                Track(ScriptableObject.CreateInstance<UMAExpressionSet>());
            set.name = "Legacy";
            UMABonePose primary = NewPose("Jaw", 20f);
            UMABonePose inverse = NewPose("Jaw", -10f);
            set.posePairs[6] = new UMAExpressionSet.PosePair
            {
                primary = primary,
                inverse = inverse
            };

            UMAExpressionSetConverter.ConversionResult result =
                UMAExpressionSetConverter.ConvertInMemory(set);
            TrackConversion(result);
            Assert.AreEqual(ExpressionPlayer.PoseCount,
                result.group.expressions.Count);
            UMAExpressionDefinition jaw = result.group.expressions[6];
            Assert.AreEqual("jawOpen_Close", jaw.id);
            Assert.AreEqual(ExpressionJoint.Jaw, jaw.affectedJoints);
            Assert.AreEqual(0.5f, jaw.dna.defaultValue);
            Assert.AreEqual(2, jaw.dna.effects.Count);

            DNAEffect_BonePose primaryEffect =
                (DNAEffect_BonePose)jaw.dna.effects[0];
            DNAEffect_BonePose inverseEffect =
                (DNAEffect_BonePose)jaw.dna.effects[1];
            Assert.AreEqual(0f, primaryEffect.curve.Evaluate(0f), 0.0001f);
            Assert.AreEqual(0f, primaryEffect.curve.Evaluate(0.5f), 0.0001f);
            Assert.AreEqual(1f, primaryEffect.curve.Evaluate(1f), 0.0001f);
            Assert.AreEqual(1f, inverseEffect.curve.Evaluate(0f), 0.0001f);
            Assert.AreEqual(0f, inverseEffect.curve.Evaluate(0.5f), 0.0001f);
            Assert.AreEqual(0f, inverseEffect.curve.Evaluate(1f), 0.0001f);
            Assert.AreEqual(ExpressionRole.BlinkLeft,
                result.group.expressions[26].roles);
            Assert.AreEqual(ExpressionRole.EyeHorizontalRight,
                result.group.expressions[31].roles);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void LegacyRaceSetAutomaticallyBuildsTransientDefinitions()
        {
            const string boneName = "LegacyRuntimeJaw";
            UMAExpressionSet set =
                Track(ScriptableObject.CreateInstance<UMAExpressionSet>());
            set.name = "RuntimeLegacy";
            set.posePairs[6] = new UMAExpressionSet.PosePair
            {
                primary = NewPose(boneName, 18f),
                inverse = NewPose(boneName, -9f)
            };
            RaceData race = Track(ScriptableObject.CreateInstance<RaceData>());
            race.expressionSet = set;

            GameObject avatar = Track(new GameObject("LegacyAvatar"));
            UMAData data = avatar.AddComponent<UMAData>();
            data.umaRecipe = new UMAData.UMARecipe { raceData = race };
            GameObject bone = new GameObject(boneName);
            bone.transform.SetParent(avatar.transform, false);
            data.skeleton = new UMASkeleton(avatar.transform);
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            player.EnableBlinking = false;
            player.EnableSaccades = false;
            player.EnableLookAt = false;
            player.Rebind();

            Assert.IsNull(player.ResolvedGroup);
            Assert.AreEqual(ExpressionPlayer.PoseCount,
                player.ExpressionCount);
            Assert.IsTrue(player.SetExpression("jawOpen_Close", 1f));
            player.ApplyRigExpressionsNow();
            Assert.AreEqual(18f,
                Quaternion.Angle(Quaternion.identity,
                    data.skeleton.GetRotation(
                        UMAUtils.StringToHash(boneName))), 0.01f);
            player.SetExpression("jawOpen_Close", 0f);
            player.ApplyRigExpressionsNow();
            Assert.AreEqual(9f,
                Quaternion.Angle(Quaternion.identity,
                    data.skeleton.GetRotation(
                        UMAUtils.StringToHash(boneName))), 0.01f);
            Assert.AreSame(set, race.expressionSet);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ConvertedLegacyValuesApplyPrimaryAndInversePoses()
        {
            const string boneName = "Jaw";
            UMAExpressionSet set =
                Track(ScriptableObject.CreateInstance<UMAExpressionSet>());
            set.name = "Legacy";
            set.posePairs[6] = new UMAExpressionSet.PosePair
            {
                primary = NewPose(boneName, 20f),
                inverse = NewPose(boneName, -10f)
            };
            UMAExpressionSetConverter.ConversionResult result =
                UMAExpressionSetConverter.ConvertInMemory(set);
            TrackConversion(result);
            DynamicExpressionPlayer player = NewPlayer(result.group,
                out UMAData data, boneName);
            DynamicExpressionLegacyAdapter adapter =
                player.gameObject.AddComponent<
                    DynamicExpressionLegacyAdapter>();
            adapter.target = player;
            adapter.ExpressionChanged = new UMAExpressionEvent();
            string legacyEventId = null;
            float legacyEventValue = 0f;
            adapter.ExpressionChanged.AddListener((_, id, value) =>
            {
                legacyEventId = id;
                legacyEventValue = value;
            });

            adapter.jawOpen_Close = 1f;
            adapter.ForwardValues();
            Assert.AreEqual("jawOpen_Close", legacyEventId);
            Assert.AreEqual(1f, legacyEventValue, 0.0001f);
            Assert.IsTrue(player.TryGetExpressionIndex(
                "jawOpen_Close", out int jawIndex));
            Assert.IsTrue(player.TryGetExpression(jawIndex,
                out float forwardedPrimary));
            Assert.AreEqual(1f, forwardedPrimary, 0.0001f);
            Assert.AreNotEqual(ExpressionEffectPhase.None,
                player.GetExpressionPhases(jawIndex) &
                ExpressionEffectPhase.LateRig);
            Assert.AreEqual(1f,
                ((DNAEffect_BonePose)
                 result.group.expressions[6].dna.effects[0])
                    .GetMappedValue(forwardedPrimary), 0.0001f);
            player.ApplyRigExpressionsNow();
            int hash = UMAUtils.StringToHash(boneName);
            Assert.IsTrue(data.skeleton.HasBone(hash));
            Quaternion primary = data.skeleton.GetRotation(hash);
            Assert.AreEqual(20f,
                Quaternion.Angle(Quaternion.identity, primary), 0.01f);

            adapter.jawOpen_Close = -1f;
            adapter.ForwardValues();
            player.ApplyRigExpressionsNow();
            Quaternion inverse = data.skeleton.GetRotation(hash);
            Assert.AreEqual(10f,
                Quaternion.Angle(Quaternion.identity, inverse), 0.01f);
            Assert.Less(Vector3.Dot(primary * Vector3.up,
                inverse * Vector3.up), 1f);
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void LegacyAdapterUsesDedicatedInspector()
        {
            GameObject avatar =
                Track(new GameObject("LegacyAdapterInspectorAvatar"));
            avatar.AddComponent<DynamicExpressionPlayer>();
            DynamicExpressionLegacyAdapter adapter =
                avatar.AddComponent<DynamicExpressionLegacyAdapter>();
            Editor editor = Track(Editor.CreateEditor(adapter));

            Assert.IsNotNull(editor);
            Assert.IsInstanceOf<DynamicExpressionLegacyAdapterInspector>(
                editor);
            Assert.IsNotNull(
                editor.serializedObject.FindProperty("target"));
            Assert.IsNull(
                editor.serializedObject.FindProperty("expressionSet"));
        }

        [Test]
        [Category("UMA")]
        [Category("DynamicExpression")]
        public void ConvertedAssetsRoundTripSerializeAllDNAReferences()
        {
            UMAExpressionSet set =
                Track(ScriptableObject.CreateInstance<UMAExpressionSet>());
            set.name = "RoundTripLegacy";
            string folder = "Assets/__UMAExpressionTests_" +
                Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",
                folder.Substring("Assets/".Length));
            _assetFolders.Add(folder);
            UMABonePose pose =
                ScriptableObject.CreateInstance<UMABonePose>();
            pose.poses = Array.Empty<UMABonePose.PoseBone>();
            AssetDatabase.CreateAsset(pose, folder + "/BlinkPose.asset");
            set.posePairs[26] = new UMAExpressionSet.PosePair
            {
                inverse = pose
            };
            UMAExpressionSetConverter.ConversionResult result =
                UMAExpressionSetConverter.ConvertToAssets(set, folder);
            string path = AssetDatabase.GetAssetPath(result.group);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            UMAExpressionGroup loaded =
                AssetDatabase.LoadAssetAtPath<UMAExpressionGroup>(path);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(ExpressionPlayer.PoseCount, loaded.Count);
            Assert.IsNotNull(loaded.expressions[26].dna);
            Assert.AreEqual(1, loaded.expressions[26].dna.effects.Count);
            Assert.IsInstanceOf<DNAEffect_BonePose>(
                loaded.expressions[26].dna.effects[0]);
            Assert.AreSame(pose,
                ((DNAEffect_BonePose)
                 loaded.expressions[26].dna.effects[0]).bonePose);
        }

        private DynamicExpressionPlayer NewPlayer(UMAExpressionGroup group,
            out UMAData data, string boneName = null)
        {
            GameObject avatar = Track(new GameObject("ExpressionAvatar"));
            data = avatar.AddComponent<UMAData>();
            if (!string.IsNullOrEmpty(boneName))
            {
                GameObject bone = new GameObject(boneName);
                bone.transform.SetParent(avatar.transform, false);
                data.skeleton = new UMASkeleton(avatar.transform);
            }
            DynamicExpressionPlayer player =
                avatar.AddComponent<DynamicExpressionPlayer>();
            player.expressionGroupOverride = group;
            player.EnableBlinking = false;
            player.EnableSaccades = false;
            player.EnableLookAt = false;
            player.processDistance = 0f;
            player.Rebind();
            return player;
        }

        private DNA NewDNA(string name, float defaultValue)
        {
            DNA dna = Track(ScriptableObject.CreateInstance<DNA>());
            dna.name = name;
            dna.displayName = name;
            dna.defaultValue = defaultValue;
            return dna;
        }

        private UMAExpressionGroup NewGroup(
            params UMAExpressionDefinition[] definitions)
        {
            UMAExpressionGroup group =
                Track(ScriptableObject.CreateInstance<UMAExpressionGroup>());
            group.expressions.AddRange(definitions);
            return group;
        }

        private static UMAExpressionDefinition Definition(string id, DNA dna,
            int priority = 0,
            ExpressionJoint joints = ExpressionJoint.Other,
            ExpressionBlendMode blend = ExpressionBlendMode.Override) =>
            new UMAExpressionDefinition
            {
                id = id,
                displayName = id,
                dna = dna,
                priority = priority,
                affectedJoints = joints,
                blendMode = blend
            };

        private UMABonePose NewPose(string boneName, float zDegrees)
        {
            UMABonePose pose =
                Track(ScriptableObject.CreateInstance<UMABonePose>());
            pose.name = boneName + "_" + zDegrees;
            pose.poses = new[]
            {
                new UMABonePose.PoseBone
                {
                    bone = boneName,
                    hash = UMAUtils.StringToHash(boneName),
                    position = Vector3.zero,
                    rotation = Quaternion.Euler(0f, 0f, zDegrees),
                    scale = Vector3.one,
                    enabled = true
                }
            };
            return pose;
        }

        private void TrackConversion(
            UMAExpressionSetConverter.ConversionResult result)
        {
            Track(result.group);
            for (int i = 0; i < result.dnaAssets.Count; i++)
                Track(result.dnaAssets[i]);
        }

        private static void AssertPhase(DNAEffect effect,
            ExpressionEffectPhase expected)
        {
            Assert.AreEqual(expected, effect.ExpressionPhases,
                effect.GetType().Name);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _objects.Add(value);
            return value;
        }
    }
}
#endif

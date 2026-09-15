#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed class HairCharacterBindingTests
    {
        private HairGroomAsset groom;
        private SlotDataAsset slot;
        private Mesh source;
        private HairCharacterBindingAsset binding;
        private readonly List<Object> owned=new List<Object>();
        private string folder;
        private T Own<T>(T item) where T:Object { owned.Add(item);return item; }
        [SetUp] public void Setup()
        {
            source=Own(new Mesh { name="Authored surface" });
            source.vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0)};
            source.triangles=new[]{0,1,2};source.RecalculateNormals();
            groom=Own(ScriptableObject.CreateInstance<HairGroomAsset>());groom.SetSource(source,"fixture");
            Array.Fill(groom.Groups[0].FindMap(HairMapKind.GrowthArea).values,1f);
            slot=Own(ScriptableObject.CreateInstance<SlotDataAsset>());slot.name="Body";slot.PrepareForAssetPath("Assets/Body.asset","Body");
            slot.meshData=new UMAMeshData();slot.meshData.RetrieveDataFromUnityMesh(source);
            slot.meshData.bindPoses=new[]{Matrix4x4.identity,Matrix4x4.Translate(Vector3.down)};
            slot.meshData.boneNameHashes=new[]{UMAUtils.StringToHash("Neck"),UMAUtils.StringToHash("Head")};
            slot.meshData.umaBones=new[]{
                new UMATransform {name="Neck",hash=UMAUtils.StringToHash("Neck"),parent=0,rotation=Quaternion.identity,scale=Vector3.one},
                new UMATransform {name="Head",hash=UMAUtils.StringToHash("Head"),parent=UMAUtils.StringToHash("Neck"),position=Vector3.up,rotation=Quaternion.identity,scale=Vector3.one}};
            slot.meshData.umaBoneCount=2;slot.meshData.RootBoneName="Neck";
            slot.meshData.ManagedBonesPerVertex=new byte[]{1,1,1};
            slot.meshData.ManagedBoneWeights=new[]{new BoneWeight1{boneIndex=0,weight=1},new BoneWeight1{boneIndex=0,weight=1},new BoneWeight1{boneIndex=1,weight=1}};
        }
        private HairCharacterBindingAsset Build(Matrix4x4? matrix=null)
        {
            binding=HairCharacterBindingBuilder.Build(groom,new[]{new HairCharacterBindingBuilder.Input {
                slot=new SlotData(slot),toCharacter=matrix ?? Matrix4x4.identity }},Matrix4x4.identity,.03f);
            return binding;
        }
        [TearDown] public void Cleanup()
        {
            if(HairCardStage.ActiveStage?.Groom==groom) StageUtility.GoToMainStage();
            HairCharacterBindingBuilder.Dispose(binding);
            if(folder!=null) AssetDatabase.DeleteAsset(folder);
            for(int i=owned.Count-1;i>=0;i--) if(owned[i]!=null && !EditorUtility.IsPersistent(owned[i])) Object.DestroyImmediate(owned[i]);
            owned.Clear();Undo.ClearAll();
        }
        [Test] public void BindingPreservesAuthoringDataAndBlendsTriangleWeights()
        {
            string before=EditorJsonUtility.ToJson(groom);var vertices=source.vertices;
            var result=Build();
            Assert.That(EditorJsonUtility.ToJson(groom),Is.EqualTo(before));Assert.That(source.vertices,Is.EqualTo(vertices));
            Assert.That(source.bindposes,Is.Empty);Assert.That(result.weightedSource.vertexCount,Is.EqualTo(source.vertexCount));
            var sample=Own(new Mesh());sample.vertices=new[]{new Vector3(.25f,.5f,0)};
            new HairSurfaceSkinning(result.donorMesh).Transfer(sample);
            var weights=sample.GetAllBoneWeights().ToArray();
            Assert.That(weights.Length,Is.EqualTo(2));Assert.That(weights.Sum(w=>w.weight),Is.EqualTo(1).Within(1e-5));
            Assert.That(weights[0].weight,Is.EqualTo(.5f).Within(1e-5));
        }
        [Test] public void AlignmentRejectsDistantRootsAndDoesNotMutateGroom()
        {
            var result=Build(Matrix4x4.Translate(Vector3.right*3));
            Assert.That(HairCharacterBindingBuilder.AlignmentError(groom,result),Does.Contain("farther"));
            Assert.That(groom.CharacterBinding,Is.Null);Assert.That(source.bindposes,Is.Empty);
        }
        [Test] public void DeclaredRootBoneCanBeOmittedFromUmaTransforms()
        {
            slot.meshData.umaBones=slot.meshData.umaBones.Skip(1).ToArray();slot.meshData.umaBoneCount=1;
            slot.meshData.rootBoneHash=UMAUtils.StringToHash("Neck");
            Build();Assert.That(binding.boneNames,Is.EqualTo(new[]{"Neck","Head"}));
            Assert.That(binding.boneParents,Is.EqualTo(new[]{-1,0}));
        }
        [Test] public void DifferentTopologyIsSupportedAndScalpSamplesUseBarycentricCoordinates()
        {
            source.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,new Vector3(.25f,.25f,0)};
            source.triangles=new[]{0,1,3,1,2,3,2,0,3};source.RecalculateNormals();groom.SetSource(source,"fixture");
            var result=Build();Assert.That(result.donorMesh.vertexCount,Is.EqualTo(3));Assert.That(result.weightedSource.vertexCount,Is.EqualTo(4));
            Assert.That(HairCharacterBindingBuilder.AlignmentError(groom,result),Is.Null);
            Assert.That(result.scalpSamples.All(s=>s.valid),Is.True);
        }
        [Test] public void SurfaceTransferKeepsMoreThanFourInfluences()
        {
            var donor=Own(Object.Instantiate(source));donor.bindposes=Enumerable.Repeat(Matrix4x4.identity,6).ToArray();
            HairSurfaceSkinning.SetWeights(donor,new byte[]{6,6,6},Enumerable.Range(0,18).Select(i=>new BoneWeight1 {boneIndex=i%6,weight=1f/6}).ToArray());
            var target=Own(Object.Instantiate(source));new HairSurfaceSkinning(donor).Transfer(target);
            Assert.That(target.GetBonesPerVertex().ToArray(),Is.EqualTo(new byte[]{6,6,6}));
        }
        [Test] public void CardsUseRootWeightsEvenWhenTipsReachAnotherBone()
        {
            Build();var mesh=Own(new Mesh());mesh.vertices=new[]{new Vector3(.1f,.1f,0),new Vector3(0,1,0)};
            var curve=new HairEvaluatedCurve();curve.points.Add(new HairCurvePoint(mesh.vertices[0],.01f,0));curve.points.Add(new HairCurvePoint(mesh.vertices[1],.01f,0));
            var build=new HairCardMeshBuildResult {mesh=mesh,vertexCount=2};build.cards.Add(new HairCardSpan {curve=curve,vertexStart=0,vertexCount=2});
            HairSurfaceSkinning.TransferCards(build,binding);
            var weights=mesh.GetAllBoneWeights().ToArray();Assert.That(weights[0].weight,Is.EqualTo(weights[2].weight));
            Assert.That(weights[0].weight,Is.EqualTo(.9f).Within(1e-5));
        }
        [Test] public void CoordinateConversionPreservesSkinningEquationAndBoneFocus()
        {
            var matrix=Matrix4x4.TRS(new Vector3(2,3,4),Quaternion.Euler(17,39,63),Vector3.one*1.5f);
            Build(matrix);
            Assert.That((HairCharacterBindingUtility.BonePosition(binding,"Head").Value-matrix.MultiplyPoint3x4(Vector3.up)).magnitude,Is.LessThan(1e-5));
            var original=Own(Object.Instantiate(binding.donorMesh));var v=original.vertices[0];var bind=original.bindposes[0];
            HairCharacterBindingUtility.TransformMesh(original,matrix.inverse);
            Assert.That((original.bindposes[0].MultiplyPoint3x4(original.vertices[0])-bind.MultiplyPoint3x4(v)).magnitude,Is.LessThan(1e-5));
        }
        [Test] public void SkeletonSurvivesWithoutSceneCharacterAndProducesUsableSlotBones()
        {
            Build();var owner=Own(new GameObject("Binding fixture"));var renderer=owner.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=binding.donorMesh;
            HairCharacterBindingUtility.ConfigureRig(renderer,binding);
            Assert.That(renderer.bones.Select(b=>b.name),Is.EqualTo(new[]{"Neck","Head"}));
            Assert.That(renderer.bones[1].parent,Is.EqualTo(renderer.bones[0]));
            var output=Own(ScriptableObject.CreateInstance<SlotDataAsset>());output.UpdateMeshData(renderer,"Neck",false,-1,false,false);
            Assert.That(output.meshData.boneNameHashes,Does.Contain(UMAUtils.StringToHash("Head")));
            Assert.That(output.meshData.bindPoses.Length,Is.GreaterThan(0));
        }
        [Test] public void ScalpBindingTargetsRealSlotIndicesAndPreservesAlpha()
        {
            Build();groom.CharacterBinding=binding;
            var c=Enumerable.Repeat(new Color32(255,255,255,73),3).ToArray();binding.donorMesh.colors32=c;
            var settings=groom.Groups[0].generation.scalp;settings.enabled=true;
            var colors=HairScalpShadingEditor.BoundColors(groom);
            Assert.That(colors.All(color=>color.a==73),Is.True);
            var modifier=Own(ScriptableObject.CreateInstance<UMA.MeshModifier>());
            HairScalpShadingEditor.Populate(modifier,groom,null,new[]{new HairScalpSlotBinding{slotName="Body",sourceVertexStart=0,vertexCount=3}});
            Assert.That(modifier.EditorModifiers.All(m=>m.SlotName=="Body"),Is.True);
        }
        [Test] public void ChangedSlotDataInvalidatesTheBinding()
        {
            Build();groom.CharacterBinding=binding;
            Assert.That(HairCharacterBindingUtility.Validate(groom),Is.Null);
            slot.meshData.vertices[0]+=Vector3.forward;
            Assert.That(HairCharacterBindingUtility.Validate(groom),Does.Contain("geometry changed"));
        }
        [Test] public void BindingSavesAndReloadsMeshesAndSlotsWithoutSceneObjects()
        {
            folder="Assets/HairBindingTest-"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",folder.Substring(7));
            AssetDatabase.CreateAsset(source,folder+"/Source.asset");AssetDatabase.CreateAsset(slot,folder+"/Body.asset");AssetDatabase.CreateAsset(groom,folder+"/Groom.asset");
            Build();HairCharacterBindingBuilder.Save(groom,binding);var path=AssetDatabase.GetAssetPath(binding);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var loaded=AssetDatabase.LoadAssetAtPath<HairCharacterBindingAsset>(path);
            Assert.That(loaded.Matches(groom),Is.True);Assert.That(loaded.slots[0].asset,Is.EqualTo(slot));
            Assert.That(loaded.weightedSource.GetAllBoneWeights().Length,Is.GreaterThan(0));Assert.That(source.bindposes,Is.Empty);
            Undo.PerformUndo();Assert.That(groom.CharacterBinding,Is.Null);
            Undo.PerformRedo();Assert.That(groom.CharacterBinding,Is.EqualTo(loaded));
        }
        [Test] public void BoundBakeWritesSkinnedLodsAndSlotWithoutACharacterInTheScene()
        {
            folder="Assets/HairBindingTest-"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",folder.Substring(7));
            AssetDatabase.CreateAsset(source,folder+"/Source.asset");AssetDatabase.CreateAsset(slot,folder+"/Body.asset");AssetDatabase.CreateAsset(groom,folder+"/Groom.asset");
            var profile=Own(ScriptableObject.CreateInstance<HairCardProfileAsset>());profile.Configure(HairCardShape.Ribbon,.01f,.002f,6,generateBackfaces:false);
            var atlas=Own(ScriptableObject.CreateInstance<HairAtlasProfileAsset>());atlas.CreateRegion("Full",new Rect(0,0,1,1));
            AssetDatabase.CreateAsset(profile,folder+"/Profile.asset");AssetDatabase.CreateAsset(atlas,folder+"/Atlas.asset");
            var group=groom.Groups[0];group.profile=profile;group.atlas=atlas;group.children.childrenPerGuide=0;
            HairMeshUtility.TryFindClosestSurface(source,groom.SourceMeshId,new Vector3(.1f,.1f,0),out var anchor);
            var guide=new HairGuide {name="Bake test",root=anchor};
            guide.points.Add(new HairGuidePoint{position=anchor.CachedLocalPosition,width=.01f});
            guide.points.Add(new HairGuidePoint{position=anchor.CachedLocalPosition+Vector3.forward*.15f,width=.002f});
            guide.EnsureIntegrity(.01f);group.guides.Add(guide);
            groom.Lods.Clear();groom.Lods.Add(new HairLodSettings {level=0});groom.Lods.Add(new HairLodSettings {level=1});
            var settings=groom.BakeSettings;settings.outputFolder=folder+"/Output";settings.assetName="BoundHair";
            settings.createMesh=settings.createSlot=true;settings.createOverlay=settings.createWardrobeRecipe=settings.updateGlobalLibrary=false;
            Build();HairCharacterBindingBuilder.Save(groom,binding);
            var outcome=HairBakePipeline.Bake(groom);
            Assert.That(outcome.succeeded,Is.True,string.Join("\n",outcome.validation.issues.Select(i=>i.message).Concat(outcome.warnings)));
            var meshes=outcome.assets.OfType<Mesh>().ToArray();Assert.That(meshes.Length,Is.EqualTo(2));
            foreach(var mesh in meshes) {Assert.That(mesh.GetBonesPerVertex().Length,Is.EqualTo(mesh.vertexCount));Assert.That(mesh.bindposeCount,Is.EqualTo(2));}
            var output=outcome.assets.OfType<SlotDataAsset>().Single();Assert.That(output.meshData.boneNameHashes,Does.Contain(UMAUtils.StringToHash("Head")));
            var reasons=new List<string>();Assert.That(output.ValidateMeshData(reasons),Is.True,string.Join("; ",reasons));
            Assert.That(source.bindposes,Is.Empty);
        }
        [UnityTest] public IEnumerator BoundStageHasSlotVisibilityAndHeadNeckFocus()
        {
            Build();groom.CharacterBinding=binding;
            bool suspended=HairEditorPreferences.Suspended;HairEditorPreferences.Suspended=true;
            try
            {
                var scene=SceneView.GetWindow<SceneView>();scene.Show();yield return null;
                var stage=HairCardStage.ShowStage(groom);yield return null;
                Assert.That(stage.ShowAvatar,Is.True);
                Assert.That(stage.HasAvatarVisibility,Is.True);Assert.That(stage.SlotVisibilityGroups[0].SlotNames,Does.Contain("Body"));
                Assert.That(stage.CanFocusBone(HumanBodyBones.Head),Is.True);Assert.That(stage.CanFocusBone(HumanBodyBones.Neck),Is.True);
                stage.ShowAvatar=false;Assert.That(stage.ShowAvatar,Is.False);
                StageUtility.GoToMainStage();
            }
            finally { HairEditorPreferences.Suspended=suspended; }
        }

        [Test] public void PointyReferenceCanUseRealBodySlotWeightsWhenReferenceFixturesArePresent()
        {
            // Optional reference content is supplied only to the isolated validation project.
            const string directory="Assets/HairBindingReferenceSlots";
            if(!AssetDatabase.IsValidFolder(directory)) return;
            var pointy=AssetDatabase.LoadAssetAtPath<HairGroomAsset>("Assets/HairReferenceReview/Pointy_HairGroom.asset");
            Assert.That(pointy,Is.Not.Null);
            var slots=AssetDatabase.FindAssets("t:SlotDataAsset",new[]{directory}).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<SlotDataAsset>).OrderBy(s=>s.slotName).ToArray();
            Assert.That(slots.Length,Is.EqualTo(5));
            var race=AssetDatabase.LoadAssetAtPath<RaceData>(directory+"/HumanFemale30.asset");Assert.That(race,Is.Not.Null);
            var before=EditorJsonUtility.ToJson(pointy);
            var actual=HairCharacterBindingBuilder.Build(pointy,slots.Select(s=>new HairCharacterBindingBuilder.Input {
                slot=new SlotData(s),race=race,toCharacter=Matrix4x4.identity}).ToArray(),HairCharacterBindingBuilder.AxisMatrix(HairCharacterBindingBuilder.DonorAxes.ZUpToYUpFlipForward),.03f);
            try
            {
                var error=HairCharacterBindingBuilder.AlignmentError(pointy,actual);
                Debug.Log("POINTY_BODY_BINDING: "+actual.donorMesh.vertexCount+" vertices, "+actual.boneNames.Length+" bones, max distance "+actual.maximumMatchedDistance+"; "+error+" fixup="+race.FixupRotations+" donor="+actual.donorMesh.bounds+" source="+pointy.SourceMesh.bounds);
                Assert.That(error,Is.Null);
                var evaluation=HairGroomEvaluator.Evaluate(pointy);using var build=HairCardMeshGenerator.Build(evaluation,"Skinning validation",includeEditingMetadata:true);
                HairSurfaceSkinning.TransferCards(build,actual);
                Assert.That(build.mesh.GetBonesPerVertex().Length,Is.EqualTo(build.mesh.vertexCount));
                Assert.That(EditorJsonUtility.ToJson(pointy),Is.EqualTo(before));
                Debug.Log("POINTY_BODY_BINDING_PASSED "+build.cardCount+" cards");
            }
            finally { HairCharacterBindingBuilder.Dispose(actual); }
        }
    }
}
#endif

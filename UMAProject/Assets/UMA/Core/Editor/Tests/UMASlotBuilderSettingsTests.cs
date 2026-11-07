#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMASlotBuilderSettingsTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private string folder;
        private GameObject source;
        private Mesh mesh;
        private Material firstMaterial, secondMaterial;
        private SlotBuilderSettingsTestWindow window;

        [SetUp]
        public void SetUp()
        {
            string name="__UMASlotBuilderSettingsTests_"+Guid.NewGuid().ToString("N");
            folder="Assets/"+name;AssetDatabase.CreateFolder("Assets",name);
            source=new GameObject("Source model");
            var root=new GameObject("Global").transform;root.SetParent(source.transform,false);
            var bone=new GameObject("Hips").transform;bone.SetParent(root,false);
            var meshObject = new GameObject("SourceMesh");
            meshObject.transform.SetParent(source.transform, false);
            var renderer=meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone=root;renderer.bones=new[]{bone};
            mesh=new Mesh
            {
                name="Source mesh",
                vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,new Vector3(2,0,0),new Vector3(3,0,0),new Vector3(2,1,0)},
                uv=new[]{new Vector2(.1f,.1f),new Vector2(.4f,.1f),new Vector2(.1f,.4f),
                    new Vector2(.6f,.1f),new Vector2(.9f,.1f),new Vector2(.6f,.4f)},
                bindposes=new[]{bone.worldToLocalMatrix*source.transform.localToWorldMatrix},
                boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},6).ToArray(),
                subMeshCount=2
            };
            mesh.SetTriangles(new[]{0,1,2},0);mesh.SetTriangles(new[]{3,4,5},1);
            mesh.RecalculateNormals();mesh.RecalculateTangents();renderer.sharedMesh=mesh;
            var shader=Shader.Find("Hidden/InternalErrorShader");Assert.That(shader,Is.Not.Null);
            firstMaterial=new Material(shader){name="theMaterial_01"};
            secondMaterial=new Material(shader){name="skinDetail_LOD0"};
            renderer.sharedMaterials=new[]{firstMaterial,secondMaterial};
            var umaMaterial=ScriptableObject.CreateInstance<UMAMaterial>();
            AssetDatabase.CreateAsset(umaMaterial,folder+"/TestUMAMaterial.asset");
            window=ScriptableObject.CreateInstance<SlotBuilderSettingsTestWindow>();
            window.slotName="SourceMesh";window.slotMesh=renderer;window.RootBone="Global";
            window.slotFolder=AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            window.slotMaterial=umaMaterial;window.nameAfterMaterial=true;window.createOverlay=true;
        }

        [TearDown]
        public void TearDown()
        {
            if(window!=null)UnityEngine.Object.DestroyImmediate(window);
            if(source!=null)UnityEngine.Object.DestroyImmediate(source);
            if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
            if(firstMaterial!=null)UnityEngine.Object.DestroyImmediate(firstMaterial);
            if(secondMaterial!=null)UnityEngine.Object.DestroyImmediate(secondMaterial);
            if(!string.IsNullOrEmpty(folder))AssetDatabase.DeleteAsset(folder);
        }

        [TestCase(false,false,false)] [TestCase(false,false,true)]
        [TestCase(false,true,false)] [TestCase(false,true,true)]
        [TestCase(true,false,false)] [TestCase(true,false,true)]
        [TestCase(true,true,false)] [TestCase(true,true,true)]
        public void BuildsPreserveMaterialCaseAndHonorDestinationFolder(bool udim,bool rootFolder,bool batchMode)
        {
            window.udimAdjustment=udim;
            if (udim)
            {
                // A mesh confined to tile 1001 uses the regular build path even with UDIM enabled.
                mesh.uv = mesh.uv.Select(uv => uv + Vector2.right).ToArray();
            }
            typeof(UmaSlotBuilderWindow).GetField("useRootFolder",Private).SetValue(window,rootFolder);
            var result=(UMASlotProcessingUtil.SlotBuildResult)typeof(UmaSlotBuilderWindow)
                .GetMethod("CreateSlot_Internal_WithResult",Private).Invoke(window,new object[]{batchMode});
            Assert.That(result,Is.Not.Null);Assert.That(result.Slots,Has.Count.EqualTo(2));
            Assert.That(result.IsUDIM, Is.EqualTo(udim));
            string destination=folder+(rootFolder?"":"/SourceMesh");
            foreach(string materialName in new[]{firstMaterial.name,secondMaterial.name})
            {
                string slotName=materialName+(udim?"_UDIM1002":"");
                string expectedPath = destination + "/" + slotName + "_slot.asset";
                Assert.That(result.Slots.Select(AssetDatabase.GetAssetPath), Does.Contain(expectedPath));
                var slot=result.Slots.Single(s=>AssetDatabase.GetAssetPath(s)==expectedPath);
                Assert.That(slot,Is.Not.Null);
                Assert.That(result.SlotToOverlay.TryGetValue(slot,out var overlay),Is.True);
                Assert.That(AssetDatabase.GetAssetPath(overlay),Does.StartWith(destination+"/"));
                Assert.That(AssetDatabase.GetAssetPath(overlay),Does.Contain(materialName));
            }
            Assert.That(AssetDatabase.IsValidFolder(folder+"/SourceMesh"),Is.EqualTo(!rootFolder));
        }

        [TestCase(false)] [TestCase(true)]
        public void MissingOrBlankMaterialFallsBackToRequestedSlotName(bool blank)
        {
            window.slotName="Folder/myCustomSlot";
            if(blank)firstMaterial.name="   ";
            else window.slotMesh.sharedMaterials=new[]{(Material)null,secondMaterial};
            string name=(string)typeof(UmaSlotBuilderWindow).GetMethod("GetSlotName",Private)
                .Invoke(window,new object[]{window.slotMesh});
            Assert.That(name,Is.EqualTo("myCustomSlot"));
        }
    }

    // Exercise the production build method without reading/writing the user's window preferences.
    internal sealed class SlotBuilderSettingsTestWindow : UmaSlotBuilderWindow
    {
        private void OnEnable() { }
        private void OnDisable() { }
    }
}
#endif

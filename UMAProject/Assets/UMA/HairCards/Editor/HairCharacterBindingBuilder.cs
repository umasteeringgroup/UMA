using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor
{
    internal static class HairCharacterBindingBuilder
    {
        internal enum DonorAxes { AsSaved, ZUpToYUp, ZUpToYUpFlipForward }
        internal static Matrix4x4 AxisMatrix(DonorAxes axes)
        {
            if(axes==DonorAxes.ZUpToYUp) return Matrix4x4.Rotate(Quaternion.Euler(-90,0,0));
            if(axes==DonorAxes.ZUpToYUpFlipForward)
            {
                var matrix=Matrix4x4.identity;matrix[1,1]=matrix[2,2]=0;matrix[1,2]=matrix[2,1]=1;return matrix;
            }
            return Matrix4x4.identity;
        }
        internal sealed class Input
        {
            internal SlotData slot;
            internal SkinnedMeshRenderer renderer;
            internal RaceData race;
            internal Matrix4x4 toCharacter;
        }
        internal static List<Input> Inputs(Object source)
        {
            var result = new List<Input>();
            if (source is RaceData race)
            {
                if (race.baseRaceRecipe == null) throw new InvalidOperationException("This race has no Base Recipe.");
                var slots = race.baseRaceRecipe.GetCachedRecipe(loadSlots: true)?.slotDataList;
                foreach (var slot in slots ?? Array.Empty<SlotData>())
                    if (slot?.asset?.meshData?.vertexCount > 0)
                        result.Add(new Input { slot = slot, race = race,
                            // Keep native slot bind space. Source-axis conversion is an explicit
                            // alignment choice, not inferred from runtime skeleton settings.
                            toCharacter = Matrix4x4.identity });
            }
            else
            {
                var owner = source as GameObject ?? (source as Component)?.gameObject;
                var avatar = owner?.GetComponentInParent<DynamicCharacterAvatar>();
                if (avatar?.umaData?.umaRecipe == null) throw new InvalidOperationException("Choose a RaceData asset or a generated DynamicCharacterAvatar.");
                var renderers = avatar.umaData.GetRenderers();
                foreach (var slot in avatar.umaData.umaRecipe.slotDataList ?? Array.Empty<SlotData>())
                {
                    if (slot?.asset?.meshData?.vertexCount <= 0 || slot?.asset == null || renderers == null ||
                        slot.skinnedMeshRenderer < 0 || slot.skinnedMeshRenderer >= renderers.Length) continue;
                    var renderer = renderers[slot.skinnedMeshRenderer];
                    if (renderer?.sharedMesh == null) continue;
                    if (slot.vertexOffset < 0 || slot.vertexOffset + slot.asset.meshData.vertexCount > renderer.sharedMesh.vertexCount)
                        throw new InvalidOperationException("A body slot has no verified vertex range. Rebuild the character first.");
                    result.Add(new Input { slot = slot, renderer = renderer, race = avatar.activeRace?.data,
                        toCharacter = avatar.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix });
                }
            }
            if (result.Count == 0) throw new InvalidOperationException("No weighted body slots could be loaded from this source.");
            return result.GroupBy(i => i.slot.slotName, StringComparer.Ordinal).Select(g => g.First()).ToList();
        }

        internal static HairCharacterBindingAsset Build(HairGroomAsset groom, IReadOnlyList<Input> inputs,
            Matrix4x4 alignment, float tolerance)
        {
            if (groom?.SourceMesh == null || !groom.SourceMesh.isReadable || inputs.Count == 0)
                throw new ArgumentException("Choose at least one body slot and a readable groom surface.");
            if (!float.IsFinite(tolerance) || tolerance <= 0 || Mathf.Abs(alignment.determinant) < 1e-8f)
                throw new ArgumentException("Alignment and distance tolerance must be finite and nonzero.");
            for(int i=0;i<16;i++) if(!float.IsFinite(alignment[i])) throw new ArgumentException("Alignment contains a non-finite value.");
            var binding = ScriptableObject.CreateInstance<HairCharacterBindingAsset>();
            binding.name = groom.name + " Character Binding"; binding.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                binding.sourceTopology = groom.SourceTopologySignature; binding.sourceGeometry=HairCharacterBindingAsset.GeometrySignature(groom.SourceMesh);binding.race = inputs[0].race;
                binding.characterToSource = alignment; binding.maximumDistance = tolerance;
                var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
                var colors = new List<Color32>(); var counts = new List<byte>(); var weights = new List<BoneWeight1>();
                var names = new List<string>(); var parents = new Dictionary<string,string>(StringComparer.Ordinal);
                var poses = new List<Matrix4x4>(); var palette = new Dictionary<string,int>(StringComparer.Ordinal);
                var triangles = new List<int[]>(); var boundSlots = new List<HairCharacterBindingAsset.Slot>();
                foreach (var input in inputs)
                {
                    var data = input.slot.asset.meshData;
                    if (data == null) throw new InvalidOperationException("A selected slot no longer contains mesh data.");
                    int count = data.vertexCount, start = positions.Count;
                    Matrix4x4 matrix = alignment * input.toCharacter;
                    if(!float.IsFinite(matrix.determinant) || Mathf.Abs(matrix.determinant)<1e-8f)
                        throw new InvalidOperationException("The donor transform is invalid or has zero scale.");
                    Matrix4x4 normalMatrix = matrix.inverse.transpose;
                    Vector3[] v, n; Vector2[] u; Color32[] c; Matrix4x4[] bind;
                    byte[] perVertex; BoneWeight1[] rawWeights;
                    int offset = 0, weightOffset = 0;
                    string[] sourceNames;
                    if (input.renderer != null)
                    {
                        var mesh = input.renderer.sharedMesh;
                        // Match the visible built body, including its skeleton/DNA pose. Renderer
                        // scale is handled by toCharacter, never applied twice by BakeMesh.
                        var baked=new Mesh { indexFormat=mesh.indexFormat,hideFlags=HideFlags.HideAndDontSave };
                        try { input.renderer.BakeMesh(baked,false);v=baked.vertices;n=baked.normals; }
                        finally { Object.DestroyImmediate(baked); }
                        u = mesh.uv; c = mesh.colors32;
                        perVertex = mesh.GetBonesPerVertex().ToArray(); rawWeights = mesh.GetAllBoneWeights().ToArray();
                        offset = input.slot.vertexOffset;
                        if (perVertex.Length != v.Length) throw new InvalidOperationException("The character renderer has no skin weights.");
                        for (int j = 0; j < offset; j++) weightOffset += perVertex[j];
                        var bones = input.renderer.bones;
                        if(bones.Any(b=>b==null)) throw new InvalidOperationException("The character has a missing bone transform.");
                        bind=bones.Select(b=>b.worldToLocalMatrix*input.renderer.transform.localToWorldMatrix).ToArray();
                        sourceNames = bones.Select(b => b != null ? b.name : null).ToArray();
                        if (input.renderer.rootBone != null) binding.rootBoneName = input.renderer.rootBone.name;
                        foreach (var bone in bones) if (bone != null) parents[bone.name] = bone.parent != null ? bone.parent.name : null;
                    }
                    else
                    {
                        v = data.vertices; n = data.normals; u = data.uv; c = data.colors32; bind = data.bindPoses;
                        perVertex = data.ManagedBonesPerVertex; rawWeights = data.ManagedBoneWeights;
                        if (perVertex == null || perVertex.Length != count || rawWeights == null || rawWeights.Length == 0)
                        {
                            if (data.boneWeights == null || data.boneWeights.Length != count)
                                throw new InvalidOperationException("Slot '" + input.slot.slotName + "' has no saved bone weights.");
                            var legacyCounts = new List<byte>(); var legacy = new List<BoneWeight1>();
                            foreach (var w in data.boneWeights)
                            {
                                int before = legacy.Count;
                                void Add(int index, float value) { if (value > 0) legacy.Add(new BoneWeight1 { boneIndex = index, weight = value }); }
                                Add(w.boneIndex0,w.weight0); Add(w.boneIndex1,w.weight1); Add(w.boneIndex2,w.weight2); Add(w.boneIndex3,w.weight3);
                                legacyCounts.Add((byte)(legacy.Count-before));
                            }
                            perVertex = legacyCounts.ToArray(); rawWeights = legacy.ToArray();
                        }
                        var transforms = (data.umaBones ?? Array.Empty<UMATransform>()).Where(b => b != null)
                            .GroupBy(b => b.hash).ToDictionary(g => g.Key,g => g.First());
                        binding.rootBoneName = string.IsNullOrEmpty(data.RootBoneName) ? "Global" : data.RootBoneName;
                        // UMA creates Global separately; it is legitimately omitted from umaBones.
                        // Resolve only the declared root hash/name, never invent names for other bones.
                        string BoneName(int hash) => transforms.TryGetValue(hash,out var t) ? t.name :
                            hash==data.rootBoneHash && hash==UMAUtils.StringToHash(binding.rootBoneName) ? binding.rootBoneName : null;
                        sourceNames = (data.boneNameHashes ?? Array.Empty<int>()).Select(BoneName).ToArray();
                        foreach (var t in transforms.Values) parents[t.name] = BoneName(t.parent);
                    }
                    if (bind == null || bind.Length == 0 || bind.Length != sourceNames.Length || sourceNames.Any(string.IsNullOrEmpty))
                        throw new InvalidOperationException("Slot '" + input.slot.slotName + "' has incomplete bone names/bind poses.");
                    var remap = new int[bind.Length];
                    for (int b = 0; b < bind.Length; b++)
                    {
                        string name = sourceNames[b]; Matrix4x4 pose = bind[b] * matrix.inverse;
                        if (!palette.TryGetValue(name,out int target))
                        { target = names.Count; palette.Add(name,target); names.Add(name); poses.Add(pose); }
                        else if (!Approximately(poses[target],pose))
                            throw new InvalidOperationException("Selected slots disagree on the bind pose for '" + name + "'. Use one built character in a consistent rest pose.");
                        remap[b] = target;
                    }
                    for (int j = 0; j < count; j++)
                    {
                        int k = offset + j;
                        positions.Add(matrix.MultiplyPoint3x4(v[k]));
                        normals.Add(n?.Length == v.Length ? normalMatrix.MultiplyVector(n[k]).normalized : Vector3.up);
                        uv.Add(u?.Length == v.Length ? u[k] : Vector2.zero);
                        colors.Add(c?.Length == v.Length ? c[k] : new Color32(255,255,255,255));
                        counts.Add(perVertex[k]);
                        for (int w = 0; w < perVertex[k]; w++)
                        {
                            if (weightOffset >= rawWeights.Length) throw new InvalidOperationException("Truncated slot bone weights.");
                            var bw = rawWeights[weightOffset++];
                            if ((uint)bw.boneIndex >= remap.Length) throw new InvalidOperationException("Invalid slot bone index.");
                            bw.boneIndex = remap[bw.boneIndex]; weights.Add(bw);
                        }
                    }
                    int sub = input.slot.asset.subMeshIndex;
                    if (data.submeshes == null || sub < 0 || sub >= data.submeshes.Length)
                        throw new InvalidOperationException("Slot has no valid selected submesh.");
                    var indices = (int[])data.submeshes[sub].getManagedTriangles(0).Clone();
                    for (int t = 0; t < indices.Length; t++)
                    { if ((uint)indices[t] >= count) throw new InvalidOperationException("Slot triangle index is outside its verified vertex range."); indices[t] += start; }
                    if (matrix.determinant < 0) for (int t=0;t<indices.Length;t+=3) (indices[t+1],indices[t+2])=(indices[t+2],indices[t+1]);
                    triangles.Add(indices);
                    boundSlots.Add(new HairCharacterBindingAsset.Slot { asset=input.slot.asset,name=input.slot.slotName,
                        topology=SlotSignature(input.slot.asset),vertexStart=start,vertexCount=count,
                        material=input.slot.GetOverlayList()?.FirstOrDefault()?.asset?.material?.material });
                }
                var donor = new Mesh { name="Body Skinning Donor",indexFormat=positions.Count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    hideFlags=HideFlags.HideAndDontSave };
                binding.donorMesh=donor;
                donor.SetVertices(positions); donor.SetNormals(normals); donor.SetUVs(0,uv); donor.SetColors(colors);
                donor.subMeshCount=triangles.Count;
                for(int i=0;i<triangles.Count;i++) donor.SetTriangles(triangles[i],i);
                donor.bindposes=poses.ToArray(); HairSurfaceSkinning.SetWeights(donor,counts.ToArray(),weights.ToArray()); donor.RecalculateBounds();
                binding.boneNames=names.ToArray(); binding.boneParents=new int[names.Count];
                for(int i=0;i<names.Count;i++)
                {
                    var visited=new HashSet<string>(); string parent=parents.GetValueOrDefault(names[i]);
                    while(parent!=null && !palette.ContainsKey(parent) && visited.Add(parent)) parent=parents.GetValueOrDefault(parent);
                    binding.boneParents[i]=parent!=null && palette.TryGetValue(parent,out int p) && p!=i ? p : -1;
                }
                binding.slots=boundSlots.ToArray();
                var transfer=new HairSurfaceSkinning(donor);
                binding.weightedSource=Object.Instantiate(groom.SourceMesh); binding.weightedSource.name="Weighted Authoring Snapshot";
                binding.weightedSource.hideFlags=HideFlags.HideAndDontSave;
                transfer.Transfer(binding.weightedSource);
                // Build correspondence both ways, independently of vertex order or topology.
                var sourceSurface=new HairSurfaceSkinning(groom.SourceMesh,false);
                binding.scalpSamples=positions.Select(p=>sourceSurface.Sample(p,tolerance)).ToArray();
                return binding;
            }
            catch { Dispose(binding); throw; }
        }
        internal static string AlignmentError(HairGroomAsset groom, HairCharacterBindingAsset binding)
        {
            var surface=new HairSurfaceSkinning(binding.donorMesh);
            var points=new List<Vector3>(); var vertices=groom.SourceMesh.vertices;
            foreach(var group in groom.Groups)
            {
                var map=group.FindMap(HairMapKind.GrowthArea);
                if(map?.values?.Length==vertices.Length) for(int i=0;i<vertices.Length;i++) if(map.SampleVertex(i)>0) points.Add(vertices[i]);
                if (map?.UsesTexture == true) foreach (var tile in map.texture.tiles)
                {
                    int n = map.texture.resolution;
                    for (int y = 0; y <= n; y++) for (int x = 0; x <= n - y; x++)
                    {
                        if (tile.pixels[y * (n + 1) + x] <= 0) continue;
                        var bc = HairTextureMap.TexelBarycentric(x, y, n);
                        points.Add(vertices[tile.a] * bc.x + vertices[tile.b] * bc.y + vertices[tile.c] * bc.z);
                    }
                }
                foreach(var guide in group.guides) if(guide?.points?.Count>0) points.Add(guide.points[0].position);
            }
            int missed=0; float maximum=0;
            foreach(var point in points) { var sample=surface.Sample(point); maximum=Mathf.Max(maximum,sample.distance); if(!sample.valid || sample.distance>binding.maximumDistance) missed++; }
            binding.maximumMatchedDistance=maximum;
            return missed==0 ? null : $"{missed:N0} painted vertices / guide roots are farther than {binding.maximumDistance*1000:F1} mm from the selected body. Worst distance: {maximum*1000:F1} mm. Adjust alignment or choose matching slots; nothing has been attached.";
        }
        internal static string SlotSignature(SlotDataAsset slot)
        {
            if(slot?.meshData==null) return null;
            var data=slot.meshData; var hash=new Hash128(); hash.Append(data.vertexCount);
            foreach(var p in data.vertices) { hash.Append(p.x);hash.Append(p.y);hash.Append(p.z); }
            foreach(var sub in data.submeshes) foreach(var index in sub.getManagedTriangles(0)) hash.Append(index);
            foreach(var pose in data.bindPoses ?? Array.Empty<Matrix4x4>()) for(int i=0;i<16;i++) hash.Append(pose[i]);
            foreach(int bone in data.boneNameHashes ?? Array.Empty<int>()) hash.Append(bone);
            if(data.ManagedBoneWeights?.Length>0)
            {
                foreach(byte count in data.ManagedBonesPerVertex ?? Array.Empty<byte>()) hash.Append((int)count);
                foreach(var weight in data.ManagedBoneWeights) {hash.Append(weight.boneIndex);hash.Append(weight.weight);}
            }
            else foreach(var weight in data.boneWeights ?? Array.Empty<UMABoneWeight>())
            {
                hash.Append(weight.boneIndex0);hash.Append(weight.boneIndex1);hash.Append(weight.boneIndex2);hash.Append(weight.boneIndex3);
                hash.Append(weight.weight0);hash.Append(weight.weight1);hash.Append(weight.weight2);hash.Append(weight.weight3);
            }
            return hash.ToString();
        }
        internal static void Save(HairGroomAsset groom, HairCharacterBindingAsset binding)
        {
            if(!binding.Matches(groom)) throw new InvalidOperationException("The binding no longer matches the groom.");
            string error=AlignmentError(groom,binding); if(error!=null) throw new InvalidOperationException(error);
            string groomPath=AssetDatabase.GetAssetPath(groom);
            if(string.IsNullOrEmpty(groomPath)) throw new InvalidOperationException("Save the groom first.");
            string path=AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.GetDirectoryName(groomPath)+"/"+groom.name+"_CharacterBinding.asset");
            var backup=Object.Instantiate(groom);
            binding.name=System.IO.Path.GetFileNameWithoutExtension(path);
            binding.hideFlags=HideFlags.None; binding.donorMesh.hideFlags=binding.weightedSource.hideFlags=HideFlags.None;
            try
            {
                AssetDatabase.CreateAsset(binding,path);
                AssetDatabase.AddObjectToAsset(binding.donorMesh,binding); AssetDatabase.AddObjectToAsset(binding.weightedSource,binding);
                Undo.RegisterCompleteObjectUndo(groom,"Bind Hair Character"); groom.CharacterBinding=binding;
                // The old review-only scalp modifier stays as an asset, but cannot be reused for production.
                foreach(var group in groom.Groups) { group.generation.scalp.meshModifier=null; group.generation.scalp.slots.Clear(); group.generation.scalp.bindingTopology=null; }
                if(binding.race!=null) groom.BakeSettings.raceData=binding.race;
                EditorUtility.SetDirty(groom); EditorUtility.SetDirty(binding); AssetDatabase.SaveAssets();
            }
            catch { EditorUtility.CopySerialized(backup,groom);EditorUtility.SetDirty(groom);if(AssetDatabase.LoadMainAssetAtPath(path)!=null) AssetDatabase.DeleteAsset(path); throw; }
            finally { Object.DestroyImmediate(backup); }
        }
        internal static bool Approximately(Matrix4x4 a,Matrix4x4 b)
        { for(int i=0;i<16;i++) if(!float.IsFinite(a[i]) || !float.IsFinite(b[i]) || Mathf.Abs(a[i]-b[i])>.002f) return false; return true; }
        internal static void Dispose(HairCharacterBindingAsset binding)
        {
            if(binding==null || EditorUtility.IsPersistent(binding)) return;
            if(binding.donorMesh!=null) Object.DestroyImmediate(binding.donorMesh);
            if(binding.weightedSource!=null) Object.DestroyImmediate(binding.weightedSource);
            Object.DestroyImmediate(binding);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static class HairCharacterBindingUtility
    {
        internal static string Validate(HairGroomAsset groom)
        {
            var binding=groom?.CharacterBinding;if(binding==null) return null;
            if(!binding.Matches(groom)) return "The saved character binding is incomplete or the authoring topology changed. Rebind in Source & Setup.";
            if(binding.sourceGeometry!=HairCharacterBindingAsset.GeometrySignature(groom.SourceMesh)) return "The authoring surface geometry changed. Rebind the character before baking.";
            if(binding.boneParents==null || binding.boneParents.Length!=binding.boneNames.Length) return "The saved bone hierarchy is incomplete. Rebind the character.";
            foreach(var slot in binding.slots)
                if(slot==null || !(slot.asset is SlotDataAsset asset) || slot.topology!=HairCharacterBindingBuilder.SlotSignature(asset))
                    return "A bound body slot is missing or its geometry changed (including skin weights/bind poses). Rebind before copying weights or scalp colors.";
            return HairCharacterBindingBuilder.AlignmentError(groom,binding);
        }
        internal static void Skin(HairGroomAsset groom,HairCardMeshBuildResult build)
        {
            var binding=groom.CharacterBinding;
            HairSurfaceSkinning.TransferCards(build,binding);
            // Export in the character's coordinate system, not the alignment-preview space.
            TransformMesh(build.mesh,binding.characterToSource.inverse);
        }
        internal static void TransformMesh(Mesh mesh,Matrix4x4 transform)
        {
            var v=mesh.vertices;var n=mesh.normals;var t=mesh.tangents;var normal=transform.inverse.transpose;
            for(int i=0;i<v.Length;i++)
            {
                v[i]=transform.MultiplyPoint3x4(v[i]);
                if(n.Length==v.Length) n[i]=normal.MultiplyVector(n[i]).normalized;
                if(t.Length==v.Length) { var tangent=transform.MultiplyVector(t[i]).normalized;t[i]=new Vector4(tangent.x,tangent.y,tangent.z,t[i].w*(transform.determinant<0?-1:1)); }
            }
            mesh.vertices=v;if(n.Length==v.Length) mesh.normals=n;if(t.Length==v.Length) mesh.tangents=t;
            var poses=mesh.bindposes;for(int i=0;i<poses.Length;i++) poses[i]*=transform.inverse;mesh.bindposes=poses;
            if(transform.determinant<0) for(int s=0;s<mesh.subMeshCount;s++) { var tri=mesh.GetTriangles(s);for(int i=0;i<tri.Length;i+=3)(tri[i+1],tri[i+2])=(tri[i+2],tri[i+1]);mesh.SetTriangles(tri,s); }
            mesh.RecalculateBounds();
        }
        internal static void ConfigureRig(SkinnedMeshRenderer renderer,HairCharacterBindingAsset binding)
        {
            var poses=renderer.sharedMesh.bindposes;var bones=new Transform[binding.boneNames.Length];
            for(int i=0;i<bones.Length;i++) { bones[i]=new GameObject(binding.boneNames[i]).transform;bones[i].SetParent(renderer.transform,false); }
            for(int i=0;i<bones.Length;i++)
            {
                int parent=binding.boneParents[i];
                var seen=new HashSet<int>{i};int walk=parent;
                while(walk>=0) { if(walk>=bones.Length || !seen.Add(walk)) throw new InvalidOperationException("Invalid or cyclic binding skeleton.");walk=binding.boneParents[walk]; }
                Matrix4x4 local=(parent>=0 ? poses[parent] : Matrix4x4.identity)*poses[i].inverse;
                bones[i].SetParent(parent>=0 ? bones[parent] : renderer.transform,false);
                bones[i].localPosition=local.GetPosition();bones[i].localRotation=local.rotation;bones[i].localScale=local.lossyScale;
            }
            renderer.bones=bones;
            int root=Array.IndexOf(binding.boneNames,binding.rootBoneName);
            if(root>=0) renderer.rootBone=bones[root];
            else
            {
                var global=new GameObject(binding.rootBoneName).transform;global.SetParent(renderer.transform,false);
                for(int i=0;i<bones.Length;i++) if(binding.boneParents[i]<0) bones[i].SetParent(global,true);
                renderer.rootBone=global;
            }
        }
        internal static Vector3? BonePosition(HairCharacterBindingAsset binding,string name)
        {
            int i=Array.FindIndex(binding.boneNames,n=>string.Equals(n,name,StringComparison.OrdinalIgnoreCase));
            return i>=0 ? binding.donorMesh.bindposes[i].inverse.GetPosition() : (Vector3?)null;
        }
    }
}

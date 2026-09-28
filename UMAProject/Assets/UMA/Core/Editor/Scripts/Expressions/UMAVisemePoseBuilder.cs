using System;
using System.Collections.Generic;
using System.Linq;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    /// <summary>Reproducible, bone-local UMA 3 speech poses. No mesh or blendshape dependencies at runtime.</summary>
    public static class UMAVisemePoseBuilder
    {
        public const string ExpressionFolder = "Assets/UMA/UMA3/Races/Expressions";
        public const string VisemeFolder = ExpressionFolder + "/Visemes";
        public const string GroupPath = ExpressionFolder + "/DynamicExpressionSet/UMA 30 Expression Set_ExpressionGroup.asset";
        public const string MildGroupPath = ExpressionFolder + "/DynamicExpressionSet/UMA 30 Expression Set_ExpressionGroup_Mild.asset";
        public static readonly string[] Names = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U" };
        const string ReferenceSlot = "Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1001_slot.asset";

        // Millimetres in the anatomical frame, never in assumed bone-local XYZ axes.
        struct Shape
        {
            public float jaw, width, protrude, upper, lower, tongueForward, tongueUp, tongueTip;
            public Shape(float j,float w,float p,float u,float l,float tf=0,float tu=0,float tt=0)
            {jaw=j;width=w;protrude=p;upper=u;lower=l;tongueForward=tf;tongueUp=tu;tongueTip=tt;}
        }
        static Shape Specification(string name, bool mild)
        {
            switch(name)
            {
                // The same relaxed rest in both sets: a tiny jaw release, with
                // no lip compression, narrowing, protrusion or tongue gesture.
                case "sil": return new Shape(.75f,1,0,0,0);
                case "PP": return mild ? new Shape(0,.99f,.25f,-1.2f,2.7f) : new Shape(0,.98f,.4f,-1.5f,3.1f);
                case "FF": return mild ? new Shape(2.2f,1.01f,0,.6f,2.2f) : new Shape(3,1.02f,0,1,3.2f);
                case "TH": return mild ? new Shape(3.5f,1.02f,0,1,0,8,2,2) : new Shape(5.5f,1.03f,0,1.5f,0,11,3,4);
                case "DD": return mild ? new Shape(1.8f,.92f,.5f,3.5f,-2.5f,2,2,9) : new Shape(2.8f,.86f,1,5,-3.5f,3,3,13);
                case "kk": return mild ? new Shape(2.5f,.90f,.3f,3.2f,-2.5f,-4,2,-5) : new Shape(3.5f,.82f,.5f,4.5f,-3.5f,-6,3,-7);
                case "CH": return mild ? new Shape(1.6f,.72f,3,3.2f,-2.5f) : new Shape(2.3f,.58f,4.5f,4.5f,-3.5f);
                case "SS": return mild ? new Shape(1.7f,1.07f,0,1.2f,-.8f,0,1,0) : new Shape(2.3f,1.12f,0,1.7f,-1.1f,0,1.5f,0);
                case "nn": return mild ? new Shape(1.8f,.85f,1,3.2f,-2.5f,2,2,12) : new Shape(2.8f,.74f,1.5f,4.5f,-3.5f,3,3,17);
                case "RR": return mild ? new Shape(2.5f,.88f,1.8f,.2f,0,-1,2,10) : new Shape(3.5f,.78f,3,.4f,0,-2,3,15);
                case "aa": return mild ? new Shape(7,.99f,0,.6f,0,-2,-1,0) : new Shape(13,.98f,0,1,0,-3,-2,0);
                case "E": return mild ? new Shape(4.5f,1.06f,0,.7f,0,0,1,0) : new Shape(6.5f,1.12f,0,1.1f,0,0,1.5f,0);
                case "I": return mild ? new Shape(4.5f,1.08f,0,.5f,0,0,1,0) : new Shape(7,1.12f,0,.8f,0,0,2,0);
                case "O": return mild ? new Shape(5,.82f,2.3f,.3f,0) : new Shape(8.5f,.69f,3.8f,.5f,0);
                // Rounding must retain a visible aperture. Closing the lips while
                // barely rotating Mandible produces a sealed pout, not the U vowel.
                case "U": return mild ? new Shape(3,.75f,4,-.5f,0) : new Shape(4,.58f,6,-1,0);
                default: throw new ArgumentException("Unknown viseme: "+name);
            }
        }

        [MenuItem("UMA/Expressions/Rebuild UMA 3 Viseme Assets...")]
        static void RebuildMenu()
        {
            if (!EditorUtility.DisplayDialog("Rebuild UMA 3 visemes?", "Replaces the generated full and mild viseme poses and DNA. Existing non-viseme expressions are preserved. Back up any custom viseme edits first.", "Rebuild", "Cancel")) return;
            Build();
        }

        public static void Build()
        {
            var group=AssetDatabase.LoadAssetAtPath<UMAExpressionGroup>(GroupPath);
            var slot=AssetDatabase.LoadAssetAtPath<SlotDataAsset>(ReferenceSlot);
            if(group==null||slot==null)throw new InvalidOperationException("Install the standard UMA 3 races and expression group before building visemes.");
            UMAPathUtility.EnsureAssetFolder(VisemeFolder+"/Full");
            UMAPathUtility.EnsureAssetFolder(VisemeFolder+"/Mild");
            var rig=new GameObject("Viseme authoring reference"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var bones=new Dictionary<int,Transform>();
                foreach(var b in slot.meshData.umaBones)bones[b.hash]=new GameObject(b.name).transform;
                foreach(var b in slot.meshData.umaBones)
                {
                    var t=bones[b.hash];t.SetParent(bones.TryGetValue(b.parent,out var parent)?parent:rig.transform,false);
                    t.localPosition=b.position;t.localRotation=b.rotation;t.localScale=b.scale;
                }
                var byName=bones.Values.ToDictionary(t=>t.name);
                var rest=bones.Values.ToDictionary(t=>t.name,t=>new Rest(t));
                // The standard rig's Head Y axis points to the crown. Its lip position identifies forward.
                var head=byName["Head"];
                Vector3 up=head.up.normalized;
                Vector3 forward=Vector3.ProjectOnPlane(byName["LipsSuperior"].position-head.position,up).normalized;
                Vector3 right=Vector3.Cross(up,forward).normalized;
                var originals=group.expressions.Where(e=>e!=null&&!IsGeneratedId(e.id)).ToList();
                var existingMild=AssetDatabase.LoadAssetAtPath<UMAExpressionGroup>(MildGroupPath);
                var mildOriginals=existingMild!=null
                    ? existingMild.expressions.Where(e=>e!=null&&!IsGeneratedId(e.id)).ToList()
                    : originals.Select(CloneDefinition).ToList();
                var mildGroup=LoadOrCreate<UMAExpressionGroup>(MildGroupPath);
                group.expressions=new List<UMAExpressionDefinition>(originals);
                // Copy definitions, not mutable references. DNA assets for existing expressions stay shared.
                // Subsequent rebuilds also preserve custom non-viseme entries in the mild group.
                mildGroup.expressions=mildOriginals;
                int priority=originals.Concat(mildOriginals).Select(e=>e.priority).DefaultIfEmpty(-1).Max()+1;
                var changed=new List<Object>();
                foreach(bool mild in new[]{false,true})
                for(int index=0;index<Names.Length;index++)
                {
                    foreach(var pair in rest)pair.Value.Restore(byName[pair.Key]);
                    var shape=Specification(Names[index],mild);
                    Author(shape,Names[index],byName,up,forward,right);
                    string stem="viseme_"+Names[index]+(mild?"_mild":"");
                    string folder=VisemeFolder+(mild?"/Mild/":"/Full/");
                    var pose=LoadOrCreate<UMABonePose>(folder+stem+"_pose.asset");
                    pose.poses=byName.Values.OrderBy(t=>t.name,StringComparer.Ordinal).Select(t=>rest[t.name].Delta(t)).Where(p=>p!=null).ToArray();
                    pose.tweenPoses=Array.Empty<UMABonePose>();pose.tweenWeights=Array.Empty<float>();pose.mixerPose=false;
                    EditorUtility.SetDirty(pose);changed.Add(pose);
                    var dna=LoadOrCreate<DNA>(folder+stem+"_DNA.asset");
                    dna.displayName=Names[index]+(mild?" (mild)":"");dna.defaultValue=0;
                    dna.description="UMA 3 "+(mild?"mild":"emphasized")+" "+Names[index]+" viseme. 0 = neutral; 1 = target. Drive through DynamicExpressionPlayer; keep combined speech weights at or below 1.";
                    dna.effects=new List<DNAEffect>{new DNAEffect_BonePose{bonePose=pose,minMapping=0,maxMapping=1,curve=AnimationCurve.Linear(0,0,1,1),isBasePose=false}};
                    EditorUtility.SetDirty(dna);changed.Add(dna);
                    (mild?mildGroup:group).expressions.Add(new UMAExpressionDefinition{
                        id="viseme_"+Names[index],displayName=Names[index]+" - "+Label(Names[index]),dna=dna,
                        roles=ExpressionRole.Viseme,affectedJoints=ExpressionJoint.Jaw|ExpressionJoint.Other,
                        priority=priority+index,blendMode=ExpressionBlendMode.Override,responseTime=0});
                }
                EditorUtility.SetDirty(group);EditorUtility.SetDirty(mildGroup);
                changed.Add(group);changed.Add(mildGroup);
                var messages=new List<ExpressionValidationMessage>();
                if(!group.Validate(messages)||!mildGroup.Validate(messages))throw new InvalidOperationException("Generated expression group failed validation.");
                foreach(var asset in changed)AssetDatabase.SaveAssetIfDirty(asset);
                Debug.Log("Created 30 UMA 3 viseme poses and 30 DNA assets; assigned full and mild expression groups.");
            }
            finally{Object.DestroyImmediate(rig);}
        }
        public static bool IsGeneratedId(string id)=>Names.Any(n=>string.Equals(id,"viseme_"+n,StringComparison.OrdinalIgnoreCase));
        static string Label(string n)
        {
            string[] labels={"Silence","Lips closed (p/b/m)","Lip to teeth (f/v)","Tongue between teeth (th)","Tongue to ridge (t/d)","Back tongue (k/g)","Rounded fricative (ch/sh/j)","Teeth close (s/z)","Tongue raised (n/l)","R sound","Open vowel","Wide vowel (eh)","Spread vowel (ih)","Round vowel (oh)","Pursed vowel (oo)"};
            return labels[Array.IndexOf(Names,n)];
        }
        static UMAExpressionDefinition CloneDefinition(UMAExpressionDefinition e)=>new UMAExpressionDefinition{
            id=e.id,displayName=e.displayName,dna=e.dna,roles=e.roles,affectedJoints=e.affectedJoints,priority=e.priority,
            blendMode=e.blendMode,responseTime=e.responseTime,blinkClosedValue=e.blinkClosedValue};
        static T LoadOrCreate<T>(string path) where T:ScriptableObject
        {
            var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value!=null)return value;
            if(System.IO.File.Exists(path))throw new InvalidOperationException("An existing asset could not be loaded as "+typeof(T).Name+" at "+path+". Nothing at this path was overwritten.");
            value=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);return value;
        }
        static void Author(Shape s,string name,Dictionary<string,Transform>b,Vector3 up,Vector3 forward,Vector3 right)
        {
            var jaw=b["Mandible"];
            var corners=new[]{b["LeftLips"],b["RightLips"]};
            var cornerRest=corners.Select(t=>t.position).ToArray();
            jaw.rotation=Quaternion.AngleAxis(s.jaw,right)*jaw.rotation;
            for(int i=0;i<corners.Length;i++)corners[i].position=Vector3.Lerp(cornerRest[i],corners[i].position,.55f);
            Vector3 center=(corners[0].position+corners[1].position)*.5f;
            foreach(string n in new[]{"LeftLips","RightLips","LipsSuperior","LeftLipsSuperiorMiddle","RightLipsSuperiorMiddle","LipsInferior","LeftLipsInferior","RightLipsInferior"})
            {
                var t=b[n];float lateral=Vector3.Dot(t.position-center,right);
                t.position+=right*lateral*(s.width-1)+forward*(s.protrude*.001f);
                if(n.Contains("Superior"))t.position+=up*(s.upper*.001f);
                if(n.Contains("Inferior"))t.position+=up*(s.lower*.001f);
                if(name=="FF"&&n.Contains("Inferior"))t.position-=forward*.0018f;
            }
            b["Tongue01"].position+=(forward*s.tongueForward+up*s.tongueUp)*.001f;
            // Opposite the jaw-opening direction raises the tongue tip.
            b["Tongue02"].rotation=Quaternion.AngleAxis(-s.tongueTip,right)*b["Tongue02"].rotation;
        }
        struct Rest
        {
            Vector3 position,scale;Quaternion rotation;
            public Rest(Transform t){position=t.localPosition;rotation=t.localRotation;scale=t.localScale;}
            public void Restore(Transform t){t.localPosition=position;t.localRotation=rotation;t.localScale=scale;}
            public UMABonePose.PoseBone Delta(Transform t)
            {
                var delta=t.localPosition-position;var rot=Quaternion.Inverse(rotation)*t.localRotation;
                if(delta.sqrMagnitude<1e-14f&&Quaternion.Angle(Quaternion.identity,rot)<.001f)return null;
                return new UMABonePose.PoseBone{bone=t.name,hash=UMAUtils.StringToHash(t.name),position=delta,rotation=rot.normalized,
                    scale=new Vector3(t.localScale.x/scale.x,t.localScale.y/scale.y,t.localScale.z/scale.z),category="Speech / mouth",enabled=true};
            }
        }
    }
}

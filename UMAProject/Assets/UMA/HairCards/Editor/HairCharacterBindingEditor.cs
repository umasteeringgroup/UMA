using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal sealed class HairCharacterBindingEditor : IDisposable
    {
        private UnityEngine.Object source;
        private List<HairCharacterBindingBuilder.Input> inputs;
        private readonly HashSet<string> checkedSlots=new HashSet<string>();
        private Vector3 position,rotation;
        private HairCharacterBindingBuilder.DonorAxes axes;
        private float scale=1, tolerance=.03f;
        private HairCharacterBindingAsset candidate;
        private HairGroomAsset owner;
        private HairAvatarPreview preview;
        private Material previewMaterial;
        private string status;
        internal void Draw(HairCardStage stage)
        {
            var groom=stage.Groom;
            if(owner!=groom)
            {
                Dispose();owner=groom;source=groom.CharacterBinding?.race;inputs=null;status=null;
                var saved=groom.CharacterBinding;
                position=saved!=null ? saved.alignmentPosition : Vector3.zero;
                rotation=saved!=null ? saved.alignmentRotation : Vector3.zero;
                axes=saved!=null ? (HairCharacterBindingBuilder.DonorAxes)saved.axisPreset : HairCharacterBindingBuilder.DonorAxes.AsSaved;
                scale=saved!=null ? saved.alignmentScale : 1;tolerance=saved!=null ? saved.maximumDistance : .03f;
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Bind Character / Race",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Attach body slots and a skeleton without replacing the authoring mesh or changing the hairstyle. Choose a generated character for its current body shape, or a race for its base-slot rest geometry (no DNA applied).",MessageType.Info);
            var binding=groom.CharacterBinding;
            if(binding!=null)
            {
                using(new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Saved binding",binding,typeof(HairCharacterBindingAsset),false);
                using(new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Bound race",binding.race,typeof(RaceData),false);
                EditorGUILayout.LabelField($"{binding.slots.Length} slots · {binding.boneNames.Length} bones · saved donor snapshot",EditorStyles.wordWrappedMiniLabel);
                if(GUILayout.Button("Detach Character Binding…") && EditorUtility.DisplayDialog("Detach Character Binding?",
                    "Keep the hairstyle and all source assets. Disable this groom's saved donor and clear its scalp modifier assignment. The binding asset is retained for Undo/recovery.","Detach","Cancel"))
                {
                    Undo.RecordObject(groom,"Detach Hair Character");groom.CharacterBinding=null;
                    foreach(var group in groom.Groups) { group.generation.scalp.meshModifier=null;group.generation.scalp.slots.Clear();group.generation.scalp.bindingTopology=null; }
                    HairGroomCommands.Commit(groom);stage.RefreshBoundCharacter();
                }
            }
            EditorGUI.BeginChangeCheck();
            var next=EditorGUILayout.ObjectField("Character or Race",source,typeof(UnityEngine.Object),true);
            if(EditorGUI.EndChangeCheck()) { Dispose();source=next;inputs=null;status=null;position=rotation=Vector3.zero;scale=1;axes=HairCharacterBindingBuilder.DonorAxes.AsSaved; }
            if(source!=null && inputs==null)
                try { inputs=HairCharacterBindingBuilder.Inputs(source);SelectBaseSlots(); }
                catch(Exception e) { status=e.Message;inputs=new List<HairCharacterBindingBuilder.Input>(); }
            if(inputs!=null && inputs.Count>0)
            {
                EditorGUILayout.LabelField("Body slots — exclude hair, clothing, eyes and accessories",EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("Base race slots")) { SelectBaseSlots();Dispose(); }
                    if(GUILayout.Button("All")) { foreach(var input in inputs) checkedSlots.Add(input.slot.slotName);Dispose(); }
                    if(GUILayout.Button("None")) { checkedSlots.Clear();Dispose(); }
                }
                foreach(var input in inputs)
                {
                    bool selected=checkedSlots.Contains(input.slot.slotName);
                    bool value=EditorGUILayout.ToggleLeft(input.slot.slotName,selected);
                    if(value) checkedSlots.Add(input.slot.slotName); else checkedSlots.Remove(input.slot.slotName);
                }
                axes=(HairCharacterBindingBuilder.DonorAxes)EditorGUILayout.EnumPopup("Donor coordinates",axes);
                EditorGUILayout.LabelField("For PointySwept + native UMA30 body slots: Z Up To Y Up Flip Forward.",EditorStyles.wordWrappedMiniLabel);
                position=EditorGUILayout.Vector3Field("Donor position (m)",position);
                rotation=EditorGUILayout.Vector3Field("Donor rotation",rotation);
                scale=EditorGUILayout.FloatField("Donor scale",scale);
                if(GUILayout.Button("Reset Alignment")) { position=rotation=Vector3.zero;scale=1;axes=HairCharacterBindingBuilder.DonorAxes.AsSaved;Dispose(); }
                tolerance=EditorGUILayout.Slider(new GUIContent("Match tolerance (m)","Maximum distance from painted vertices and guide roots to the selected body. Do not use a large tolerance to hide misalignment."),tolerance,.001f,.1f);
                if(EditorGUI.EndChangeCheck()) { Dispose();status=null; }
                using(new EditorGUI.DisabledScope(checkedSlots.Count==0 || !float.IsFinite(scale) || scale<=0))
                    if(GUILayout.Button("Preview Alignment"))
                    {
                        Dispose();
                        try
                        {
                            candidate=HairCharacterBindingBuilder.Build(groom,inputs.Where(i=>checkedSlots.Contains(i.slot.slotName)).ToList(),Matrix4x4.TRS(position,Quaternion.Euler(rotation),Vector3.one*scale)*HairCharacterBindingBuilder.AxisMatrix(axes),tolerance);
                            candidate.axisPreset=(int)axes;candidate.alignmentPosition=position;candidate.alignmentRotation=rotation;candidate.alignmentScale=scale;
                            status=HairCharacterBindingBuilder.AlignmentError(groom,candidate) ?? "Alignment passed. Cyan is the donor; the authoring surface and hair are unchanged. Inspect the head from all sides before attaching.";
                            previewMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags=HideFlags.HideAndDontSave,color=new Color(.1f,.8f,.85f,.3f),renderQueue=3000 };
                            // A ghosted donor allows the original scalp and hairline to remain visible.
                            previewMaterial.SetOverrideTag("RenderType","Transparent");
                            previewMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                            previewMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            previewMaterial.SetInt("_ZWrite",0);previewMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            previewMaterial.EnableKeyword("_ALPHABLEND_ON");
                            if(previewMaterial.HasProperty("_Surface")) previewMaterial.SetFloat("_Surface",1);
                            if(previewMaterial.HasProperty("_Mode")) previewMaterial.SetFloat("_Mode",3);
                            previewMaterial.SetShaderPassEnabled("ShadowCaster",false);
                            preview=HairAvatarPreview.Build(candidate,previewMaterial);
                            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(preview.Root,stage.scene);
                            SceneView.RepaintAll();
                        }
                        catch(Exception e) { Dispose();status=e.Message; }
                    }
                using(new EditorGUI.DisabledScope(candidate==null))
                    if(GUILayout.Button("Attach Validated Binding…"))
                    {
                        string error=HairCharacterBindingBuilder.AlignmentError(groom,candidate);
                        if(error!=null) { status=error;EditorUtility.DisplayDialog("Cannot Attach Body",error,"OK"); }
                        else if(EditorUtility.DisplayDialog("Attach Character Binding?","Preserve all paint, guides, sculpt passes and hair materials. Save the selected body and skeleton as a new donor asset and set its race as the bake's compatible race. Replace this groom's binding and clear the old scalp modifier assignment; recreate scalp shading for the real slots afterward. Existing source assets are never modified.","Attach","Cancel"))
                        {
                            try { HairCharacterBindingBuilder.Save(groom,candidate);candidate=null;Dispose();stage.RefreshBoundCharacter();status="Character attached. Recreate Scalp Vertex Shading for these slots, then Validate & Bake."; }
                            catch(Exception e) { status=e.Message;EditorUtility.DisplayDialog("Binding Failed",status,"OK"); }
                        }
                    }
                if(candidate!=null && GUILayout.Button("Hide Alignment Preview")) Dispose();
            }
            if(!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status,MessageType.Info);
        }
        private void SelectBaseSlots()
        {
            checkedSlots.Clear();
            var race=inputs?.FirstOrDefault()?.race;
            var baseSlots=race?.baseRaceRecipe?.GetCachedRecipe(loadSlots:true)?.slotDataList;
            var names=baseSlots!=null ? new HashSet<string>(baseSlots.Where(s=>s?.asset!=null).Select(s=>s.slotName)) : null;
            foreach(var input in inputs) if(names==null || names.Contains(input.slot.slotName)) checkedSlots.Add(input.slot.slotName);
        }
        public void Dispose()
        {
            preview?.Dispose();preview=null;
            if(previewMaterial!=null) UnityEngine.Object.DestroyImmediate(previewMaterial);previewMaterial=null;
            HairCharacterBindingBuilder.Dispose(candidate);candidate=null;
        }
    }
}

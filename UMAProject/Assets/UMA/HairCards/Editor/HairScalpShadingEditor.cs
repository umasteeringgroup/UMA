using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public static class HairScalpShadingEditor
    {
        internal static bool CreateAndApply(HairCardStage stage, HairGroup group)
        {
            if (stage?.Groom == null || group == null || group.locked) return false;
            var settings = group.generation.scalp;
            var bindings = CaptureBindings(stage, group);
            if (bindings == null || bindings.Count == 0)
            {
                EditorUtility.DisplayDialog("Scalp Modifier Needs Slot Binding", "Attach a valid body using Source & Setup → Bind Character / Race, or open the groom with its original built UMA character. The modifier needs verified body-slot correspondence; guessing could color the wrong vertices.", "OK");
                return false;
            }
            string path = AssetDatabase.GetAssetPath(stage.Groom);
            if (string.IsNullOrEmpty(path)) { EditorUtility.DisplayDialog("Save Groom First", "Save the groom as an asset before creating its scalp Mesh Modifier.", "OK"); return false; }
            Undo.RecordObject(stage.Groom, "Bind Scalp Vertex Shading");
            var modifier = settings.meshModifier as UMA.MeshModifier;
            if (modifier == null)
            {
                modifier = ScriptableObject.CreateInstance<UMA.MeshModifier>(); modifier.name = stage.Groom.name + " " + group.name + " Scalp Colors";
                HairGenerationEditor.SaveResourceNear(stage.Groom, modifier);
            }
            else Undo.RecordObject(modifier, "Update Scalp Mesh Modifier");
            settings.enabled = true;
            Populate(modifier, stage.Groom, null, bindings);
            settings.slots = bindings; settings.bindingTopology = stage.Groom.SourceTopologySignature;
            // All groups share one composed modifier. Independent replacement-color modifiers
            // would overwrite one another where coverage/detail groups overlap.
            foreach (var owner in stage.Groom.Groups)
            {
                owner.generation.scalp.meshModifier = modifier;
                owner.generation.scalp.slots = new List<HairScalpSlotBinding>(bindings);
                owner.generation.scalp.bindingTopology = stage.Groom.SourceTopologySignature;
            }
            EditorUtility.SetDirty(modifier); stage.TrackResourceEdit(modifier); HairGroomCommands.Commit(stage.Groom);
            stage.ApplyScalpPreview(); AssetDatabase.SaveAssetIfDirty(modifier);
            return true;
        }

        internal static List<HairScalpSlotBinding> CaptureBindings(HairCardStage stage, HairGroup group)
        {
            var groom = stage.Groom; var settings = group.generation.scalp;
            if(groom.CharacterBinding != null)
            {
                if(HairCharacterBindingUtility.Validate(groom)!=null) return null;
                var result=new List<HairScalpSlotBinding>();
                foreach(var slot in groom.CharacterBinding.slots)
                    result.Add(new HairScalpSlotBinding { slotName=slot.name,sourceVertexStart=slot.vertexStart,vertexCount=slot.vertexCount });
                return result;
            }
            var data = stage.SourceAvatar?.umaData;
            if (data != null)
            {
                var renderer = stage.SourceRendererForScalp;
                var renderers = data.GetRenderers(); int rendererIndex = renderers == null ? -1 : Array.IndexOf(renderers, renderer);
                if (renderer != null && HairMeshUtility.ComputeTopologySignature(renderer.sharedMesh) == groom.SourceTopologySignature)
                {
                    var slots = data.umaRecipe?.slotDataList; var result = new List<HairScalpSlotBinding>();
                    if (slots != null) foreach (var slot in slots)
                    {
                        if (slot?.asset?.meshData == null || slot.skinnedMeshRenderer != rendererIndex || slot.vertexOffset < 0 ||
                            slot.vertexOffset + slot.asset.meshData.vertexCount > groom.SourceVertexCount) continue;
                        result.Add(new HairScalpSlotBinding { slotName = slot.slotName, sourceVertexStart = slot.vertexOffset, vertexCount = slot.asset.meshData.vertexCount });
                    }
                    if (result.Count > 0) return result;
                }
            }
            if (settings.bindingTopology == groom.SourceTopologySignature && settings.slots?.Count > 0) return settings.slots;
            // SourceSlot can actually be a combined renderer name. Only a proven SlotDataAsset
            // origin is safe as a single-slot fallback.
            if (GlobalObjectId.TryParse(groom.SourceObjectId, out var originId) &&
                GlobalObjectId.GlobalObjectIdentifierToObjectSlow(originId) is SlotDataAsset origin &&
                origin.meshData?.vertexCount == groom.SourceVertexCount)
                return new List<HairScalpSlotBinding> { new HairScalpSlotBinding { slotName = origin.slotName, sourceVertexStart = 0, vertexCount = groom.SourceVertexCount } };
            return null;
        }

        internal static void ValidateBindings(HairGroomAsset groom, HairValidationReport report)
        {
            if (!groom.BakeSettings.createWardrobeRecipe) return;
            foreach (var group in groom.Groups)
                if (group?.enabled == true && group.generation?.scalp?.enabled == true)
                {
                    var scalp = group.generation.scalp;
                    if (!(scalp.meshModifier is UMA.MeshModifier) || scalp.slots?.Count == 0 || scalp.bindingTopology != groom.SourceTopologySignature)
                        report.Add(HairValidationSeverity.Error, HairValidationCode.ScalpBindingMissing,
                            "Scalp shading needs a bound Mesh Modifier. Attach the body in Source & Setup if needed, then select Scalp Vertex Shading → Create / Update & Apply Scalp Modifier.", group.Id);
                }
        }

        internal static void SynchronizeRecipe(HairGroomAsset groom, UMA.CharacterSystem.UMAWardrobeRecipe recipe, Action<UnityEngine.Object> backup)
        {
            foreach (var group in groom.Groups)
                if (group?.generation?.scalp?.meshModifier is UMA.MeshModifier owned) recipe.MeshModifiers.RemoveAll(m => m == owned);
            var active = groom.Groups.Find(g => g?.enabled == true && g.generation?.scalp?.enabled == true);
            if (active == null) return;
            var settings = active.generation.scalp;
            if (!(settings.meshModifier is UMA.MeshModifier modifier) || settings.bindingTopology != groom.SourceTopologySignature || settings.slots.Count == 0)
                throw new InvalidOperationException("Bind the scalp Mesh Modifier before baking.");
            backup?.Invoke(modifier);
            Populate(modifier, groom, null, settings.slots); EditorUtility.SetDirty(modifier);
            recipe.MeshModifiers.Add(modifier);
        }

        internal static Color32[] BoundColors(HairGroomAsset groom)
        {
            var binding=groom.CharacterBinding;
            var white=new Color32[groom.SourceVertexCount];Array.Fill(white,new Color32(255,255,255,255));
            var tint=new List<Color32>();HairScalpShadingUtility.Evaluate(groom,white,tint,new Dictionary<string,HairSurfaceFields>());
            var original=binding.donorMesh.colors32;var result=new Color32[binding.donorMesh.vertexCount];
            for(int i=0;i<result.Length;i++)
            {
                Color32 baseline=original.Length==result.Length ? original[i] : new Color32(255,255,255,255);
                Color color=HairSurfaceSkinning.Color(binding.scalpSamples[i],tint,new Color32(255,255,255,255));
                result[i]=(Color)baseline*color;
            }
            return result;
        }
        private static void PopulateBound(UMA.MeshModifier modifier,HairGroomAsset groom)
        {
            string error=HairCharacterBindingUtility.Validate(groom);if(error!=null) throw new InvalidOperationException(error);
            var binding=groom.CharacterBinding;var colors=BoundColors(groom);var baseline=binding.donorMesh.colors32;
            var entries=new List<UMA.MeshModifier.Modifier>();
            foreach(var slot in binding.slots)
            {
                var adjustments=new VertexColorAdjustmentCollection();
                for(int i=0;i<slot.vertexCount;i++)
                {
                    int vertex=slot.vertexStart+i;Color32 before=baseline.Length==colors.Length ? baseline[vertex] : new Color32(255,255,255,255);
                    if(before.Equals(colors[vertex])) continue;
                    adjustments.Add(new VertexColorAdjustment { slotName=slot.name,vertexIndex=i,color=colors[vertex],weight=1 });
                }
                if(adjustments.Count()>0) entries.Add(new UMA.MeshModifier.Modifier { ModifierName="Hair Scalp Shading",SlotName=slot.name,Scale=1,adjustments=adjustments,keepAsIs=true });
            }
            modifier.EditorModifiers=entries;modifier.RuntimeModifiers=new List<UMA.MeshModifier.Modifier>(entries);
        }

        /// <summary>Rebuild an owned scalp modifier from the original source colors. Callers must
        /// supply verified slot vertex ranges for this exact source topology, not name guesses.
        /// Pass null for group to compose every enabled group. Does not save or alter the source.</summary>
        public static void Populate(UMA.MeshModifier modifier, HairGroomAsset groom, HairGroup group, IReadOnlyList<HairScalpSlotBinding> slots)
        {
            if (modifier == null || groom == null || groom.SourceMesh == null || slots == null)
                throw new ArgumentNullException("A modifier, readable source groom and verified slot bindings are required.");
            if(groom.CharacterBinding!=null) { PopulateBound(modifier,groom);return; }
            foreach (var slot in slots)
                if (slot == null || string.IsNullOrWhiteSpace(slot.slotName) || slot.sourceVertexStart < 0 || slot.vertexCount <= 0 ||
                    (long)slot.sourceVertexStart + slot.vertexCount > groom.SourceVertexCount)
                    throw new ArgumentException("Invalid scalp slot binding. No modifier was changed.");
            var colors = new List<Color32>();
            // Store multiplicative shading as targets from the source baseline, not incremental
            // edits to an already shaded preview. Repeated updates must be idempotent.
            var baseline = groom.SourceMesh.colors32;
            HairScalpShadingUtility.Evaluate(groom, baseline, colors, new Dictionary<string, HairSurfaceFields>(), group);
            modifier.EditorModifiers = new List<UMA.MeshModifier.Modifier>();
            modifier.RuntimeModifiers = new List<UMA.MeshModifier.Modifier>();
            foreach (var slot in slots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.slotName) || slot.sourceVertexStart < 0 || slot.vertexCount < 0 ||
                    slot.sourceVertexStart + slot.vertexCount > colors.Count) throw new ArgumentException("Invalid scalp slot binding.");
                var collection = new VertexColorAdjustmentCollection();
                for (int i = 0; i < slot.vertexCount; i++)
                {
                    int source = slot.sourceVertexStart + i;
                    Color32 before = baseline.Length == colors.Count ? baseline[source] : new Color32(255, 255, 255, 255);
                    if (before.Equals(colors[source])) continue;
                    collection.Add(new VertexColorAdjustment { vertexIndex = i, slotName = slot.slotName, color = colors[source], weight = 1f });
                }
                if (collection.Count() == 0) continue;
                var entry = new UMA.MeshModifier.Modifier { ModifierName = "Hair Scalp Shading", SlotName = slot.slotName, Scale = 1f, adjustments = collection, keepAsIs = true };
                modifier.EditorModifiers.Add(entry); modifier.RuntimeModifiers.Add(entry);
            }
        }
    }
}

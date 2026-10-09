using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private readonly Dictionary<string,string> automaticLinkInputs=new Dictionary<string,string>();
        private bool linkedUpdateStarted;
        private double nextLinkedUpdate;
        private readonly Queue<Action> afterLinkedUpdates = new Queue<Action>();
        internal bool WaitForLinkedOutput(Action resume)
        {
            if (LinkedOutputReady()) return false;
            if (resume != null) afterLinkedUpdates.Enqueue(resume);
            ShowWorkspaceStatus("Updating linked layers before continuing...");
            return true;
        }
        private bool LinkedOutputReady()
        {
            // Saving/exporting must not bypass a pending refresh while a gesture or another save owns the data.
            if (regeneratingStaleLayers || IsPersistenceActive || pluginLayerCancellation != null || strokeActive ||
                uvStrokeActive || GUIUtility.hotControl != 0) return false;
            for (int pass = 0; pass < 32; pass++)
            {
                nextLinkedUpdate = 0;
                UpdateLinkedPlugins();
                if (pluginLayerCancellation != null) return false;
                if (!linkedUpdateStarted) return true;
            }
            return false;
        }
        private void ResumeAfterLinkedUpdates()
        {
            if (afterLinkedUpdates.Count == 0 || !LinkedOutputReady()) return;
            // Keep every requested action; an autosave must not replace an explicit save or export.
            afterLinkedUpdates.Dequeue().Invoke();
        }
        private void UpdateLinkedPlugins()
        {
            linkedUpdateStarted=false;
            if(regeneratingStaleLayers || controller?.Textures==null || controller.Plugins==null || IsPersistenceActive || pluginLayerCancellation!=null ||
                strokeActive || uvStrokeActive || GUIUtility.hotControl!=0 || EditorApplication.timeSinceStartup<nextLinkedUpdate)return;
            nextLinkedUpdate=EditorApplication.timeSinceStartup+.3;
            controller.Textures.RefreshLayerLinks();
            foreach(var set in controller.Textures.Sets)
            {
                var baseHash = new HashCode();
                foreach (var channel in set.channels)
                {
                    baseHash.Add(channel.Key);
                    baseHash.Add(channel.Value.editable?.Revision ?? 0);
                    baseHash.Add(channel.Value.editable == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(channel.Value.editable));
                }
                string signature = baseHash.ToHashCode().ToString();
                bool valid = true, hasLinkedInput = false;
                foreach(var layer in set.layers)
                {
                    if(!string.IsNullOrEmpty(layer.linkError))valid=false;
                    string key=set.persistentId+"/"+layer.id;
                    if(layer.kind==TexturePaintLayerKind.Plugin && hasLinkedInput && valid)
                    {
                        bool changed=!automaticLinkInputs.TryGetValue(key,out string previous)||previous!=signature;
                        if(changed)
                        {
                            automaticLinkInputs[key]=signature;
                            var plugin=controller.Plugins.FindCommand(layer.pluginId);
                            if(plugin!=null){linkedUpdateStarted=true;RegeneratePluginLayer(set,layer,plugin,true);return;}
                        }
                    }
                    if(!string.IsNullOrEmpty(layer.layerMask?.pluginId) && hasLinkedInput && valid)
                    {
                        string maskKey=key+"/mask";
                        if(!automaticLinkInputs.TryGetValue(maskKey,out string priorMask)||priorMask!=signature)
                        {
                            automaticLinkInputs[maskKey]=signature;var plugin=controller.Plugins.FindCommand(layer.layerMask.pluginId);
                            if(plugin!=null){linkedUpdateStarted=true;RegenerateLayerMaskPlugin(set,layer,plugin,true);return;}
                        }
                    }
                    hasLinkedInput |= layer.projectionSettings?.garment?.HasReferences == true || layer.links?.HasLinks == true || layer.layerMask?.effects?.HasReferences == true;
                    // Generators read the entire stack below them, including ordinary paint and fill layers.
                    signature = Hash128.Compute(signature + "|" + TexturePaintLinkEvaluator.ContentSignature(layer)).ToString();
                }
            }
        }
        private bool linksExpanded = true;
        private void DrawLayerLinks(TextureSet set, TexturePaintLayer layer)
        {
            linksExpanded = EditorGUILayout.Foldout(linksExpanded, "References and Instances", true);
            if (!linksExpanded) return;
            var links = layer.links?.Clone() ?? new TexturePaintLayerLinks();
            EditorGUI.BeginChangeCheck();
            links.anchorName = EditorGUILayout.TextField(new GUIContent("Anchor Name", "Name this layer's output so other layers can refer to it."), links.anchorName);
            if (links.instance?.IsSet == true)
            {
                links.instance = DrawLayerReference("Instance Source", set, layer, links.instance, false);
                EditorGUILayout.HelpBox(layer.kind == TexturePaintLayerKind.Projection || layer.kind == TexturePaintLayerKind.Fill
                    ? "Material settings follow the source. Placement, size and painted mask remain local."
                    : "Content follows the source in UV space. Offset, scale, rotation and mask remain local.", MessageType.None);
            }
            else if (layer.kind == TexturePaintLayerKind.Reference)
            {
                links.content = DrawLayerReference("Content Source", set, layer, links.content, true);
                links.outputChannel = DrawReferenceChannel("Output Channel",links.outputChannel,set.channels.Keys);
                links.tint = EditorGUILayout.ColorField("Tint", links.tint);
            }
            if (layer.kind == TexturePaintLayerKind.Reference)
            {
                links.tiling = EditorGUILayout.Vector2Field("UV Scale", links.tiling);
                links.offset = EditorGUILayout.Vector2Field("UV Offset", links.offset);
                links.rotation = EditorGUILayout.FloatField("UV Rotation", links.rotation);
            }
            links.mask = DrawLayerReference("Live Mask Source", set, layer, links.mask, true);
            if (EditorGUI.EndChangeCheck()) ChangeLayerLinks(set, layer, links);
            if (!string.IsNullOrEmpty(layer.linkError)) EditorGUILayout.HelpBox(layer.linkError, MessageType.Warning);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reference This Layer")) CreateLinkedLayer(set,layer,false);
            if (GUILayout.Button("Create Instance")) CreateLinkedLayer(set,layer,true);
            GUILayout.EndHorizontal();
            if ((layer.links?.HasLinks == true || layer.kind == TexturePaintLayerKind.Reference) && GUILayout.Button("Make Independent")) MakeLinkedLayerIndependent(set,layer);
        }
        private TexturePaintLayerReference DrawLayerReference(string label, TextureSet set, TexturePaintLayer destination,
            TexturePaintLayerReference reference, bool components, TexturePaintChannel? preferredChannel = null,
            TexturePaintReferenceComponent? preferredComponent = null)
        {
            var labels = new List<string> { "None" };
            var sources = new List<TexturePaintLayerReference> { null };
            int selected = 0;
            IReadOnlyList<TextureSet> all = controller?.Textures?.Sets ?? new[] { set };
            var resolved=TexturePaintLinkEvaluator.Resolve(all,set,reference,out _);
            foreach (TextureSet owner in all) foreach (TexturePaintLayer candidate in owner.layers)
            {
                if (ReferenceEquals(candidate,destination)) continue;
                if(!components && destination.kind!=TexturePaintLayerKind.Reference && candidate.kind!=destination.kind)continue;
                string anchor = string.IsNullOrWhiteSpace(candidate.links?.anchorName) ? candidate.name : candidate.links.anchorName;
                string target=owner.surface?.slotName;
                foreach(var slot in owner.surface?.slots ?? new List<SlotData>())
                    if(slot?.asset!=null && slot.asset.udimTileNumber>=1001){target+=" ["+slot.asset.udimTileNumber+"]";break;}
                labels.Add($"{owner.Name} / {target} / {owner.layers.IndexOf(candidate)+1}: {anchor}");
                sources.Add(TexturePaintLayerReference.To(owner,candidate));
                if(ReferenceEquals(resolved,candidate))selected=sources.Count-1;
            }
            if (reference?.IsSet == true && selected == 0)
            { selected=labels.Count;labels.Add("Missing source (cached)");sources.Add(reference.Clone()); }
            int next = EditorGUILayout.Popup(label,selected,labels.ToArray());
            var result = next == selected ? reference?.Clone() : sources[next]?.Clone();
            if(next!=selected && result!=null)
            {
                var newSource=TexturePaintLinkEvaluator.Resolve(all,set,result,out _);
                if(newSource!=null && newSource.TryGetFirstAuthoredChannel(out var channel))result.channel=channel;
                if(preferredChannel.HasValue && newSource!=null &&
                    (newSource.channels.ContainsKey(preferredChannel.Value) || newSource.kind==TexturePaintLayerKind.Group))
                    result.channel=preferredChannel.Value;
                if(preferredComponent.HasValue)result.component=preferredComponent.Value;
            }
            if (result?.IsSet == true && components)
            {
                EditorGUI.indentLevel++;
                result.component=(TexturePaintReferenceComponent)EditorGUILayout.EnumPopup("Read",result.component);
                var source=TexturePaintLinkEvaluator.Resolve(all,set,result,out var sourceSet);
                if(result.component!=TexturePaintReferenceComponent.Mask && source!=null)
                    result.channel=DrawReferenceChannel("Source Channel",result.channel,
                        source.kind==TexturePaintLayerKind.Group ? sourceSet.channels.Keys : source.channels.Keys);
                result.invert=EditorGUILayout.Toggle("Invert",result.invert);
                EditorGUI.indentLevel--;
            }
            return result;
        }
        private static TexturePaintChannel DrawReferenceChannel(string label,TexturePaintChannel current,IEnumerable<TexturePaintChannel> available)
        {
            var values=new List<TexturePaintChannel>(available);values.Sort();
            var names=values.ConvertAll(value=>value.ToString());
            int selected=values.IndexOf(current);
            if(selected<0){selected=values.Count;values.Add(current);names.Add(current+" (unavailable)");}
            return values[EditorGUILayout.Popup(label,selected,names.ToArray())];
        }
        private void ChangeLayerLinks(TextureSet set, TexturePaintLayer layer, TexturePaintLayerLinks links)
        {
            FinishProjectionEdit();
            if (!TryResolveLogicalPeers(set,layer,out List<TexturePaintLogicalLayerMember> peers,out string error))
            { ShowWorkspaceStatus(error);return; }
            var old = new Dictionary<TexturePaintLayer,TexturePaintLayerLinks>();
            foreach(var peer in peers) {old[peer.layer]=peer.layer.links?.Clone();peer.layer.links=links.Clone();peer.layer.linkSignature=null;}
            PushLightweightCommand("Change Layer References",
                ()=> {foreach(var peer in peers) {var current=peer.textureSet.layers.Find(item=>item.id==peer.layer.id);if(current==null)continue;current.links=old[peer.layer]?.Clone();current.linkSignature=null;}RefreshLinkedContent();},
                ()=> {foreach(var peer in peers) {var current=peer.textureSet.layers.Find(item=>item.id==peer.layer.id);if(current==null)continue;current.links=links.Clone();current.linkSignature=null;}RefreshLinkedContent();});
            RefreshLinkedContent();MarkDocumentDirty(peers);RepaintAll();
        }
        private void RefreshLinkedContent()
        {
            controller?.Textures?.RefreshLayerLinks();
            if(controller?.Textures!=null) foreach(var set in controller.Textures.Sets) set.BindPreviewTextures();
        }
        private void CreateLinkedLayer(TextureSet set, TexturePaintLayer source, bool instance)
        {
            FinishProjectionEdit();
            BeginLayerCreationUndo(instance?"Create Linked Instance":"Create Layer Reference");
            TexturePaintLayer layer;
            if(instance && (source.kind==TexturePaintLayerKind.Projection || source.kind==TexturePaintLayerKind.Fill))
            {
                layer=set.CloneLayer(source,source.name+" Instance");layer.links=new TexturePaintLayerLinks();
                int index=set.layers.IndexOf(source)+1;set.layers.Insert(index,layer);set.activeLayerIndex=index;
            }
            else {layer=set.AddLayer(source.name+(instance?" Instance":" Reference"));layer.kind=TexturePaintLayerKind.Reference;layer.links=new TexturePaintLayerLinks();}
            layer.parentId=source.parentId;
            set.layers.Remove(layer);int placement=set.layers.IndexOf(source)+1;set.layers.Insert(placement,layer);set.activeLayerIndex=placement;
            var reference=TexturePaintLayerReference.To(set,source);
            if(source.TryGetFirstAuthoredChannel(out var sourceChannel))reference.channel=sourceChannel;
            layer.links.outputChannel=set.GetChannel(reference.channel)!=null?reference.channel:selectedChannel;
            reference.component=instance?TexturePaintReferenceComponent.Color:TexturePaintReferenceComponent.Alpha;
            if(instance)layer.links.instance=reference;else layer.links.content=reference;
            CompleteLayerCreationUndo(layer);RefreshLinkedContent();SyncActiveLayerSelection(set);RepaintAll();
        }
        private void MakeLinkedLayerIndependent(TextureSet set,TexturePaintLayer layer)
        {
            FinishProjectionEdit();
            if(!TryResolveLogicalPeers(set,layer,out List<TexturePaintLogicalLayerMember> peers,out string error)) {ShowWorkspaceStatus(error);return;}
            var before=new List<LayerLocation>();var after=new List<LayerLocation>();
            foreach(var peer in peers)
            {
                var copy=peer.textureSet.CloneLayer(peer.layer,peer.layer.name,true);
                if(peer.layer.linkedMask!=null)
                {var mask=peer.textureSet.InitializeLayerMask(copy,1);mask.target.Reset(peer.layer.linkedMask,Color.white);mask.effects=new TexturePaintLayerMaskEffects();}
                copy.links=new TexturePaintLayerLinks {anchorName=peer.layer.links?.anchorName};
                if(copy.kind==TexturePaintLayerKind.Reference)copy.kind=TexturePaintLayerKind.Paint;
                int index=peer.textureSet.layers.IndexOf(peer.layer);
                before.Add(new LayerLocation{set=peer.textureSet,layer=peer.layer,index=index});after.Add(new LayerLocation{set=peer.textureSet,layer=copy,index=index});
            }
            for(int i=0;i<before.Count;i++)SwapLayerSnapshot(before[i].set,before[i].layer,after[i].layer,before[i].index);
            PushLightweightCommand("Make Layer Independent",
                ()=> {for(int i=0;i<before.Count;i++)SwapLayerSnapshot(before[i].set,after[i].layer,before[i].layer,before[i].index);RefreshLinkedContent();},
                ()=> {for(int i=0;i<before.Count;i++)SwapLayerSnapshot(after[i].set,before[i].layer,after[i].layer,after[i].index);RefreshLinkedContent();},
                ()=> {foreach(var item in before)DisposeLayerIfDetached(item.set,item.layer);foreach(var item in after)DisposeLayerIfDetached(item.set,item.layer);});
            MarkDocumentDirty();RepaintAll();
        }
    }
}

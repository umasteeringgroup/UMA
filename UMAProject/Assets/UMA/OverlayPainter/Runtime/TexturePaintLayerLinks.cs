using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UMA.TexturePaint
{
    public sealed partial class TextureStore
    {
        private TexturePaintLinkEvaluator linkEvaluator;
        public void RefreshLayerLinks()
        {
            foreach (TextureSet set in Sets) set.ownerStore = this;
            bool hasLinks = false;
            foreach (TextureSet set in Sets) foreach (TexturePaintLayer layer in set.layers)
                if (layer.projectionSettings?.garment?.HasReferences == true || layer.links?.HasLinks == true || layer.layerMask?.effects?.HasReferences == true || layer.layerMask?.referenceOutputs.Count > 0 || layer.linkedMask != null) { hasLinks = true; break; }
            if (!hasLinks) return;
            linkEvaluator ??= new TexturePaintLinkEvaluator();
            linkEvaluator.Refresh(Sets);
        }
    }
    /// <summary>Evaluates named layer references and instances in dependency order, never allowing cycles.</summary>
    public sealed class TexturePaintLinkEvaluator : IDisposable
    {
        private sealed class Node { public TextureSet set; public TexturePaintLayer layer; public int state; public bool valid; public string signature; }
        private readonly List<Node> nodes = new List<Node>();
        private readonly Dictionary<TexturePaintLayer, Node> byLayer = new Dictionary<TexturePaintLayer, Node>();
        private Material material;
        private bool refreshing;
        private IReadOnlyList<TextureSet> sets;
        public static TexturePaintLayer Resolve(IReadOnlyList<TextureSet> sets, TextureSet destination,
            TexturePaintLayerReference reference, out TextureSet owner)
        {
            owner = null;
            if (reference?.IsSet != true) return null;
            // Prefer the local logical member. Explicit cross-target links keep their source surface.
            if (!string.IsNullOrEmpty(reference.logicalLayerId))
                foreach (TexturePaintLayer candidate in destination.layers)
                    if (candidate.logicalLayerId == reference.logicalLayerId && candidate.paintTargetId == reference.paintTargetId)
                    { owner = destination; return candidate; }
            foreach (TextureSet set in sets)
                if (string.IsNullOrEmpty(reference.surfaceId) || set.persistentId == reference.surfaceId)
                    foreach (TexturePaintLayer candidate in set.layers)
                        if (candidate.id == reference.layerId || (!string.IsNullOrEmpty(reference.logicalLayerId) &&
                            candidate.logicalLayerId == reference.logicalLayerId && candidate.paintTargetId == reference.paintTargetId))
                        { owner = set; return candidate; }
            return null;
        }
        public void Refresh(IReadOnlyList<TextureSet> textureSets)
        {
            if (refreshing) return;
            refreshing = true; sets = textureSets;
            try
            {
                if (material == null)
                {
                    Shader shader = Shader.Find("Hidden/UMA/TexturePaint/LayerReference");
                    if (shader == null || !shader.isSupported) return;
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                nodes.Clear(); byLayer.Clear();
                foreach (TextureSet set in sets) foreach (TexturePaintLayer layer in set.layers)
                { var node = new Node { set = set, layer = layer }; nodes.Add(node); byLayer[layer] = node; }
                foreach (Node node in nodes) Visit(node);
            }
            finally { refreshing = false; }
        }
        private bool Visit(Node node)
        {
            if (node.state == 2) return node.valid;
            if (node.state == 1) { node.layer.linkError = "Circular layer reference. Break a link to restore updates."; return false; }
            node.state = 1; var layer = node.layer; bool valid = true;
            var garment = layer.projectionSettings?.garment;
            var dependencies = new List<Node>();
            var references = new List<TexturePaintLayerReference> { layer.links?.content, layer.links?.mask, layer.links?.instance };
            if (garment?.HasReferences == true)
            { references.Add(garment.foldInput); references.Add(garment.protectionInput); }
            if (layer.layerMask?.effects?.stack != null) foreach (var effect in layer.layerMask.effects.stack)
                if (effect.enabled && effect.kind == TexturePaintMaskEffectKind.LayerReference) references.Add(effect.reference);
            foreach (var reference in references)
            {
                if (reference?.IsSet != true) continue;
                TexturePaintLayer source = Resolve(sets,node.set,reference,out _);
                if (source == null || !byLayer.TryGetValue(source,out Node dependency))
                { layer.linkError = "A linked source is missing. Cached output is retained; relink or make independent."; valid = false; continue; }
                dependencies.Add(dependency);
                if (!Visit(dependency)) { layer.linkError = "A source has a missing or circular dependency. Cached output retained."; valid = false; }
            }
            if (layer.kind == TexturePaintLayerKind.Plugin || !string.IsNullOrEmpty(layer.layerMask?.pluginId))
                foreach(Node below in nodes) if(below.set==node.set && node.set.layers.IndexOf(below.layer)<node.set.layers.IndexOf(layer))
                { dependencies.Add(below);if(!Visit(below))valid=false; }
            if (layer.kind == TexturePaintLayerKind.Group)
                foreach (Node child in nodes) if (child.set == node.set && child.layer.parentId == layer.id)
                { dependencies.Add(child); if (!Visit(child)) valid = false; }
            if (!valid)
            {
                layer.linkError ??= "A source has a missing or circular dependency. Cached output retained.";
                node.state = 2; node.valid = false; return false;
            }
            layer.linkError = null;
            string signature = DependencySignature(layer, dependencies);
            if ((garment?.HasReferences == true || layer.links?.HasLinks == true || layer.layerMask?.effects?.HasReferences == true || layer.layerMask?.referenceOutputs.Count > 0) && signature != layer.linkSignature)
            {
                try
                {
                    // Validate both channel references before replacing either cached output.
                    foreach(var reference in new[]{layer.links?.content,layer.links?.mask})
                    {
                        if(reference?.IsSet!=true || reference.component==TexturePaintReferenceComponent.Mask)continue;
                        var source=Resolve(sets,node.set,reference,out _);
                        if(source.kind!=TexturePaintLayerKind.Group && !source.channels.ContainsKey(reference.channel))
                            throw new InvalidOperationException("The source does not contain the selected channel. Cached output retained.");
                    }
                    if (layer.links?.instance?.IsSet == true) ApplyInstance(node, Resolve(sets,node.set,layer.links.instance,out TextureSet sourceSet),sourceSet);
                    else if (layer.links?.content?.IsSet == true) ApplyReference(node,layer.links.content);
                    if (garment?.HasReferences == true && layer.links?.instance?.IsSet != true)
                    {
                        foreach (var reference in new[] { garment.foldInput, garment.protectionInput })
                        {
                            if(reference?.IsSet != true || reference.component == TexturePaintReferenceComponent.Mask) continue;
                            var source = Resolve(sets,node.set,reference,out _);
                            if(source.kind != TexturePaintLayerKind.Group && !source.channels.ContainsKey(reference.channel))
                                throw new InvalidOperationException("A garment input channel is missing. Cached output retained.");
                        }
                        using var renderer = new TexturePaintProjectionRenderer();
                        if(!renderer.Generate(sets,new[]{node.set},new[]{layer},layer.projectionSettings,out string error))
                            throw new InvalidOperationException(error);
                    }
                    ApplyMaskEffectReferences(node);
                    ApplyMask(node);
                    layer.linkRevision++;
                    // Signatures include the generated buffers, so capture the final state.
                    layer.linkSignature = DependencySignature(layer, dependencies);
                    node.set.BindPreviewTextures();
                }
                catch (Exception exception) { layer.linkError = exception.Message; }
            }
            else if (layer.links?.mask?.IsSet != true && layer.linkedMask != null)
            { UnityEngine.Object.DestroyImmediate(layer.linkedMask); layer.linkedMask = null; node.set.BindPreviewTextures(); }
            node.signature = DependencySignature(layer, dependencies);
            node.state = 2; node.valid = string.IsNullOrEmpty(layer.linkError); return node.valid;
        }
        private static string DependencySignature(TexturePaintLayer layer, List<Node> dependencies)
        {
            // Fixed-size keys prevent exponential growth when procedural layers depend on all layers below.
            var hash = new HashCode();
            hash.Add(ContentSignature(layer));
            foreach (Node dependency in dependencies) hash.Add(dependency.signature);
            return hash.ToHashCode().ToString();
        }
        public static string ContentSignature(TexturePaintLayer layer)
        {
            var hash = new HashCode();
            hash.Add(layer.id); hash.Add(layer.kind); hash.Add(RuntimeHelpers.GetHashCode(layer)); hash.Add(layer.linkRevision); hash.Add(layer.visible); hash.Add(layer.opacity);hash.Add(layer.blendMode);hash.Add(layer.parentId);
            hash.Add(JsonUtility.ToJson(layer.links)); hash.Add(JsonUtility.ToJson(layer.effects));
            hash.Add(JsonUtility.ToJson(layer.fillSettings)); hash.Add(JsonUtility.ToJson(layer.fillTileSources));
            hash.Add(JsonUtility.ToJson(layer.projectionSettings)); hash.Add(JsonUtility.ToJson(layer.splineSettings));
            hash.Add(layer.pluginParametersJson); hash.Add(JsonUtility.ToJson(layer.pluginParameters));
            foreach (var pair in layer.channels) { hash.Add(pair.Key); hash.Add(RuntimeHelpers.GetHashCode(pair.Value)); hash.Add(pair.Value.Revision); hash.Add(JsonUtility.ToJson(layer.GetChannelSettings(pair.Key))); }
            if (layer.layerMask != null) { hash.Add(RuntimeHelpers.GetHashCode(layer.layerMask)); hash.Add(layer.layerMask.target?.Revision ?? 0); hash.Add(JsonUtility.ToJson(layer.layerMask.effects)); }
            return hash.ToHashCode().ToString();
        }
        internal static Texture ReadSource(TextureSet set, TexturePaintLayer source, TexturePaintChannel channel, bool mask, List<RenderTexture> temporary)
        {
            TextureChannelTarget target = set.GetChannel(channel);
            int width = target?.Texture?.width ?? 1024, height = target?.Texture?.height ?? 1024;
            if (mask) return set.GetLayerMaskPreview(source) ?? Texture2D.whiteTexture;
            source.channels.TryGetValue(channel,out EditableTextureTarget pixels);
            if (pixels == null && source.kind != TexturePaintLayerKind.Group) throw new InvalidOperationException("The source does not contain the selected channel. Cached output retained.");
            if (set.compositor?.IsAvailable != true) return pixels != null ? pixels.Front : Texture2D.blackTexture;
            var result = RenderTexture.GetTemporary(new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGBHalf,0) { enableRandomWrite = true, sRGB = false });
            temporary.Add(result);
            var old = RenderTexture.active; RenderTexture.active=result; GL.Clear(false,true,Color.clear); RenderTexture.active=old;
            if (source.kind == TexturePaintLayerKind.Group)
            { set.compositor.ComposeIsolatedGroup(set,source,channel,result); set.compositor.UnassociateAlpha(result); return result; }
            TexturePaintLayerChannelSettings settings=source.GetChannelSettings(channel);
            set.compositor.CompositeLayerInto(result,set,source,pixels,source.GetChannelSettings(channel).channel,
                source.visible && settings.enabled ? source.opacity*settings.opacity : 0,TexturePaintBlendMode.Normal);
            set.compositor.UnassociateAlpha(result);
            return result;
        }
        private void Configure(TexturePaintLayerLinks links, TexturePaintLayerReference reference, bool mask, Texture baseMask, bool normal,
            TexturePaintChannel channel = TexturePaintChannel.Albedo)
        {
            material.SetInt("_Component",(int)reference.component); material.SetInt("_Invert",reference.invert?1:0);
            material.SetInt("_Mask",mask?1:0); material.SetInt("_Normal",normal?1:0);
            material.SetVector("_Tint",mask ? links.tint : TexturePaintChannelUtility.WorkingColor(channel,links.tint)); material.SetVector("_Transform",new Vector4(links.tiling.x,links.tiling.y,links.offset.x,links.offset.y));
            material.SetFloat("_Rotation",links.rotation*Mathf.Deg2Rad); material.SetTexture("_BaseMask",baseMask!=null?baseMask:Texture2D.whiteTexture);
        }
        private void ApplyReference(Node node, TexturePaintLayerReference reference)
        {
            TexturePaintLayer source=Resolve(sets,node.set,reference,out TextureSet owner);
            TextureChannelTarget target=node.set.GetChannel(node.layer.links.outputChannel);
            if(target?.Texture==null) throw new InvalidOperationException("The destination does not support the reference output channel.");
            var temporary=new List<RenderTexture>();
            EditableTextureTarget output=null;
            try
            {
                Texture image=ReadSource(owner,source,reference.channel,reference.component==TexturePaintReferenceComponent.Mask,temporary);
                output=new EditableTextureTarget(node.layer.name,target.Texture.width,target.Texture.height,target.format,null,Color.clear);
                Configure(node.layer.links,reference,false,null,target.channel==TexturePaintChannel.Normal,target.channel);
                Graphics.Blit(image,output.Front,material);output.CopyFrontToBack();
                foreach(var old in node.layer.channels.Values) old.Dispose();node.layer.channels.Clear();
                node.layer.channels[target.channel]=output;output=null;node.layer.GetChannelSettings(target.channel);
            }
            finally { output?.Dispose();foreach(var texture in temporary) RenderTexture.ReleaseTemporary(texture); }
        }
        private void ApplyMaskEffectReferences(Node node)
        {
            var mask = node.layer.layerMask;
            if (mask == null) return;
            var next = new Dictionary<string, RenderTexture>();
            var temporary = new List<RenderTexture>();
            var previousActive = RenderTexture.active;
            try
            {
                foreach (var effect in mask.effects.stack)
                {
                    if (!effect.enabled || effect.kind != TexturePaintMaskEffectKind.LayerReference || effect.reference?.IsSet != true) continue;
                    var source = Resolve(sets, node.set, effect.reference, out var owner);
                    Texture image = ReadSource(owner, source, effect.reference.channel,
                        effect.reference.component == TexturePaintReferenceComponent.Mask, temporary);
                    var output = EditableTextureTarget.Create("Mask reference " + effect.id, mask.target.Width, mask.target.Height, RenderTextureFormat.ARGBHalf);
                    next.Add(effect.id, output);
                    Configure(new TexturePaintLayerLinks(), effect.reference, true, Texture2D.whiteTexture, false);
                    Graphics.Blit(image, output, material);
                }
                mask.ClearReferenceOutputs();
                foreach (var pair in next) mask.referenceOutputs.Add(pair.Key, pair.Value);
                next.Clear();
            }
            finally
            {
                RenderTexture.active = previousActive != null ? previousActive : null;
                foreach (var output in next.Values) UnityEngine.Object.DestroyImmediate(output);
                foreach (var texture in temporary) RenderTexture.ReleaseTemporary(texture);
            }
        }
        private void ApplyMask(Node node)
        {
            TexturePaintLayerReference reference=node.layer.links?.mask;
            if(reference?.IsSet!=true)
            { if(node.layer.linkedMask!=null) UnityEngine.Object.DestroyImmediate(node.layer.linkedMask);node.layer.linkedMask=null;return; }
            TexturePaintLayer source=Resolve(sets,node.set,reference,out TextureSet owner);
            TextureChannelTarget target=null;foreach(var channel in node.set.channels.Values) {target=channel;break;}
            if(target?.Texture==null) return;
            var temporary=new List<RenderTexture>();
            try
            {
                Texture image=ReadSource(owner,source,reference.channel,reference.component==TexturePaintReferenceComponent.Mask,temporary);
                Texture painted=node.set.compositor?.GetPaintedLayerMask(node.layer,target.Texture.width,target.Texture.height,node.set) ?? node.layer.layerMask?.target?.Front;
                var output=EditableTextureTarget.Create("Linked layer mask",target.Texture.width,target.Texture.height,RenderTextureFormat.ARGB32);
                Configure(new TexturePaintLayerLinks(),reference,true,painted,false);Graphics.Blit(image,output,material);
                if(node.layer.linkedMask!=null) UnityEngine.Object.DestroyImmediate(node.layer.linkedMask);node.layer.linkedMask=output;
            }
            finally {foreach(var texture in temporary) RenderTexture.ReleaseTemporary(texture);}
        }
        private void ApplyInstance(Node node, TexturePaintLayer source, TextureSet sourceSet)
        {
            TexturePaintLayer layer=node.layer;
            if ((layer.kind==TexturePaintLayerKind.Fill || layer.kind==TexturePaintLayerKind.Projection) && layer.kind!=source.kind)
                throw new InvalidOperationException("The instance source has a different layer type. Choose a matching source or make independent.");
            if(layer.kind==TexturePaintLayerKind.Projection && source.kind==TexturePaintLayerKind.Projection)
            {
                var settings=source.projectionSettings.Clone();var local=layer.projectionSettings;
                settings.placed=local.placed;settings.position=local.position;settings.rotation=local.rotation;
                settings.flipX=local.flipX;settings.flipY=local.flipY;settings.lockAspect=local.lockAspect;
                settings.width=local.width;settings.height=local.height;settings.depth=local.depth;settings.mode=local.mode;settings.depthFromSurface=local.depthFromSurface;
                settings.gridSize=local.gridSize; settings.pinned=(bool[])local.pinned.Clone(); settings.cylinderAngle=local.cylinderAngle;
                settings.points=(Vector3[])local.points.Clone();settings.regionSurfaceId=local.regionSurfaceId;settings.regionTriangle=local.regionTriangle;
                var copy=node.set.CloneLayer(layer,layer.name,true,false);
                using var renderer=new TexturePaintProjectionRenderer();
                if(!renderer.Generate(sets,new[]{node.set},new[]{copy},settings,out string error)) {copy.Dispose();throw new InvalidOperationException(error);}
                ReplaceChannels(layer,copy);layer.projectionSettings=settings;copy.Dispose();
            }
            else if(layer.kind==TexturePaintLayerKind.Fill && source.kind==TexturePaintLayerKind.Fill)
            {
                var local=layer.fillSettings;var copy=node.set.CloneLayer(source,layer.name,true,true);
                copy.layerMask?.Dispose();copy.layerMask=null;
                foreach(var channel in new List<TexturePaintChannel>(copy.channels.Keys))
                {
                    var target=node.set.GetChannel(channel);var pixels=copy.channels[channel];
                    if(target?.Texture==null){pixels.Dispose();copy.channels.Remove(channel);continue;}
                    if(pixels.Width!=target.Texture.width || pixels.Height!=target.Texture.height || pixels.Front.format!=target.format)
                    {
                        pixels.Dispose();copy.channels[channel]=new EditableTextureTarget(layer.name,target.Texture.width,target.Texture.height,target.format,null,Color.clear);
                    }
                }
                copy.fillSettings=source.fillSettings.Clone();copy.fillChannel=source.fillChannel;copy.fillTileSources=source.fillTileSources?.Clone();
                if(local!=null) {copy.fillSettings.tiling=local.tiling;copy.fillSettings.offset=local.offset;copy.fillSettings.rotation=local.rotation;}
                foreach(var settings in copy.channelSettings.Values)
                { if(settings.sourceSettings!=null && local!=null){settings.sourceSettings.tiling=local.tiling;settings.sourceSettings.offset=local.offset;settings.sourceSettings.rotation=local.rotation;} }
                if(!node.set.RegenerateFillLayer(copy)) {copy.Dispose();throw new InvalidOperationException("Instance could not regenerate its Fill source.");}
                layer.channelSettings.Clear();foreach(var pair in copy.channelSettings)layer.channelSettings[pair.Key]=pair.Value.Clone();
                ReplaceChannels(layer,copy);layer.fillSettings=copy.fillSettings;layer.fillTileSources=copy.fillTileSources;layer.fillChannel=copy.fillChannel;copy.Dispose();
            }
            else
            {
                var outputs=new Dictionary<TexturePaintChannel,EditableTextureTarget>();var temporary=new List<RenderTexture>();
                try
                {
                    foreach(var channel in node.set.channels.Values)
                    {
                        if(source.kind!=TexturePaintLayerKind.Group && !source.channels.ContainsKey(channel.channel)) continue;
                        var output=new EditableTextureTarget(layer.name,channel.Texture.width,channel.Texture.height,channel.format,null,Color.clear);outputs[channel.channel]=output;
                        var reference=layer.links.instance.Clone();reference.component=TexturePaintReferenceComponent.Color;
                        Configure(layer.links,reference,false,null,channel.channel==TexturePaintChannel.Normal,channel.channel);
                        Graphics.Blit(ReadSource(sourceSet,source,channel.channel,false,temporary),output.Front,material);output.CopyFrontToBack();
                    }
                    foreach(var old in layer.channels.Values) old.Dispose();layer.channels.Clear();foreach(var pair in outputs)layer.channels[pair.Key]=pair.Value;outputs.Clear();
                }
                finally {foreach(var output in outputs.Values)output.Dispose();foreach(var texture in temporary)RenderTexture.ReleaseTemporary(texture);}
            }
            if(layer.kind==TexturePaintLayerKind.Fill || layer.kind==TexturePaintLayerKind.Projection)
            {
                layer.effects=source.effects?.Clone();layer.blendMode=source.blendMode;
                if(layer.kind==TexturePaintLayerKind.Projection) foreach(var pair in source.channelSettings) if(layer.channels.ContainsKey(pair.Key))layer.channelSettings[pair.Key]=pair.Value.Clone();
            }
        }
        private static void ReplaceChannels(TexturePaintLayer layer,TexturePaintLayer copy)
        { foreach(var old in layer.channels.Values)old.Dispose();layer.channels.Clear();foreach(var pair in copy.channels)layer.channels[pair.Key]=pair.Value;copy.channels.Clear(); }
        public void Dispose() { if(material!=null)UnityEngine.Object.DestroyImmediate(material);nodes.Clear();byLayer.Clear(); }
    }
}

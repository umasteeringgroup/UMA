using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAResourceUsageMonitor))]
    public sealed class UMAResourceUsageMonitorEditor : Editor
    {
        private const string LayoutPath = "Assets/UMA/Core/Editor/Scripts/UMAResourceUsageMonitorEditor.uxml";

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
            if (layout == null)
            {
                root.Add(new HelpBox("Resource monitor layout could not be loaded. Reimport its UXML file.", HelpBoxMessageType.Error));
                InspectorElement.FillDefaultInspector(root, serializedObject, this);
                return root;
            }
            layout.CloneTree(root);
            var settings = root.Q<Foldout>("settings");
            InspectorElement.FillDefaultInspector(settings, serializedObject, this);
            var monitor = (UMAResourceUsageMonitor)target;
            var values = new Dictionary<string, Label>();
            var comparisons = new Dictionary<string, Label>();
            foreach (string label in new[] { "Reuse mode", "Avatars completed / spawned", "Queue / texture work", "Time to ready", "Generator work", "Mesh stage + preprocess", "Texture stage", "Atlas preparation", "Atlas lookup + binding", "Atlas generation", "Early atlas hits", "Mesh / texture update calls", "Mesh hits / pending joins", "Atlas hits / pending joins", "Cache builds: mesh / atlas", "Unique meshes / avatar uses", "Unique atlas textures / avatar uses", "Live reuse: meshes / textures", "Mesh memory", "Texture memory", "Total output memory", "Memory avoided by live sharing", "Memory sampling cost" })
                values.Add(label, AddMetric(root.Q("currentMetrics"), label));
            foreach (string label in new[] { "Time to ready", "Generator work", "Mesh time", "Texture time", "Atlas preparation", "Atlas lookup + binding", "Atlas generation", "Early atlas hits", "Output memory", "Mesh hits", "Atlas hits" })
                comparisons.Add(label, AddMetric(root.Q("comparison"), label));

            root.Q<Button>("restartOff").clicked += () => monitor.RestartCrowd(false);
            root.Q<Button>("restartOn").clicked += () => monitor.RestartCrowd(true);
            root.Q<Button>("refreshMemory").clicked += monitor.RefreshMemory;
            root.Q<Button>("reset").clicked += monitor.ResetCapture;
            root.Q<Button>("log").clicked += monitor.LogReport;
            root.Q<Button>("copy").clicked += () => EditorGUIUtility.systemCopyBuffer = monitor.GetReport();
            root.Q<Button>("save").clicked += () =>
            {
                try { monitor.SaveReport(); }
                catch (Exception exception) { Debug.LogException(exception, monitor); }
            };

            void Refresh()
            {
                if (monitor == null) return;
                bool playing = Application.isPlaying && monitor.isActiveAndEnabled;
                root.Q("restartButtons").SetEnabled(playing && monitor.HasCrowd && !monitor.IsRestarting);
                root.Q("reportButtons").SetEnabled(playing && !monitor.IsRestarting);
                settings.SetEnabled(!Application.isPlaying);
                root.Q<Label>("status").text = playing ? monitor.Status : "Enter Play mode. Select this component to compare the generated crowd.";
                var s = monitor.Current;
                var m = s.Memory;
                values["Reuse mode"].text = s.Mode ?? monitor.InitialReuse.ToString();
                values["Avatars completed / spawned"].text = $"{s.CompletedAvatars:N0} / {s.SpawnedAvatars:N0}";
                values["Queue / texture work"].text = $"{s.QueueSize:N0} / {s.PendingTextureWork:N0}";
                values["Time to ready"].text = Ms(s.WallMilliseconds);
                values["Generator work"].text = Ms(s.GeneratorMilliseconds);
                values["Mesh stage + preprocess"].text = Ms(s.MeshMilliseconds);
                values["Texture stage"].text = Ms(s.TextureMilliseconds);
                values["Atlas preparation"].text = Ms(s.AtlasPreparationMilliseconds);
                values["Atlas lookup + binding"].text = Ms(s.AtlasLookupMilliseconds);
                values["Atlas generation"].text = Ms(s.AtlasGenerationMilliseconds);
                values["Early atlas hits"].text = s.AtlasEarlyHits.ToString("N0");
                values["Mesh / texture update calls"].text = $"{s.MeshUpdates:N0} / {s.TextureUpdates:N0}";
                values["Mesh hits / pending joins"].text = $"{s.MeshCache.Hits:N0} / {s.MeshCache.PendingJoins:N0}";
                values["Atlas hits / pending joins"].text = $"{s.TextureCache.Hits:N0} / {s.TextureCache.PendingJoins:N0}";
                values["Cache builds: mesh / atlas"].text = $"{s.MeshCache.Publications:N0} / {s.TextureCache.Publications:N0}";
                values["Unique meshes / avatar uses"].text = $"{m.UniqueMeshes:N0} / {m.MeshUses:N0}";
                values["Unique atlas textures / avatar uses"].text = $"{m.UniqueTextures:N0} / {m.TextureUses:N0}";
                values["Live reuse: meshes / textures"].text = $"{m.SharedMeshUses:N0} / {m.SharedTextureUses:N0}";
                values["Mesh memory"].text = MiB(m.MeshBytes);
                values["Texture memory"].text = MiB(m.TextureBytes);
                values["Total output memory"].text = MiB(m.TotalBytes);
                values["Memory avoided by live sharing"].text = MiB(m.AvoidedBytes);
                values["Memory sampling cost"].text = Ms(s.MemorySampleMilliseconds);
                root.Q<Label>("reuseNotes").text = (s.ReuseNotes == null ? string.Empty : string.Join("\n", s.ReuseNotes)) +
                    (s.MeshCache.Bypasses > 0 ? $"\nMesh cache bypasses: {s.MeshCache.Bypasses}. {s.MeshCache.LastBypassReason}" : "") +
                    (s.TextureCache.Bypasses > 0 ? $"\nAtlas cache bypasses: {s.TextureCache.Bypasses}. {s.TextureCache.LastBypassReason}" : "");
                var off = monitor.LastOff;
                var on = monitor.LastOn;
                comparisons["Time to ready"].text = Pair(off, on, x => Ms(x.WallMilliseconds)) + Change(off, on, x => x.WallMilliseconds);
                comparisons["Generator work"].text = Pair(off, on, x => Ms(x.GeneratorMilliseconds)) + Change(off, on, x => x.GeneratorMilliseconds);
                comparisons["Mesh time"].text = Pair(off, on, x => Ms(x.MeshMilliseconds)) + Change(off, on, x => x.MeshMilliseconds);
                comparisons["Texture time"].text = Pair(off, on, x => Ms(x.TextureMilliseconds)) + Change(off, on, x => x.TextureMilliseconds);
                comparisons["Output memory"].text = Pair(off, on, x => MiB(x.Memory.TotalBytes)) + Change(off, on, x => x.Memory.TotalBytes);
                comparisons["Mesh hits"].text = Pair(off, on, x => x.MeshCache.Hits.ToString("N0"));
                comparisons["Atlas hits"].text = Pair(off, on, x => x.TextureCache.Hits.ToString("N0"));
                comparisons["Atlas preparation"].text = Pair(off, on, x => Ms(x.AtlasPreparationMilliseconds));
                comparisons["Atlas lookup + binding"].text = Pair(off, on, x => Ms(x.AtlasLookupMilliseconds));
                comparisons["Atlas generation"].text = Pair(off, on, x => Ms(x.AtlasGenerationMilliseconds));
                comparisons["Early atlas hits"].text = Pair(off, on, x => x.AtlasEarlyHits.ToString("N0"));
                root.Q<Label>("notes").text =
                    "Time = measured main-thread stage work, including cache lookup/binding; not GPU time or total worker CPU. Time to ready also includes frame scheduling and texture readbacks. " +
                    "Atlas preparation = layout, UV updates and drawing setup; lookup = signature checks and cache-hit binding; generation = allocation, drawing, postprocessing and conversion submission on misses. These are substeps, not extra time to add to Texture stage; other bookkeeping and DNA pre-apply remain in the stage total. Early hits skip drawing setup. " +
                    "Timings cover the assigned generator; cache counters cover all avatars in this process since capture began. Hits are successful cache lookups, not retained leases; pending joins may be cancelled. Cache builds count published shared outputs only (zero with reuse OFF).\n\n" +
                    "Memory = Unity-reported native object memory of current generated mesh/atlas references, deduplicated across the crowd. Not total application RAM/VRAM, source input textures, or temporary build buffers. Same texture on two passes counts once per avatar. Disabled/pooled avatars still count. " +
                    "Memory is sampled after building by default. Fully random appearances may have few exact matches.\n" +
                    $"Cache entries already present at capture start: {s.CacheEntriesAtStart}." +
                    (m.UnavailableMemorySizes > 0 ? $" Memory size unavailable for {m.UnavailableMemorySizes} objects; totals are incomplete. Use the Editor or a Development Build." : "") +
                    (s.CountersReset ? " Counters were reset externally: restart this capture." : "");
            }
            Refresh();
            root.schedule.Execute(Refresh).Every(500);
            return root;
        }

        private static Label AddMetric(VisualElement parent, string title)
        {
            var row = new VisualElement();
            row.AddToClassList("metric");
            var name = new Label(title);
            name.AddToClassList("metric-name");
            row.Add(name);
            var value = new Label("—");
            value.AddToClassList("metric-value");
            row.Add(value);
            parent.Add(row);
            return value;
        }

        private static string Pair(UMAResourceUsageMonitor.RunSnapshot off, UMAResourceUsageMonitor.RunSnapshot on,
            Func<UMAResourceUsageMonitor.RunSnapshot, string> format) =>
            $"OFF {(off == null ? "—" : format(off))}  |  ON {(on == null ? "—" : format(on))}";
        private static string Ms(double value) => $"{value:N2} ms";
        private static string MiB(long value) => $"{value / (1024d * 1024d):N2} MiB";
        private static string Change(UMAResourceUsageMonitor.RunSnapshot off, UMAResourceUsageMonitor.RunSnapshot on,
            Func<UMAResourceUsageMonitor.RunSnapshot, double> value)
        {
            if (off == null || on == null || value(off) <= 0) return string.Empty;
            if (off.CountersReset || on.CountersReset || off.SpawnedAvatars != on.SpawnedAvatars ||
                off.CompletedAvatars != off.SpawnedAvatars || on.CompletedAvatars != on.SpawnedAvatars)
                return "\nIncomplete or different-sized runs";
            double percent = (value(on) / value(off) - 1) * 100;
            return $"\nON change: {percent:+0.0;-0.0;0.0}%";
        }
    }
}

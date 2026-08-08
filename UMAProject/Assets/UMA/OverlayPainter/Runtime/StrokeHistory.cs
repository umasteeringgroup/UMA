using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UMA.TexturePaint
{
    /// <summary>
    /// Format-preserving sparse tile history kept entirely on the GPU. Closing, undoing, and
    /// redoing a stroke never performs a CPU readback, compression pass, or Unity object Undo.
    /// </summary>
    public sealed class StrokeHistory : IDisposable
    {
        private sealed class Capture
        {
            public RenderTexture texture;
            public RectInt rect;
            public GraphicsFormat format;
        }

        private sealed class Entry
        {
            public EditableTextureTarget target;
            public RectInt rect;
            public Capture before;
            public Capture after;
            public string name;
        }

        private sealed class Group
        {
            public string key;
            public readonly List<Entry> entries = new List<Entry>();
            public readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            public long EstimatedBytes;
        }

        private readonly List<Group> undo = new List<Group>();
        private readonly List<Group> redo = new List<Group>();
        private Group pending;
        private long estimatedBytes;
        private long commitVersion;

        public int Capacity { get; set; } = 32;
        public int TileSize { get; set; } = 128;
        public long MemoryBudgetBytes { get; set; } = 256L * 1024L * 1024L;
        public long EstimatedMemoryBytes => estimatedBytes + (pending?.EstimatedBytes ?? 0L);
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public long CommitVersion => commitVersion;
        public int UndoTileCount { get { int count = 0; for (int i = 0; i < undo.Count; i++) count += undo[i].entries.Count; return count; } }

        public void Begin(string name, EditableTextureTarget target, RectInt rect)
        {
            BeginGroup();
            Include(name, target, rect);
        }

        public void BeginGroup(string key = null)
        {
            CancelPending();
            pending = new Group { key = key };
        }

        public void Include(string name, EditableTextureTarget target, RectInt rect)
        {
            if (target == null) return;
            rect = Clamp(rect, target.Width, target.Height);
            if (rect.width <= 0 || rect.height <= 0) return;
            if (pending == null) pending = new Group();
            int tileSize = Mathf.Clamp(TileSize, 32, 512);
            int minTileX = rect.xMin / tileSize, maxTileX = (rect.xMax - 1) / tileSize;
            int minTileY = rect.yMin / tileSize, maxTileY = (rect.yMax - 1) / tileSize;
            for (int tileY = minTileY; tileY <= maxTileY; tileY++)
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                string key = target.Front.GetInstanceID() + ":" + tileX + ":" + tileY;
                if (!pending.keys.Add(key)) continue;
                RectInt tileRect = Clamp(new RectInt(tileX * tileSize, tileY * tileSize, tileSize, tileSize), target.Width, target.Height);
                Capture before = BeginCapture(target.Front, tileRect);
                pending.entries.Add(new Entry { name = name, target = target, rect = tileRect, before = before });
                pending.EstimatedBytes += EstimateRawBytes(before);
            }
        }

        public void Commit()
        {
            if (pending == null) return;
            if (pending.entries.Count == 0) { pending = null; return; }
            for (int i = 0; i < pending.entries.Count; i++)
                pending.entries[i].after = BeginCapture(pending.entries[i].target.Front, pending.entries[i].rect);
            pending.EstimatedBytes *= 2L;
            undo.Add(pending);
            estimatedBytes += pending.EstimatedBytes;
            pending = null;
            ClearGroups(redo);
            PruneToBudget();
            commitVersion++;
        }

        public void CancelPending()
        {
            ReleaseGroup(pending);
            pending = null;
        }

        /// <summary>
        /// Restores the active group's original tiles without discarding them. This supports a
        /// one-time in-stroke rerasterization while retaining the same lightweight undo entry.
        /// </summary>
        public bool RestorePendingBefore()
        {
            if (pending == null || pending.entries.Count == 0) return false;
            for (int i = 0; i < pending.entries.Count; i++)
            {
                Entry entry = pending.entries[i];
                Restore(entry.target, entry.rect, entry.before);
            }
            return true;
        }

        public void Clear()
        {
            ClearGroups(undo);
            ClearGroups(redo);
            CancelPending();
            estimatedBytes = 0L;
        }

        public void ClearRedo() => ClearGroups(redo);

        public bool Undo() => Apply(undo, redo, false);
        public bool Redo() => Apply(redo, undo, true);

        /// <summary>
        /// Restores and removes the newest group when it belongs to a procedural operation that is
        /// about to be regenerated. Unlike Undo, this deliberately creates no redo entry.
        /// </summary>
        public bool RevertLatest(string key)
        {
            if (string.IsNullOrEmpty(key) || undo.Count == 0) return false;
            Group group = undo[undo.Count - 1];
            if (!string.Equals(group.key, key, StringComparison.Ordinal)) return false;
            undo.RemoveAt(undo.Count - 1);
            for (int i = 0; i < group.entries.Count; i++)
            {
                Entry entry = group.entries[i];
                Restore(entry.target, entry.rect, entry.before);
            }
            estimatedBytes -= group.EstimatedBytes;
            if (estimatedBytes < 0L) estimatedBytes = 0L;
            ReleaseGroup(group);
            return true;
        }

        private bool Apply(List<Group> source, List<Group> destination, bool useAfter)
        {
            if (source.Count == 0) return false;
            Group group = source[source.Count - 1];
            source.RemoveAt(source.Count - 1);
            for (int i = 0; i < group.entries.Count; i++)
            {
                Entry entry = group.entries[i];
                Restore(entry.target, entry.rect, useAfter ? entry.after : entry.before);
            }
            destination.Add(group);
            return true;
        }

        private static Capture BeginCapture(RenderTexture source, RectInt rect)
        {
            if (source == null || rect.width <= 0 || rect.height <= 0) return null;
            RenderTextureDescriptor descriptor = source.descriptor;
            descriptor.width = rect.width;
            descriptor.height = rect.height;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.enableRandomWrite = false;
            RenderTexture texture = new RenderTexture(descriptor)
            {
                name = "Texture Paint History Tile",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            texture.Create();
            Graphics.CopyTexture(source, 0, 0, rect.x, rect.y, rect.width, rect.height,
                texture, 0, 0, 0, 0);
            return new Capture { texture = texture, rect = rect, format = source.graphicsFormat };
        }

        private static void Restore(EditableTextureTarget target, RectInt rect, Capture capture)
        {
            if (target == null || capture?.texture == null) return;
            Graphics.CopyTexture(capture.texture, 0, 0, 0, 0, rect.width, rect.height,
                target.Front, 0, 0, rect.x, rect.y);
            Graphics.CopyTexture(capture.texture, 0, 0, 0, 0, rect.width, rect.height,
                target.Back, 0, 0, rect.x, rect.y);
        }

        private void PruneToBudget()
        {
            int capacity = Mathf.Max(1, Capacity);
            long budget = Math.Max(1024L * 1024L, MemoryBudgetBytes);
            while (undo.Count > capacity || (estimatedBytes > budget && undo.Count > 1))
            {
                estimatedBytes -= undo[0].EstimatedBytes;
                ReleaseGroup(undo[0]);
                undo.RemoveAt(0);
            }
        }

        private void ClearGroups(List<Group> groups)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                estimatedBytes -= groups[i].EstimatedBytes;
                ReleaseGroup(groups[i]);
            }
            groups.Clear();
            if (estimatedBytes < 0L) estimatedBytes = 0L;
        }

        private static long EstimateRawBytes(Capture capture)
        {
            uint blockSize = GraphicsFormatUtility.GetBlockSize(capture.format);
            uint blockWidth = GraphicsFormatUtility.GetBlockWidth(capture.format);
            uint blockHeight = GraphicsFormatUtility.GetBlockHeight(capture.format);
            long blocksX = (capture.rect.width + blockWidth - 1L) / blockWidth;
            long blocksY = (capture.rect.height + blockHeight - 1L) / blockHeight;
            return blocksX * blocksY * blockSize;
        }

        private static void ReleaseGroup(Group group)
        {
            if (group == null) return;
            for (int i = 0; i < group.entries.Count; i++)
            {
                ReleaseCapture(group.entries[i].before);
                ReleaseCapture(group.entries[i].after);
            }
            group.entries.Clear();
            group.keys.Clear();
        }

        private static void ReleaseCapture(Capture capture)
        {
            if (capture?.texture == null) return;
            capture.texture.Release();
            Destroy(capture.texture);
            capture.texture = null;
        }

        private static RectInt Clamp(RectInt rect, int width, int height)
        {
            int xMin = Mathf.Clamp(rect.xMin, 0, width), yMin = Mathf.Clamp(rect.yMin, 0, height);
            int xMax = Mathf.Clamp(rect.xMax, xMin, width), yMax = Mathf.Clamp(rect.yMax, yMin, height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        public void Dispose() => Clear();
        private static void Destroy(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    public partial class DynamicCharacterAvatarEditor
    {
        private readonly AvatarInspectorRefreshGate _dnaMetadataRefresh = new AvatarInspectorRefreshGate(0.5);
        private readonly List<DNA> _dnaAssetsSnapshot = new List<DNA>();
        private readonly List<string> _dnaAssetNames = new List<string>();
        private readonly List<string> _groupTitles = new List<string>();
        private bool _dnaDictionaryNeedsRefresh;
        private bool _dnaViewDirty = true;
        private bool _dnaViewInitialized;
        private int _dnaViewRebuildCount;
        private readonly List<DNAInstance> _dnaInstanceSnapshot = new List<DNAInstance>();
        private readonly List<string> _dnaInstanceNames = new List<string>();
        private readonly Dictionary<DNAGroup, List<(int index, DNAInstance inst)>> _assignedDnaGroups =
            new Dictionary<DNAGroup, List<(int, DNAInstance)>>();
        private readonly List<KeyValuePair<DNAGroup, List<(int index, DNAInstance inst)>>> _sortedDnaGroups =
            new List<KeyValuePair<DNAGroup, List<(int, DNAInstance)>>>();
        private readonly List<(int index, DNAInstance inst)> _unknownDna = new List<(int, DNAInstance)>();
        private readonly HashSet<string> _assignedDnaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DNAGroup, string[]> _availableDnaNames = new Dictionary<DNAGroup, string[]>();

        private bool DNAMetadataMatches(DNACollection collection)
        {
            if (_cachedDNACollectionRef != collection) return false;
            var groups = collection?.DNAGroups;
            if (_groupsSnapshot.Count != (groups?.Count ?? 0)) return false;
            int assetIndex = 0;
            for (int i = 0; i < _groupsSnapshot.Count; i++)
            {
                var group = groups[i];
                if (group != _groupsSnapshot[i] || group?.DNAArea != _groupTitles[i] ||
                    (group?.dnaList?.Count ?? 0) != _groupDnaCounts[i]) return false;
                for (int j = 0; j < _groupDnaCounts[i]; j++, assetIndex++)
                {
                    var dna = group.dnaList[j];
                    if (assetIndex >= _dnaAssetsSnapshot.Count || dna != _dnaAssetsSnapshot[assetIndex] ||
                        (dna != null ? dna.name : null) != _dnaAssetNames[assetIndex]) return false;
                }
            }
            return assetIndex == _dnaAssetsSnapshot.Count;
        }

        private void CaptureDNAMetadata(List<DNAGroup> groups)
        {
            _groupTitles.Clear();
            _dnaAssetsSnapshot.Clear();
            _dnaAssetNames.Clear();
            if (groups != null)
                foreach (var group in groups)
                {
                    _groupTitles.Add(group?.DNAArea);
                    if (group?.dnaList == null) continue;
                    foreach (var dna in group.dnaList)
                    {
                        _dnaAssetsSnapshot.Add(dna);
                        _dnaAssetNames.Add(dna != null ? dna.name : null);
                    }
                }
            _dnaDictionaryNeedsRefresh = true;
            _dnaViewDirty = true;
        }

        private void EnsureAssignedDNACache(List<DNAInstance> instances)
        {
            // A GUI event pair must use the same groups and row ordering.
            if (_dnaViewInitialized && Event.current != null && Event.current.type != EventType.Layout) return;
            int count = instances?.Count ?? 0;
            bool changed = _dnaViewDirty || !_dnaViewInitialized || count != _dnaInstanceSnapshot.Count;
            for (int i = 0; !changed && i < count; i++)
                changed = !ReferenceEquals(instances[i], _dnaInstanceSnapshot[i]) || instances[i]?.Name != _dnaInstanceNames[i];
            if (!changed) return; // Values/enabled flags are read live through the cached references.

            _dnaViewDirty = false;
            _dnaViewInitialized = true;
            _dnaViewRebuildCount++;
            _dnaInstanceSnapshot.Clear();
            _dnaInstanceNames.Clear();
            _assignedDnaGroups.Clear();
            _unknownDna.Clear();
            _assignedDnaNames.Clear();
            _availableDnaNames.Clear();
            for (int i = 0; i < count; i++)
            {
                var instance = instances[i];
                _dnaInstanceSnapshot.Add(instance);
                _dnaInstanceNames.Add(instance?.Name);
                if (instance == null) continue;
                if (!string.IsNullOrEmpty(instance.Name)) _assignedDnaNames.Add(instance.Name);
                if (!string.IsNullOrEmpty(instance.Name) && _nameToGroupCache.TryGetValue(instance.Name, out var group))
                {
                    if (!_assignedDnaGroups.TryGetValue(group, out var entries))
                        _assignedDnaGroups.Add(group, entries = new List<(int, DNAInstance)>());
                    entries.Add((i, instance));
                }
                else _unknownDna.Add((i, instance));
            }
            _sortedDnaGroups.Clear();
            foreach (var pair in _assignedDnaGroups)
            {
                pair.Value.Sort(CompareDnaEntries);
                _sortedDnaGroups.Add(pair);
            }
            _sortedDnaGroups.Sort((a, b) => string.Compare(a.Key.DNAArea, b.Key.DNAArea, StringComparison.OrdinalIgnoreCase));
            _unknownDna.Sort(CompareDnaEntries);
        }

        private static int CompareDnaEntries((int index, DNAInstance inst) a, (int index, DNAInstance inst) b) =>
            string.Compare(a.inst?.Name, b.inst?.Name, StringComparison.OrdinalIgnoreCase);

        private string[] GetAvailableDNANames(DNAGroup group)
        {
            if (group == null) return Array.Empty<string>();
            if (_availableDnaNames.TryGetValue(group, out var names)) return names;
            var list = new List<string>();
            if (group.dnaList != null)
                foreach (var dna in group.dnaList)
                    if (dna != null && !_assignedDnaNames.Contains(dna.name)) list.Add(dna.name);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            names = list.ToArray();
            _availableDnaNames.Add(group, names);
            return names;
        }
    }
}

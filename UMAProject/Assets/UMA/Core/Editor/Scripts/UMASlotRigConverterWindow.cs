using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Editor-side helpers around <see cref="UMASlotRigConverter"/>: convert a slot and save the
    /// result as a new SlotDataAsset. The source asset is never modified.
    /// </summary>
    public static class UMASlotRigConverterEditorUtility
    {
        public const string DefaultSuffix = "_Converted";

        /// <summary>
        /// Converts <paramref name="slot"/> for <paramref name="targetRace"/> and saves it as a new
        /// asset. Returns the new asset. Call <see cref="AssetDatabase.SaveAssets"/> afterwards.
        /// </summary>
        /// <param name="outputFolder">Project-relative folder; null saves next to the source asset.</param>
        /// <param name="addToIndex">Also register the new slot in the UMA asset index.</param>
        public static SlotDataAsset ConvertAndSave(SlotDataAsset slot, RaceData sourceRace, RaceData targetRace,
            UMASlotRigConversionOptions options, string nameSuffix, string outputFolder, bool addToIndex,
            out UMASlotRigConversionReport report)
        {
            UMARigRestPose sourceRig = UMARigRestPose.FromRace(sourceRace);
            UMARigRestPose targetRig = UMARigRestPose.FromRace(targetRace);
            return ConvertAndSave(slot, sourceRig, targetRig, options, nameSuffix, outputFolder, addToIndex, out report);
        }

        /// <param name="replaceExisting">
        /// When an earlier conversion of this slot already sits at the output path, write into that
        /// asset (keeping its GUID, so recipes that use it keep working) instead of creating a
        /// second asset with the same slot name.
        /// </param>
        public static SlotDataAsset ConvertAndSave(SlotDataAsset slot, UMARigRestPose sourceRig, UMARigRestPose targetRig,
            UMASlotRigConversionOptions options, string nameSuffix, string outputFolder, bool addToIndex,
            out UMASlotRigConversionReport report, bool replaceExisting = true)
        {
            return ConvertAndSaveCore(slot, sourceRig, targetRig, options, nameSuffix, outputFolder, addToIndex, replaceExisting,
                saveIndex: true, out report, out _);
        }

        /// <summary>
        /// True when <paramref name="slot"/> is already bound in <paramref name="targetRig"/>'s mesh
        /// space, so converting it from another rig would damage it.
        /// </summary>
        public static bool IsAlreadyOnRig(SlotDataAsset slot, UMARigRestPose targetRig, UMASlotRigConversionOptions options = null)
        {
            float tolerance = (options ?? UMASlotRigConversionOptions.Default).BindPoseTolerance;
            return slot != null && UMASlotRigConverter.IsBoundToRigMeshSpace(slot.meshData, targetRig, tolerance, out _);
        }

        /// <summary>
        /// Shared implementation. <paramref name="saveIndex"/> false leaves saving the UMA index to the
        /// caller, so a batch saves it once. <paramref name="replaced"/> reports whether an earlier
        /// conversion was overwritten.
        /// </summary>
        internal static SlotDataAsset ConvertAndSaveCore(SlotDataAsset slot, UMARigRestPose sourceRig, UMARigRestPose targetRig,
            UMASlotRigConversionOptions options, string nameSuffix, string outputFolder, bool addToIndex, bool replaceExisting,
            bool saveIndex, out UMASlotRigConversionReport report, out bool replaced)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }
            options = options ?? UMASlotRigConversionOptions.Default;
            if (UMASlotRigConverter.IsBoundToRigMeshSpace(slot.meshData, targetRig, options.BindPoseTolerance, out float nativeDifference))
            {
                throw new InvalidOperationException(
                    $"'{slot.slotName}' is already bound to the target rig (bind poses within {nativeDifference:0.#######} of its mesh space); converting it would damage it.");
            }

            report = new UMASlotRigConversionReport();
            // A profile's entries for single slots are looked up by slot name. The caller's options
            // are shared across a batch, so the name is set for this call only.
            string callerSlotName = options.ProfileSlotName;
            if (string.IsNullOrEmpty(callerSlotName))
            {
                options.ProfileSlotName = slot.slotName;
            }
            UMAMeshData converted;
            try
            {
                converted = UMASlotRigConverter.Convert(slot.meshData, sourceRig, targetRig, options, report);
            }
            finally
            {
                options.ProfileSlotName = callerSlotName;
            }

            string suffix = string.IsNullOrEmpty(nameSuffix) ? DefaultSuffix : nameSuffix;
            string sourcePath = AssetDatabase.GetAssetPath(slot);
            string folder = !string.IsNullOrEmpty(outputFolder)
                ? outputFolder.Replace('\\', '/').TrimEnd('/')
                : (string.IsNullOrEmpty(sourcePath) ? "Assets" : Path.GetDirectoryName(sourcePath).Replace('\\', '/'));
            EnsureFolder(folder);

            string fileName = string.IsNullOrEmpty(sourcePath) ? slot.name : Path.GetFileNameWithoutExtension(sourcePath);
            string basePath = folder + "/" + fileName + suffix + ".asset";
            string logicalName = slot.slotName + suffix;

            // An earlier conversion of the same slot: same path, same slot name. Anything else at that
            // path is left alone and the new asset gets a unique file name.
            SlotDataAsset existing = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(basePath);
            bool sameSlot = existing != null && existing != slot && string.Equals(existing.slotName, logicalName, StringComparison.Ordinal);
            replaced = replaceExisting && sameSlot;
            string assetPath = replaced ? basePath : AssetDatabase.GenerateUniqueAssetPath(basePath);
            if (sameSlot && !replaced)
            {
                report.Warnings.Add($"'{basePath}' already holds a slot named '{logicalName}'; this conversion was saved as a second asset with the same slot name.");
            }

            SlotDataAsset copy = UnityEngine.Object.Instantiate(slot);
            copy.meshData = converted;
            copy.isLegacySlot = false;
            copy.PrepareForAssetPath(assetPath, logicalName);
            converted.SlotName = logicalName;
            report.SlotName = logicalName;

            SlotDataAsset result;
            if (replaced)
            {
                // Same outcome as a fresh conversion, written into the existing asset. The old mesh
                // data may hold native bone weights; release them before it is dropped.
                UMAMeshData previous = existing.meshData;
                if (previous != null && previous.LoadedBoneweights)
                {
                    previous.FreeBoneWeights();
                }
                EditorUtility.CopySerialized(copy, existing);
                existing.meshData = converted;
                UnityEngine.Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existing);
                result = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(copy, assetPath);
                EditorUtility.SetDirty(copy);
                result = copy;
            }

            UMAAssetIndexer index = addToIndex ? UMAAssetIndexer.Instance : null;
            if (index != null)
            {
                if (index.GetAssetItem(typeof(SlotDataAsset), AssetItem.GetEvilName(result)) == null)
                {
                    index.EvilAddAsset(typeof(SlotDataAsset), result);
                }
                if (saveIndex)
                {
                    index.ForceSave();
                }
            }

            return result;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// Converts slots authored for one race's rig so they fit another race's rig, saving new
    /// SlotDataAssets next to the originals.
    /// </summary>
    public sealed class UMASlotRigConverterWindow : EditorWindow
    {
        [SerializeField] private RaceData sourceRace;
        [SerializeField] private RaceData targetRace;
        [SerializeField] private UMASlotRigConversionProfile profile;
        [SerializeField] private UMARigCorrectionMode correctionMode = UMARigCorrectionMode.JointAnchored;
        [SerializeField] private string nameSuffix = UMASlotRigConverterEditorUtility.DefaultSuffix;
        [SerializeField] private DefaultAsset outputFolder;
        [SerializeField] private bool addToIndex = true;
        [SerializeField] private bool replaceExisting = true;
        [SerializeField] private UMARigNormalMode normalMode = UMARigNormalMode.MatchSourceSkinning;
        [SerializeField] private List<SlotDataAsset> slots = new List<SlotDataAsset>();
        [SerializeField] private string lastReport = string.Empty;
        private Vector2 scroll;
        private Vector2 reportScroll;

        [MenuItem("UMA/Tools/Slot Tools/Convert Slots To Another Rig")]
        private static void Open()
        {
            GetWindow<UMASlotRigConverterWindow>("Rig Converter");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Convert slots from one rig to another", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bakes each slot to avatar space through its own bind poses, optionally moves each bone's influence " +
                "onto the target rig's joint, and rebinds it against the target race's rest skeleton. " +
                "New assets are written; the originals are left alone.",
                MessageType.Info);

            sourceRace = (RaceData)EditorGUILayout.ObjectField("Source race", sourceRace, typeof(RaceData), false);
            targetRace = (RaceData)EditorGUILayout.ObjectField("Target race", targetRace, typeof(RaceData), false);
            correctionMode = (UMARigCorrectionMode)EditorGUILayout.EnumPopup(new GUIContent("Correction",
                "None: exact bake and rebind. Joint anchored: also follow the target rig's joint positions and bone directions."), correctionMode);
            normalMode = (UMARigNormalMode)EditorGUILayout.EnumPopup(new GUIContent("Normals",
                "Match source skinning: the converted slot is lit exactly as the original is on its own rig. " +
                "Inverse transpose: follows the baked surface more closely where a vertex blends bones that sit far apart, but shades differently from the original."), normalMode);
            profile = (UMASlotRigConversionProfile)EditorGUILayout.ObjectField(new GUIContent("Profile (optional)", "Per-bone overrides."), profile, typeof(UMASlotRigConversionProfile), false);
            nameSuffix = EditorGUILayout.TextField(new GUIContent("Name suffix", "Appended to the asset file name and the slot name."), nameSuffix);
            outputFolder = (DefaultAsset)EditorGUILayout.ObjectField(new GUIContent("Output folder", "Leave empty to save next to each source slot."), outputFolder, typeof(DefaultAsset), false);
            addToIndex = EditorGUILayout.Toggle("Add to UMA index", addToIndex);
            replaceExisting = EditorGUILayout.Toggle(new GUIContent("Replace earlier conversions",
                "Converting a slot again writes into the asset the last conversion made, keeping its GUID so recipes that use it keep working. " +
                "Off: every run creates a new asset, which leaves two slots with the same name."), replaceExisting);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);
            if (GUILayout.Button("Add selected", GUILayout.Width(100f)))
            {
                foreach (UnityEngine.Object o in Selection.objects)
                {
                    if (o is SlotDataAsset sda && !slots.Contains(sda))
                    {
                        slots.Add(sda);
                    }
                }
            }
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                slots.Clear();
            }
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(80f), GUILayout.MaxHeight(220f));
            for (int i = 0; i < slots.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                slots[i] = (SlotDataAsset)EditorGUILayout.ObjectField(slots[i], typeof(SlotDataAsset), false);
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    slots.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+"))
            {
                slots.Add(null);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(sourceRace == null || targetRace == null || slots.Count == 0))
            {
                if (GUILayout.Button("Convert", GUILayout.Height(30f)))
                {
                    Convert();
                }
            }

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.LabelField("Last run", EditorStyles.boldLabel);
                reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(80f));
                EditorGUILayout.TextArea(lastReport, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void Convert()
        {
            var options = new UMASlotRigConversionOptions
            {
                CorrectionMode = correctionMode,
                NormalMode = normalMode,
                Profile = profile,
            };
            string folder = outputFolder != null ? AssetDatabase.GetAssetPath(outputFolder) : null;
            var log = new System.Text.StringBuilder();
            var created = new List<SlotDataAsset>();
            int skipped = 0;
            int failed = 0;
            try
            {
                if (!UMARigRestPose.TryFromRace(sourceRace, out UMARigRestPose sourceRig, out string sourceProblem))
                {
                    log.AppendLine("Source race: " + sourceProblem);
                }
                else if (!UMARigRestPose.TryFromRace(targetRace, out UMARigRestPose targetRig, out string targetProblem))
                {
                    log.AppendLine("Target race: " + targetProblem);
                }
                else
                {
                    if (profile != null && profile.HasClearBodyEffectors)
                    {
                        // Effectors that clear the body measure the clothing against the target race's skin.
                        options.BodySurface = UMAMeshHideAutoGenerator.BuildBodySurface(UMAMeshHideAutoGenerator.CollectBodySlots(targetRace, targetRig));
                    }
                    for (int i = 0; i < slots.Count; i++)
                    {
                        SlotDataAsset slot = slots[i];
                        if (slot == null)
                        {
                            continue;
                        }
                        EditorUtility.DisplayProgressBar("Converting slots", slot.slotName, (float)i / slots.Count);
                        if (!UMASlotRigConverter.CanConvert(slot.meshData, out string blocker))
                        {
                            log.AppendLine($"'{slot.slotName}' skipped: {blocker}");
                            skipped++;
                            continue;
                        }
                        if (UMASlotRigConverterEditorUtility.IsAlreadyOnRig(slot, targetRig, options))
                        {
                            log.AppendLine($"'{slot.slotName}' skipped: it is already bound to {targetRace.raceName}'s rig.");
                            skipped++;
                            continue;
                        }
                        try
                        {
                            SlotDataAsset result = UMASlotRigConverterEditorUtility.ConvertAndSaveCore(slot, sourceRig, targetRig, options,
                                nameSuffix, folder, addToIndex, replaceExisting, saveIndex: false, out UMASlotRigConversionReport report, out bool replaced);
                            created.Add(result);
                            log.AppendLine(report.Summary());
                            for (int w = 0; w < report.Warnings.Count; w++)
                            {
                                log.AppendLine("   warning: " + report.Warnings[w]);
                            }
                            for (int r = 0; r < report.RemappedBones.Count; r++)
                            {
                                log.AppendLine("   remapped: " + report.RemappedBones[r]);
                            }
                            log.AppendLine((replaced ? "   replaced: " : "   saved: ") + AssetDatabase.GetAssetPath(result));
                        }
                        catch (Exception ex)
                        {
                            log.AppendLine($"'{slot.slotName}' failed: {ex.GetType().Name}: {ex.Message}");
                            Debug.LogException(ex);
                            failed++;
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // One index save for the whole batch.
                if (addToIndex && created.Count > 0 && UMAAssetIndexer.Instance != null)
                {
                    UMAAssetIndexer.Instance.ForceSave();
                }
                AssetDatabase.SaveAssets();
            }

            log.Insert(0, $"{created.Count} converted, {skipped} skipped, {failed} failed.\n");
            lastReport = log.ToString();
            Debug.Log("[UMA] Rig conversion\n" + lastReport);
            if (created.Count > 0)
            {
                Selection.objects = created.ToArray();
                EditorGUIUtility.PingObject(created[0]);
            }
        }
    }
}

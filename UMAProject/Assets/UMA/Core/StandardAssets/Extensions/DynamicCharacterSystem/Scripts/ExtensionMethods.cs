using System;
using System.Collections.Generic;
using System.Text;
using UMA.PoseTools;

namespace UMA.CharacterSystem
{
    public static class UMAExtensions
    {
        /// <summary>
        /// Returns all BonePoseDNAConverters found in the controller's plugins.
        /// </summary>
        public static List<BonePoseDNAConverterPlugin.BonePoseDNAConverter> GetBonePoseConverters(this DynamicDNAConverterController controller)
        {
            var result = new List<BonePoseDNAConverterPlugin.BonePoseDNAConverter>();
            if (controller == null) return result;

            var plugins = controller.GetPlugins();
            if (plugins == null || plugins.Count == 0) return result;

            for (int i = 0; i < plugins.Count; i++)
            {
                var bp = plugins[i] as BonePoseDNAConverterPlugin;
                if (bp == null || bp.poseDNAConverters == null) continue;

                for (int j = 0; j < bp.poseDNAConverters.Count; j++)
                {
                    var conv = bp.poseDNAConverters[j];
                    if (conv != null) result.Add(conv);
                }
            }
            return result;
        }

        public static BonePoseDNAConverterPlugin EnsureBonePosePlugin(this DynamicDNAConverterController controller)
        {
            if (controller == null) return null;

            // Try existing
            var existing = controller.GetPlugins(typeof(BonePoseDNAConverterPlugin));
            if (existing != null && existing.Count > 0)
            {
                return existing[0] as BonePoseDNAConverterPlugin;
            }

            // Create new plugin
            var plugin = controller.AddPlugin(typeof(BonePoseDNAConverterPlugin)) as BonePoseDNAConverterPlugin;

#if UNITY_EDITOR
            if (plugin != null)
            {
                UnityEditor.Undo.RecordObject(controller, "Add BonePose Plugin");
                UnityEditor.EditorUtility.SetDirty(controller);
                UnityEditor.EditorUtility.SetDirty(plugin);
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif
            return plugin;
        }


        /// <summary>
        /// Removes all BonePoseDNAConverter entries that reference the given UMABonePose.
        /// Returns the number of converters removed.
        /// If removeEmptyPlugins is true, any BonePose plugin left with zero entries is deleted from the controller.
        /// </summary>
        public static int RemoveBonePoseConverters(
            this DynamicDNAConverterController controller,
            UMABonePose pose,
            bool removeEmptyPlugins = false)
        {
            if (controller == null || pose == null) return 0;

            int removed = 0;
            var plugins = controller.GetPlugins(typeof(BonePoseDNAConverterPlugin));
            if (plugins == null || plugins.Count == 0) return 0;

            for (int p = plugins.Count - 1; p >= 0; p--)
            {
                var bp = plugins[p] as BonePoseDNAConverterPlugin;
                if (bp == null || bp.poseDNAConverters == null) continue;

                bool anyRemovedFromThis = false;

                // Remove matching converters (iterate backwards)
                for (int i = bp.poseDNAConverters.Count - 1; i >= 0; i--)
                {
                    var conv = bp.poseDNAConverters[i];
                    if (conv == null) continue;

                    var target = conv.poseToApply;
                    bool match =
                        target == pose ||
                        (target != null && pose != null && target.GetInstanceID() == pose.GetInstanceID());

                    if (match)
                    {
#if UNITY_EDITOR
                        UnityEditor.Undo.RecordObject(bp, "Remove Bone Pose Converter");
#endif
                        bp.poseDNAConverters.RemoveAt(i);
                        removed++;
                        anyRemovedFromThis = true;
                    }
                }

#if UNITY_EDITOR
                if (anyRemovedFromThis)
                {
                    UnityEditor.EditorUtility.SetDirty(bp);
                }
#endif

                // Optionally remove empty plugin assets
                if (removeEmptyPlugins && (bp.poseDNAConverters == null || bp.poseDNAConverters.Count == 0))
                {
#if UNITY_EDITOR
                    UnityEditor.Undo.RecordObject(controller, "Remove Empty BonePose Plugin");
#endif
                    controller.DeletePlugin(bp);
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(controller);
#endif
                }
            }

#if UNITY_EDITOR
            if (removed > 0)
            {
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif
            return removed;
        }


        /// <summary>
        /// Adds a BonePoseDNAConverter entry to the controller's BonePose plugin.
        /// - pose: the UMABonePose to apply
        /// - startingWeight: default weight (0..1) applied when no modifying DNA is used
        /// - modifyingDNA: optional DNAEvaluatorList to drive the pose weight from DNA
        /// Returns the created converter, or null on failure.
        /// </summary>
        public static BonePoseDNAConverterPlugin.BonePoseDNAConverter AddBonePoseConverter(
            this DynamicDNAConverterController controller,
            UMABonePose pose,
            float startingWeight = 1f,
            DNAEvaluatorList modifyingDNA = null)
        {
            if (controller == null || pose == null) return null;

            var plugin = controller.EnsureBonePosePlugin();
            if (plugin == null) return null;

            var conv = new BonePoseDNAConverterPlugin.BonePoseDNAConverter
            {
                poseToApply = pose,
                startingPoseWeight = UnityEngine.Mathf.Clamp01(startingWeight)
            };

            if (modifyingDNA != null)
            {
                conv.modifyingDNA = new DNAEvaluatorList(modifyingDNA);
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(plugin, "Add Bone Pose Converter");
#endif
            if (plugin.poseDNAConverters == null)
            {
                plugin.poseDNAConverters = new List<BonePoseDNAConverterPlugin.BonePoseDNAConverter>();
            }
            plugin.poseDNAConverters.Add(conv);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(plugin);
            UnityEditor.EditorUtility.SetDirty(controller);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
            return conv;
        }

        public static int WordCount(this String str)
        {
            return str.Split(new char[] { ' ', '.', '?' },
            StringSplitOptions.RemoveEmptyEntries).Length;
        }
        public static string ToTitleCase(this String str)
        {
            char[] sep = { ' ' };

            string[] words = str.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length > 2)
                {
                    string s1 = word.Substring(0, 1).ToUpper();
                    string s2 = word.Substring(1, word.Length - 1).ToLower();
                    sb.Append(s1);
                    sb.Append(s2);
                }
                else
                {
                    sb.Append(word.ToUpper());
                }
            }
            return sb.ToString();
        }

        public static string[] SplitCamelCase(this String str)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append('|');
                }
                if (i == 0)
                {
                    c = char.ToUpper(c);
                }

                sb.Append(c);
            }
            return sb.ToString().Split('|');
        }

        public static string MenuCamelCase(this String str)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append('/');
                }
                if (i == 0)
                {
                    c = char.ToUpper(c);
                }

                sb.Append(c);
            }
            return sb.ToString();
        }


        public static string BreakupCamelCase(this String str)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append(' ');
                }
                if (i == 0)
                {
                    c = char.ToUpper(c);
                }

                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
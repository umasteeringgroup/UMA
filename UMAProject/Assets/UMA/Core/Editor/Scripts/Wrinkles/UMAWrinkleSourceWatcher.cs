using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Auto-import for hand-edited wrinkle map sources: while "UMA > Skin > Auto-Import Wrinkle Map Sources" is on,
    /// saving the height, crease or flush image in the folder last used by Export / Import Wrinkle Map Sources
    /// rebuilds UMA3_Wrinkles.1001.png (and its height) without any dialog. Paint in an image editor, save, and the
    /// head updates when Unity next has focus.
    /// </summary>
    [InitializeOnLoad]
    public static class UMAWrinkleSourceWatcher
    {
        const string MenuPath = "UMA/Skin/Auto-Import Wrinkle Map Sources (UMA 3 Head)";
        const string EnabledPref = "UMA.WrinkleMapSources.AutoImport";
        const double PollSeconds = 1.0;
        // A file must keep the same size and time for this long before it is read, so a save in progress is not.
        const double SettleSeconds = 0.75;

        static double nextPoll;
        static string watchedFolder;
        static DateTime lastImported; // newest source time already imported
        static DateTime pendingTime;
        static long pendingSize = -1;
        static double pendingSince;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, false);
            set
            {
                EditorPrefs.SetBool(EnabledPref, value);
                Reset();
            }
        }

        static UMAWrinkleSourceWatcher()
        {
            EditorApplication.update += Update;
        }

        [MenuItem(MenuPath, priority = 44)]
        static void Toggle()
        {
            Enabled = !Enabled;
            string folder = UMAWrinkleMapBaker.LastSourceFolder;
            if (Enabled)
                Debug.Log(string.IsNullOrEmpty(folder) || !Directory.Exists(folder)
                    ? "Wrinkle map auto-import is on. Export or import the wrinkle map sources once to choose the folder to watch."
                    : "Wrinkle map auto-import is on: saving the height, crease or flush image in " + folder + " rebuilds " + UMAWrinkleMapBaker.UMA3WrinkleMapPath + ".");
            else Debug.Log("Wrinkle map auto-import is off.");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>Forget what was seen, so the current files count as already imported.</summary>
        public static void Reset()
        {
            watchedFolder = null;
            pendingSize = -1;
        }

        /// <summary>The newest write time and the total size of the three sources in a folder; false if one is missing.</summary>
        public static bool ReadState(string folder, out DateTime newest, out long size)
        {
            newest = DateTime.MinValue; size = 0;
            foreach (var suffix in new[] { UMAWrinkleMapBaker.SourceHeightSuffix, UMAWrinkleMapBaker.SourceCreaseSuffix, UMAWrinkleMapBaker.SourceFlushSuffix })
            {
                var info = new FileInfo(UMAWrinkleMapBaker.SourcePath(folder, UMAWrinkleMapBaker.UMA3WrinkleMapPath, suffix));
                if (!info.Exists) return false;
                if (info.LastWriteTimeUtc > newest) newest = info.LastWriteTimeUtc;
                size += info.Length;
            }
            return true;
        }

        static void Update()
        {
            if (!Enabled || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + PollSeconds;

            string folder = UMAWrinkleMapBaker.LastSourceFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            if (!ReadState(folder, out DateTime newest, out long size)) return;
            if (!string.Equals(folder, watchedFolder, StringComparison.OrdinalIgnoreCase))
            {
                // A new folder (or the first poll): its current files are the starting point, not a change.
                watchedFolder = folder;
                lastImported = newest;
                pendingSize = -1;
                return;
            }
            if (newest <= lastImported) return;
            if (newest != pendingTime || size != pendingSize)
            {
                pendingTime = newest; pendingSize = size; pendingSince = now;
                return;
            }
            if (now - pendingSince < SettleSeconds) return;

            lastImported = newest;
            pendingSize = -1;
            Import(folder);
        }

        static void Import(string folder)
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(UMAWrinkleMapBaker.UMA3HeadSlotPath);
            if (slot == null) { Debug.LogWarning("Wrinkle map auto-import: the UMA 3 head slot was not found at " + UMAWrinkleMapBaker.UMA3HeadSlotPath); return; }
            try
            {
                var result = UMAWrinkleMapBaker.ImportSources(slot, folder, UMAWrinkleMapBaker.UMA3WrinkleMapPath);
                Debug.Log("Wrinkle map auto-imported from " + folder + ". " + result.Report);
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is ArgumentException)
            {
                // Most often a file that an image editor is still writing; the next save is picked up again.
                Debug.LogWarning("Wrinkle map auto-import skipped: " + e.Message);
            }
        }
    }
}

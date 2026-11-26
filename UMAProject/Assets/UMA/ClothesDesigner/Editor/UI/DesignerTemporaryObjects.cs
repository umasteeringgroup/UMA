using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The designer's temporary GameObjects (the cloth preview, the refit preview, the try-on character and
    /// its view, the converter's character), and a watchdog that removes any of them no live designer owns.
    /// They are never saved (DontSave), but one left behind by a script reload, an error or a closed designer
    /// would still show in the artist's scene. The watchdog looks after every script reload, scene open and
    /// hierarchy change, and when a designer closes.
    /// </summary>
    [InitializeOnLoad]
    public static class DesignerTemporaryObjects
    {
        public const string ClothName = "Clothes Designer Cloth";
        public const string RefitName = "Clothes Designer Refit";
        public const string TryOnName = "Clothes Designer Try-On (temporary)";
        public const string TryOnViewName = "Clothes Designer Try-On View";
        public const string ConverterName = "Clothes Designer Converter (temporary)";

        public const string BodyShapesName = "Clothes Designer Body Shapes (temporary)";
        private static readonly string[] Names = { ClothName, RefitName, TryOnName, TryOnViewName, ConverterName, BodyShapesName };
        private static readonly HashSet<GameObject> owned = new HashSet<GameObject>();
        private static bool checkQueued;

        static DesignerTemporaryObjects()
        {
            EditorApplication.hierarchyChanged += QueueCheck;
            EditorSceneManager.sceneOpened += (scene, mode) => QueueCheck();
            // Unity does not always report a DontSave object as a hierarchy change, so the open scenes' root
            // objects are also looked at every few seconds (a few microseconds).
            EditorApplication.update += Poll;
            // After a script reload nothing is owned yet: anything left from before is a stray.
            QueueCheck(true);
        }

        private const double PollSeconds = 2.0;
        private static double nextPoll;

        private static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + PollSeconds;
            RemoveStrays(false);
        }

        /// <summary>A new temporary object, owned until <see cref="Destroy"/>.</summary>
        public static GameObject Create(string name, HideFlags flags)
        {
            var go = new GameObject(name) { hideFlags = flags | HideFlags.DontSave };
            owned.Add(go);
            return go;
        }

        /// <summary>
        /// A new temporary object in a stage's own scene. Null when that scene is gone (the stage closed): the
        /// object would otherwise land in the artist's open scene.
        /// </summary>
        public static GameObject CreateInScene(string name, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject go = Create(name, HideFlags.DontSave | HideFlags.NotEditable);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            owned.Remove(go);
            UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>Looks for strays on the next editor update (once, however often it is asked).</summary>
        public static void QueueCheck() => QueueCheck(false);

        private static bool fullCheckQueued;

        private static void QueueCheck(bool full)
        {
            fullCheckQueued |= full;
            if (checkQueued) return;
            checkQueued = true;
            // Not delayCall: it does not run while the editor is in the background.
            EditorApplication.update += RunQueuedCheck;
        }

        private static void RunQueuedCheck()
        {
            EditorApplication.update -= RunQueuedCheck;
            checkQueued = false;
            bool full = fullCheckQueued;
            fullCheckQueued = false;
            RemoveStrays(full);
        }

        /// <summary>
        /// Removes the designer's temporary objects that no live designer owns. The open scenes' root objects
        /// are looked at; with <paramref name="includeHidden"/>, every loaded GameObject too (hidden ones are
        /// not listed in a scene).
        /// </summary>
        public static int RemoveStrays(bool includeHidden)
        {
            owned.RemoveWhere(g => g == null);
            var strays = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (IsStray(root)) strays.Add(root);
                }
            }
            if (includeHidden)
            {
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go.transform.parent == null && !EditorUtility.IsPersistent(go) && IsStray(go) && !strays.Contains(go)) strays.Add(go);
                }
            }
            foreach (GameObject go in strays)
            {
                Debug.LogWarning("Clothes Designer: removed '" + go.name + "', a temporary object left behind (it is never saved).");
                UnityEngine.Object.DestroyImmediate(go);
            }
            return strays.Count;
        }

        private static bool IsStray(GameObject go) =>
            go != null && (go.hideFlags & HideFlags.DontSaveInEditor) != 0 && Array.IndexOf(Names, go.name) >= 0 && !owned.Contains(go);
    }
}

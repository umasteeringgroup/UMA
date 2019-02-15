using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TypePair = System.Collections.Generic.KeyValuePair<System.Type, string>;
using System.Diagnostics;
using System;

// Simple Editor Script that fills a bar in the given seconds.
namespace UMA
{
    public class TagsEditor : EditorWindow
    {
        public class ProcessItem
        {
            public delegate void Processor(ProcessItem p);
            public UnityEngine.Object oObj;
            public Processor pFunc;
            public TypePair typePair;

            public ProcessItem(TypePair t, UnityEngine.Object o, Processor pFunc)
            {
                this.pFunc = pFunc;
                oObj = o;
                typePair = t;
            }

            public void Process()
            {
                pFunc(this);
            }
        }

        Queue<ProcessItem> Items = new Queue<ProcessItem>();
        float TotalItems;
        bool Cleanup = false;
        Stopwatch stopWatch = new Stopwatch();

        [MenuItem("UMA/Tags Editor")]
        static void Init()
        {
            UnityEditor.EditorWindow window = GetWindow(typeof(TagsEditor), true);
            window.CenterOnMainWin();
            window.Show();
        }

        public void LoadItemList(ProcessItem.Processor pFunc)
        {
            foreach(TypePair t in UMAEditorUtilities.FriendlyNames)
            {
                var objs = Resources.FindObjectsOfTypeAll(t.Key);
                if (objs == null) continue;
                foreach(var o in objs)
                {
                    Items.Enqueue(new ProcessItem(t, o, pFunc));
                }
            }
            TotalItems = Items.Count;
        } 

        void OnGUI()
        {
            GUILayout.Label("Warning: This can take a few minutes.");
            GUILayout.Label("The AssetDatabase will be refreshed");
            GUILayout.Label("when complete.");

            if (GUILayout.Button("Set UMA Tags") && Items.Count == 0)
            {
                LoadItemList(SetItems);
            }
            if (GUILayout.Button("Clear UMA Tags") & Items.Count == 0)
            {
                LoadItemList(ClearItems);
            }
            if (Items.Count > 0)
            {
                stopWatch.Reset();
                stopWatch.Start();
                while (Items.Count > 0)
                {
                    ProcessItem p = Items.Dequeue();
                    p.Process();
                    if (stopWatch.ElapsedMilliseconds > 100)
                    {
                        break;
                    }
                }
                stopWatch.Stop();
                if (Items.Count == 0)
                {
                    Cleanup = true;
                    EditorUtility.DisplayProgressBar("UMA Tags","Refreshing AssetDatabase" , 1.0f);
                }
            }
            if (Event.current.type == EventType.Layout && Cleanup)
            {
                Cleanup = false;
                AssetDatabase.SaveAssets();
                EditorUtility.ClearProgressBar();
            }
        }

        private void SetItems(ProcessItem p)
        {
            string tagName = "UMA_" + p.typePair.Value;
            string[] currentLabels = AssetDatabase.GetLabels(p.oObj);
            ArrayUtility.Add<string>(ref currentLabels, tagName);
            AssetDatabase.SetLabels(p.oObj, currentLabels);
            EditorUtility.SetDirty(p.oObj);
            float progress = (TotalItems - Items.Count) / TotalItems;
            EditorUtility.DisplayProgressBar("UMA Tags", "Setting tag " +tagName+ " (" + Items.Count + ") "+p.oObj.name, progress);
        }

        private void ClearItems(ProcessItem p)
        {
            string tagName = "UMA_" + p.typePair.Value;
            string[] currentLabels = AssetDatabase.GetLabels(p.oObj);
            ArrayUtility.Remove<string>(ref currentLabels, tagName);
            AssetDatabase.SetLabels(p.oObj, currentLabels);
            EditorUtility.SetDirty(p.oObj);
            float progress = (TotalItems-Items.Count) / TotalItems;
            EditorUtility.DisplayProgressBar("UMA Tags", "Clearing tag " + tagName + " (" + Items.Count + ") " + p.oObj.name, progress);
        }

        void OnInspectorUpdate() 
        {
            Repaint();
        }
    }
}
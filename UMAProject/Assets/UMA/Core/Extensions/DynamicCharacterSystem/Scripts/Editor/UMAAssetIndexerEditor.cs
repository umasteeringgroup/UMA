using UnityEngine;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;
using UnityEditor;
using UnityEditor.Animations;
using UMAEditor;

[CustomEditor(typeof(UMAAssetIndexer))]
public class UMAAssetIndexerEditor : Editor
{
    Dictionary<System.Type,bool> Toggles = new Dictionary<System.Type,bool>();
    UMAAssetIndexer UAI;
    List<Object> AddedDuringGui = new List<Object>();
    List<System.Type> AddedTypes = new List<System.Type>();
    List<UMAAssetIndexer.AssetItem> DeletedDuringGUI = new List<UMAAssetIndexer.AssetItem>();
    List<System.Type> RemovedTypes = new List<System.Type>();

    [MenuItem("UMA/UMA Global Library")]
    public static void Init()
    {
        UMAAssetIndexer ua = UMAAssetIndexer.Instance;
        Selection.activeObject = ua.gameObject;
    }

    #region Drag Drop
    private void DropAreaGUI(Rect dropArea)
    {

        var evt = Event.current;

        if (evt.type == EventType.DragUpdated)
        {
            if (dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        if (evt.type == EventType.DragPerform)
        {
            if (dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                AddedDuringGui.Clear();
                UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                for (int i = 0; i < draggedObjects.Length; i++)
                {
                    if (draggedObjects[i])
                    {
                        AddedDuringGui.Add(draggedObjects[i]);

                        var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                        if (System.IO.Directory.Exists(path))
                        {
                            RecursiveScanFoldersForAssets(path);
                        }
                    }
                }
            }
        }
    }

    private void DropAreaType(Rect dropArea)
    {

        var evt = Event.current;

        if (evt.type == EventType.DragUpdated)
        {
            if (dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        if (evt.type == EventType.DragPerform)
        {
            if (dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                AddedTypes.Clear();
                UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                for (int i = 0; i < draggedObjects.Length; i++)
                {
                    if (draggedObjects[i])
                    {
                        System.Type sType = draggedObjects[i].GetType();

                        AddedTypes.Add(sType);
                    }
                }
            }
        }
    }

    private void AddObject(Object draggedObject)
    {
        System.Type type = draggedObject.GetType();
        if (UAI.IsIndexedType(type))
        {
            UAI.EvilAddAsset(type, draggedObject);
        }
    }

    private void RecursiveScanFoldersForAssets(string path)
    {
        var assetFiles = System.IO.Directory.GetFiles(path);

        foreach (var assetFile in assetFiles)
        {
            string Extension = System.IO.Path.GetExtension(assetFile).ToLower();
            if (Extension == ".asset" || Extension == ".controller")
            {
                Object o = AssetDatabase.LoadMainAssetAtPath(assetFile);

                if (o)
                {
                    AddedDuringGui.Add(o);
                }
            }
        }
        foreach (var subFolder in System.IO.Directory.GetDirectories(path))
        {
            RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
        }
    }
    #endregion

    private void Cleanup()
    {
        if (AddedDuringGui.Count > 0 || DeletedDuringGUI.Count > 0 || AddedTypes.Count > 0 || RemovedTypes.Count > 0)
        {
            foreach (Object o in AddedDuringGui)
            {
                AddObject(o);
            }

            foreach (UMAAssetIndexer.AssetItem ai in DeletedDuringGUI)
            {
                UAI.RemoveAsset(ai._Type, ai._Name);
            }

            foreach (System.Type st in RemovedTypes)
            {
                UAI.RemoveType(st);
            }

            foreach (System.Type st in AddedTypes)
            {
                UAI.AddType(st);
            }

            AddedTypes.Clear();
            RemovedTypes.Clear();
            DeletedDuringGUI.Clear();
            AddedDuringGui.Clear();

            UAI.ForceSave();
            Repaint();
        }
    }

    private void SetFoldouts(bool Value)
    {
        System.Type[] Types = UAI.GetTypes();
        foreach (System.Type t in Types)
        {
            Toggles[t] = Value;
        }
    }

    public override void OnInspectorGUI()
    {
        UAI = target as UMAAssetIndexer;

        if (Event.current.type == EventType.Layout)
        {
            Cleanup();
        }

        ShowTypes();

        // Draw and handle the Drag/Drop
        GUILayout.Space(20);
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag Indexable Assets here. Non indexed assets will be ignored.");
        GUILayout.Space(20);
        DropAreaGUI(dropArea);

        System.Type[] Types = UAI.GetTypes();
        if (Toggles.Count != Types.Length) SetFoldouts(false);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Collapse All"))
        {
            SetFoldouts(false);
        }

        if (GUILayout.Button("Expand All"))
        {
            SetFoldouts(true);
        }
        if (GUILayout.Button("Reindex Names"))
        {
            UAI.RebuildIndex();
        }
        if (GUILayout.Button("Clear"))
        {
            UAI.Clear();
        }
        GUILayout.EndHorizontal();

        foreach (System.Type t in Types)
        {
            if (t != typeof(AnimatorController)) // Somewhere, a kitten died because I typed that.
            {
                ShowArray(t);
            }
        }
    }

    public void ShowArray(System.Type CurrentType)
    {
        Dictionary<string, UMAAssetIndexer.AssetItem> TypeDic = UAI.GetAssetDictionary(CurrentType);

        GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
        GUILayout.Space(10);
        Toggles[CurrentType] = EditorGUILayout.Foldout(Toggles[CurrentType], CurrentType.Name + ": " + TypeDic.Count + " Item(s)");


        GUILayout.EndHorizontal();

        if (Toggles[CurrentType]) 
        {
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            GUILayout.BeginHorizontal(); 
            GUILayout.Label("Sorted By: " + UMAAssetIndexer.SortOrder, GUILayout.MaxWidth(160));
            foreach (string s in UMAAssetIndexer.SortOrders)
            {
                if (GUILayout.Button(s, GUILayout.Width(80)))
                {
                    UMAAssetIndexer.SortOrder = s;
                }
            }
            GUILayout.EndHorizontal();


            List<UMAAssetIndexer.AssetItem> Items = new List<UMAAssetIndexer.AssetItem>();
            Items.AddRange(TypeDic.Values);
            Items.Sort();
            foreach (UMAAssetIndexer.AssetItem ai in Items)
            {
                GUILayout.BeginHorizontal(EditorStyles.textField);

                string lblVal = ai.ToString(UMAAssetIndexer.SortOrder);
                if (GUILayout.Button(lblVal /* ai._Name + " (" + ai._AssetBaseName + ")" */, EditorStyles.label))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(ai._Path));
                }

                if (GUILayout.Button("-", GUILayout.Width(20.0f)))
                {
                    DeletedDuringGUI.Add(ai);
                }
                GUILayout.EndHorizontal();
            }

            GUIHelper.EndVerticalPadded(10);
        }
    }

    public bool bShowTypes;

    public void ShowTypes()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
        GUILayout.Space(10);
        bShowTypes = EditorGUILayout.Foldout(bShowTypes, "Additional Indexed Types");
        GUILayout.EndHorizontal();

        if (bShowTypes)
        {
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

            // Draw and handle the Drag/Drop
            GUILayout.Space(20);
            Rect dropTypeArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropTypeArea, "Drag a single type here to start indexing that type.");
            GUILayout.Space(20);
            DropAreaType(dropTypeArea);
            foreach(string s in UAI.IndexedTypeNames)
            {
                System.Type CurrentType = System.Type.GetType(s);
                GUILayout.BeginHorizontal(EditorStyles.textField);
                GUILayout.Label(CurrentType.ToString(), GUILayout.MinWidth(240));
                if (GUILayout.Button("-", GUILayout.Width(20.0f)))
                {
                    RemovedTypes.Add(CurrentType);
                }
                GUILayout.EndHorizontal();
            }
            GUIHelper.EndVerticalPadded(10);
        }
    }
}
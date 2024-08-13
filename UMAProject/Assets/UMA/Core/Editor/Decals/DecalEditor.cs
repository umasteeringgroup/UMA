using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;


/// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
/// Are we missing                 // SkinnedMeshAligner.AlignBindPose(prefabMesh, resultingSkinnedMesh);
/// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

namespace UMA
{
    public class DecalEditor : IEditorScene
    {
        static int instance = 0;
        int localInstance;
        Scene scene;
        InteractiveUMAWindow sceneView;
        private Rect infoRect = new Rect(10, 30, 400, 30);
        private GUIStyle whiteLabels;
        private GUIStyle blackLabels;
        private GameObject Character;
        private DynamicCharacterAvatar avatar;
        private GameObject sceneRoot;
        // UMA transform bones
        private Transform Root;
        private Transform Global;
        private Transform Position;

        public Material ScreenBlocker;
        public Texture blockerTex;

        private bool showHelp;
        private bool showRadius = true;
        private int savedLockedLayers;
        private DecalIndicator decalIndicator;
        private DecalManager decalManager;
        float currentRotation = 0;
        float distance = 0.006f;
        public Material decalMaterial;
        public List<string> RaceNames = new List<string>();
        int raceNumber = 0;

        private GameObject VertexMarker;
        private GameObject PlanesMarker;

        bool ShowSlotDialog = false;
        DecalDefinition SlotDecal;

        private string SaveFolder;
        private string decalSlotName;
        int decalNum;

        public int refVertexNumber;
        public Vector3 refVertexPosition;

        NativeArray<byte> basebonesPerVertex;
        NativeArray<BoneWeight1> baseboneWeights;
        List<string> boneNames = new List<string>();


        public DecalEditor()
        {
            localInstance = instance;
            instance++;
        }


        [MenuItem("UMA/Interactive Decals (EXPERIMENTAL)")]
        public static void Init()
        {
            DecalEditor de = new DecalEditor();
            InteractiveUMAWindow.Init("UMA Decals - EXPERIMENTAL", de);
        }

        private void ResetLabelStart()
        {
            infoRect = new Rect(10, 20, 400, 30);
        }

        private void MoveToNextMessage(float xoffset, float yoffset)
        {
            infoRect.x += xoffset;
            infoRect.y += yoffset;
        }

        private void DrawNextLabel(string lbl)
        {
            // Frame the text so it's visible everywhere
            MoveToNextMessage(-1, -1);
            GUI.Label(infoRect, lbl, blackLabels);
            MoveToNextMessage(2, 0);
            GUI.Label(infoRect, lbl, blackLabels);
            MoveToNextMessage(0, 2);
            GUI.Label(infoRect, lbl, blackLabels);
            MoveToNextMessage(-2, 0);
            GUI.Label(infoRect, lbl, blackLabels);
            MoveToNextMessage(1, -1);
            GUI.Label(infoRect, lbl, whiteLabels);
            MoveToNextMessage(0, 20);
        }

        public void Initialize(InteractiveUMAWindow sceneView, Scene scene)
        {
            this.sceneView = sceneView;
            this.scene = scene;
            ResetLabelStart();
            whiteLabels = new GUIStyle(EditorStyles.boldLabel);
            blackLabels = new GUIStyle(EditorStyles.boldLabel);
            whiteLabels.normal.textColor = Color.white;
            blackLabels.normal.textColor = Color.black;
        }

        void HelpWindow(int WindowID)
        {
            GUILayout.Label("Left click and drag to area select");
            GUILayout.Label("Hold SHIFT while dragging to ADD polygons");
            GUILayout.Label("Hold CTRL while dragging to REMOVE polygons");
            GUILayout.Label("Hold ALT while dragging to orbit");
        }

        Plane GetPlaneInWorldSpace(GameObject planeObject)
        {
            // get the corners of the plane in worldspace 
            // return new plane for those corners.

            // 0, 10, 110

            MeshFilter m = planeObject.GetComponent<MeshFilter>();
            Transform t = planeObject.transform;

            Vector3 v0 = t.TransformPoint(m.sharedMesh.vertices[0]);
            Vector3 v2 = t.TransformPoint(m.sharedMesh.vertices[10]);
            Vector3 v1 = t.TransformPoint(m.sharedMesh.vertices[110]);
#if SHOW_PLANES
            GameObject g1 = GameObject.Instantiate(PlanesMarker, v0, Quaternion.identity, Root);
            GameObject g2 = GameObject.Instantiate(PlanesMarker, v1, Quaternion.identity, Root);
            GameObject g3 = GameObject.Instantiate(PlanesMarker, v2, Quaternion.identity, Root);
            SceneManager.MoveGameObjectToScene(g1, scene);
            SceneManager.MoveGameObjectToScene(g2, scene);
            SceneManager.MoveGameObjectToScene(g3, scene); 
#endif
            return new Plane(v0, v1, v2);
        }

        Plane[] GetPlanesInWorldspace()
        {
            Plane[] WorldspacePlanes = new Plane[6];

            WorldspacePlanes[0] = GetPlaneInWorldSpace(decalIndicator.U1);
            WorldspacePlanes[1] = GetPlaneInWorldSpace(decalIndicator.U2);
            WorldspacePlanes[2] = GetPlaneInWorldSpace(decalIndicator.V1);
            WorldspacePlanes[3] = GetPlaneInWorldSpace(decalIndicator.V2);
            WorldspacePlanes[4] = GetPlaneInWorldSpace(decalIndicator.Front);
            WorldspacePlanes[5] = GetPlaneInWorldSpace(decalIndicator.Back);

            return WorldspacePlanes;
        }

        private void SlotDialog(int id)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            EditorGUILayout.LabelField("Save Slot",EditorStyles.boldLabel);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Folder", GUILayout.Width(100)))
            {
                SaveFolder = EditorUtility.OpenFolderPanel("Select destination folder", SaveFolder, "Decal_" + decalNum);
                PlayerPrefs.SetString("UMADecalFolder", SaveFolder);
            }
            GUILayout.Space(16);
            GUILayout.Label(SaveFolder);
            GUILayout.EndHorizontal();


            GUILayout.BeginHorizontal();
            GUILayout.Label("Enter Name", GUILayout.Width(100));
            SlotDecal.Name = GUILayout.TextField(SlotDecal.Name);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (String.IsNullOrEmpty(SaveFolder))
            {
                GUILayout.Label("Select a folder to save the prefab");
            }
            else
            {
                GUILayout.Label(" ");
            }

            GUILayout.Space(60);
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.ExpandWidth(true));
           
            if (GUILayout.Button("Save",GUILayout.Width(60)))
            {
                if (string.IsNullOrEmpty(SaveFolder))
                {
                    EditorUtility.DisplayDialog("error", "Please select a folder before saving the slot!", "OK");
                }
                else
                {
                    SaveSlot(SlotDecal);
                    ShowSlotDialog = false;
                }
            }
            if (GUILayout.Button("Exit",GUILayout.Width(60)))
            {
                ShowSlotDialog = false;
            }
            GUILayout.EndHorizontal();
        }

        void SaveSlot(DecalDefinition d)
        {
            SimpleDecal sd = d.DecalMeshObject.GetComponent<SimpleDecal>();
            sd.SaveDecal(d.Name, SaveFolder, true, Character.GetComponentInChildren<SkinnedMeshRenderer>(),d,VertexMarker,scene,sceneRoot);
        }

        void DecalList(int WindowID)
        {
            DecalDefinition deleteMe = null;

            EditorGUILayout.BeginHorizontal();
            raceNumber = EditorGUILayout.Popup(raceNumber, RaceNames.ToArray(),GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Change Race",GUILayout.ExpandWidth(false)))
            {
                if (raceNumber >= 0)
                {
                    Debug.Log($"Changing race to {raceNumber}: {RaceNames[raceNumber]} ");
                    string theRace = RaceNames[raceNumber];
                    avatar.ForceRaceChange(theRace);
                    avatar.GenerateSingleUMA();
                }
                
                foreach (DecalDefinition d in decalManager.Decals)
                {
                    if (d.DecalMeshObject != null)
                    {
                        GameObject.DestroyImmediate(d.DecalMeshObject);
                    }
                }
                decalManager.Decals.Clear();
            }
            if (GUILayout.Button("Insp Char", GUILayout.ExpandWidth(false)))
            {
                InspectorUtlity.InspectTarget(Character);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            decalNum = 1;
            foreach(DecalDefinition d in decalManager.Decals)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(d.Name, GUILayout.ExpandWidth(true)))
                {
                    Selection.activeGameObject = d.DecalMeshObject;
                    sceneView.FrameSelected();
                }
                
                if (GUILayout.Button("Insp",GUILayout.Width(42)))
                {
                    InspectorUtlity.InspectTarget(d.DecalMeshObject);
                }
                if (GUILayout.Button("Save", GUILayout.Width(42)))
                {
                    SlotDecal = d;
                    SaveFolder = PlayerPrefs.GetString("UMADecalFolder", "");
                    ShowSlotDialog = true;
                }
                if (GUILayout.Button("x",GUILayout.Width(20)))
                {
                    deleteMe = d;
                }
                EditorGUILayout.EndHorizontal();
                decalNum++;
            }

            if (deleteMe != null)
            {
                decalManager.Decals.Remove(deleteMe);
                if (deleteMe.DecalMeshObject != null)
                {
                    GameObject.DestroyImmediate(deleteMe.DecalMeshObject);
                }
            }
        }

        void SceneWindow(int WindowID)
        {
            if (decalIndicator == null)
            {
                return;
            }

            float val = decalIndicator.transform.localScale.x;

            float newval = EditorGUILayout.Slider("Decal Size", val, 0.0f, 1.0f);
            if (val != newval)
            {
                decalIndicator.transform.localScale = new Vector3(newval, newval, newval);
                EditorPrefs.SetFloat("DecalIndicatorScale", newval);
            }

            float newRotation = EditorGUILayout.Slider("Decal Rotation", currentRotation, 0.0f, 360.0f);
            if (newRotation != currentRotation)
            {
                Vector3 localEuler = decalIndicator.LocalEuler;
                localEuler.z = newRotation;
                decalIndicator.transform.localEulerAngles = localEuler;
                currentRotation = newRotation;
            }

            distance = EditorGUILayout.Slider("Decal Offset",distance, 0.001f, 0.02f);

            showRadius = GUILayout.Toggle(showRadius, "Show indicator");
            if (GUI.changed)
            {
                decalIndicator.gameObject.SetActive(showRadius);
            }

            var newMaterial = EditorGUILayout.ObjectField(decalMaterial, typeof(Material),false) as Material;

            if (newMaterial != decalMaterial)
            {
                decalIndicator.visualPlane.GetComponent<MeshRenderer>().material = newMaterial;
                decalMaterial = newMaterial;
            }

            if (GUILayout.Button("Capture current decal"))
            {
                // Todo: get all meshes
                Mesh bakedMesh = FreezeCurrentMesh(sceneView);

                GameObject newDecal = GameObject.Instantiate(decalIndicator.visualPlane,Character.transform.position,Character.transform.rotation);

                newDecal.transform.localScale= new Vector3(1f, 1f, 1f);
                MeshFilter DecalMeshFilter = newDecal.GetComponent<MeshFilter>();

                Mesh decalMesh = Mesh.Instantiate(DecalMeshFilter.sharedMesh);

                Vector3[] verts = DecalMeshFilter.sharedMesh.vertices;

                PhysicsScene physcene = PhysicsSceneExtensions.GetPhysicsScene(scene);

                Transform t = decalIndicator.visualPlane.transform;

                Mesh characterMesh  = avatar.umaData.GetRenderer(0).sharedMesh;

                NativeArray<byte> basebonesPerVertex = characterMesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> baseboneWeights = characterMesh.GetAllBoneWeights();
                Dictionary<int, int> VertexBoneWeightOffset = CalculateVertxBoneWeightOffsets(basebonesPerVertex,baseboneWeights);

                List<BoneWeight1> newBoneWeights = new List<BoneWeight1>();
                List<byte> newBonesPerVertex = new List<byte>();
                
               // logbones = true;
                FoundBones.Clear();


                for (int i=0;i<verts.Length;i++)
                {
                    Vector3 src =t.TransformPoint(verts[i]);
                    verts[i] = LocalRayHit(physcene, src , distance, bakedMesh,VertexBoneWeightOffset,basebonesPerVertex,baseboneWeights);
                    if (lastboneWeights != null)
                    {
                        newBoneWeights.AddRange(lastboneWeights);
                        newBonesPerVertex.Add((byte)lastboneWeights.Length);
                    }
                    else
                    {
                        BoneWeight1 nullBW = new BoneWeight1();
                        nullBW.boneIndex = 0;
                        nullBW.weight = 1.0f;

                        newBoneWeights.Add(nullBW);
                        newBonesPerVertex.Add(1);
                    }
                }

                if (FoundBones.Count > 0)
                {
                    foreach(var kp in FoundBones)
                    {
                        Debug.Log($"FoundBone: {kp.Key}, {kp.Value} Hash {UMAUtils.StringToHash(kp.Value)}");
                    }
                }

                decalMesh.SetVertices(verts);
                DecalMeshFilter.mesh = decalMesh;
                decalMesh.RecalculateNormals();
                decalMesh.RecalculateTangents();
                decalMesh.RecalculateBounds();

                var smr = newDecal.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = decalMesh;
                smr.updateWhenOffscreen = true;

                SimpleDecal sd = newDecal.AddComponent<SimpleDecal>();

                SkinnedMeshRenderer characterRenderer = Character.GetComponentInChildren<SkinnedMeshRenderer>();
                UMAData cData = avatar.umaData;

                var boneHashNames = cData.skeleton.GetBoneHashNames();

                int[] hashes = new int[boneHashNames.Count];
                string[] names = new string[boneHashNames.Count];


                for(int i=0;i<boneHashNames.Count;i++)
                {
                    hashes[i] = boneHashNames[i].Key;
                    names[i] = boneHashNames[i].Value; 
                }
 
                sd.Configure(names,hashes, newBonesPerVertex.ToArray(), newBoneWeights.ToArray());
                SceneManager.MoveGameObjectToScene(newDecal, scene);

                DecalManager dm = Character.GetComponentInChildren<DecalManager>();

                DecalDefinition dd = new DecalDefinition();
                dd.bakedMesh = bakedMesh;

                dd.planesInWorldSpace = GetPlanesInWorldspace();
                dd.WorldImpactPoint = decalIndicator.gameObject.transform.position;
                dd.LocalImpactPoint = avatar.gameObject.transform.InverseTransformPoint(dd.WorldImpactPoint);

                int MaxIndex = 0;
                foreach(DecalDefinition cd in dm.Decals)
                {
                    if (cd.material.name == decalMaterial.name)
                    {
                        if (cd.InitialIndex > MaxIndex)
                        {
                            MaxIndex = cd.InitialIndex;
                        }
                    }
                }

                dd.InitialIndex = MaxIndex + 1;
                dd.Name = decalMaterial.name + "_" + dd.InitialIndex;
                
                dd.material = decalMaterial;
                dd.DecalMeshObject = newDecal;
                
                dm.Decals.Add(dd);
                
                // DecalMeshFilter.sharedMesh.SetVertices(verts);
                // get all vertexes in the circle,
                // get all triangles that face the camera.
                // build new triangle list for the submesh

                /*
                List<Vector3> verts = new List<Vector3>();
                List<int> decalVerts = new List<int>();
                m.GetVertices(verts);



                for(int i=0;i<verts.Count;i++)
                {
                    Vector3 meshvert = verts[i];
                    Vector3 Delta = meshvert - decalIndicator.transform.localPosition;

                    if (Delta.magnitude < decalIndicator.transform.localScale.x)
                    {
                        decalVerts.Add(i);
                    }
                }

                if (decalVerts.Count > 0)
                {
                    // find all the triangles for the new decal.
                    // 
                }
                // Save decal information needed for submesh. 
                // Add the submesh.
                */
            }
        }

        private Dictionary<int, int> CalculateVertxBoneWeightOffsets(NativeArray<byte> basebonesPerVertex, NativeArray<BoneWeight1> baseboneWeights)
        {
            Dictionary<int, int> offsets = new Dictionary<int, int>();

            int offset = 0; 
            for(int i=0;i<basebonesPerVertex.Length;i++)
            {
                offsets.Add(i, offset);
                offset += basebonesPerVertex[i]; 
            }
            return offsets;
        }

        public void Cleanup(InteractiveUMAWindow scene)
        {

        }

        public void OnSceneGUI(InteractiveUMAWindow sceneView)
        {
            ProcessEvents(sceneView);

            const float WindowHeight = 140;
            const float WindowWidth = 380;
            const float Margin = 20;
            Handles.BeginGUI();
            if (ShowSlotDialog)
            {
                Rect ScrBox = new Rect(0, 0, sceneView.position.width, sceneView.position.height);
                // EditorGUI.DrawPreviewTexture(ScrBox,blockerTex, ScreenBlocker,ScaleMode.StretchToFill);
                GUI.DrawTexture(ScrBox, blockerTex, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, 0.8f), 0, 0);
                //GUI.Box(ScrBox, blockerTex);
                GUI.Window(3, new Rect((sceneView.position.width/2) - 250, (sceneView.position.height/2) - 150, 500, 200), SlotDialog, "");
            }
            else
            {
                if (showHelp)
                {
                    // GUI.Window(2, new Rect(10, 20, 280, 104), HelpWindow, "");
                    GUI.Window(2, new Rect(10, 20, 300, 300), DecalList, "");
                }
                GUI.Window(1, new Rect(sceneView.position.width - (WindowWidth + Margin), sceneView.position.height - (WindowHeight + Margin), WindowWidth, WindowHeight), SceneWindow, "Interactive Decals");
            }
            Handles.EndGUI();
        }


        BoneWeight1[] lastboneWeights = null;

        private bool logbones = false;
        private Dictionary<int, string> FoundBones = new Dictionary<int, string>();

        private Vector3 LocalRayHit(PhysicsScene physcene, Vector3 vert, float offset, Mesh theMesh,
            Dictionary<int, int> VertexBoneWeightOffset,
            NativeArray<byte> basebonesPerVertex,
            NativeArray<BoneWeight1> baseboneWeights)
        {
            lastboneWeights = null;
            // get world position of hit.
            // translate to local coordinates of character.
            // 
            Ray ray = new Ray(vert, decalIndicator.Ray.direction);
            RaycastHit hit;


            Debug.DrawRay(vert, decalIndicator.Ray.direction, Color.green,30.0f);
//            Rays.Add(ray);

            if (!physcene.Raycast(ray.origin, ray.direction, out hit, 512.0f, LayerMask.GetMask("Player")))
            {
                Debug.Log("Ray did not hit");
                return vert;
            }

            if (hit.triangleIndex < 0)
            {
                return vert;
            }

            Vector3 hitpoint = hit.point + ((decalIndicator.Ray.direction.normalized * -1) * offset);

            Vector3 LocalHitpoint = hit.transform.InverseTransformPoint(hitpoint);


          //  Transform umaTransform = GetUMATransform(hit.transform);



            for (int i = 0; i < theMesh.subMeshCount; i++)
            {
                var smd = theMesh.GetSubMesh(i);
                if (hit.triangleIndex < smd.indexStart)
                {
                    continue;
                }

                if (hit.triangleIndex >= (smd.indexStart + (smd.indexCount/3)))
                {
                    continue;
                }

                // should fall through for only ONE submesh.
                int[] tris = theMesh.GetTriangles(i);
                int submishtricount = smd.indexCount / 3;
                int baseIndexStart = hit.triangleIndex - smd.indexStart;
                int tribase = 3 * (hit.triangleIndex - smd.indexStart);

                try
                {

                    int v1x = tris[tribase];
                    int v2x = tris[tribase + 1];
                    int v3x = tris[tribase + 2];

                    Vector3 v1 = theMesh.vertices[v1x];
                    Vector3 v2 = theMesh.vertices[v2x];
                    Vector3 v3 = theMesh.vertices[v3x];


#if LOCAL_SPACE
                // lastboneWeights = GetBoneWeights(LocalHitpoint, v1, v2, v3, v1x, v2x, v3x, VertexBoneWeightOffset, basebonesPerVertex, baseboneWeights);
#else
                    Matrix4x4 theMat = Character.transform.localToWorldMatrix;

                    v1 = theMat * v1;
                    v2 = theMat * v2;
                    v3 = theMat * v3;
                    lastboneWeights = GetBoneWeights(hitpoint, v1, v2, v3, v1x, v2x, v3x, VertexBoneWeightOffset, basebonesPerVertex, baseboneWeights);
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            return LocalHitpoint;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Hit"></param>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <param name="v3"></param>
        /// <param name="v1x"></param>
        /// <param name="v2x"></param>
        /// <param name="v3x"></param>
        /// <param name="VertexBoneWeightOffset"> this is a dictionary calculated from the following two. It translates a vertex to the starting index by adding up all the BonesPerVertex of preceding verts</param>
        /// <param name="basebonesPerVertex"></param>
        /// <param name="baseboneWeights"></param>
        /// <returns></returns>
        private BoneWeight1[] GetBoneWeights(Vector3 Hit, Vector3 v1, Vector3 v2, Vector3 v3, 
            int v1x, int v2x, int v3x, 
            Dictionary<int,int> VertexBoneWeightOffset,  
            NativeArray<byte> basebonesPerVertex, 
            NativeArray<BoneWeight1> baseboneWeights)
        {
            Dictionary<int, float> boneDictionary = new Dictionary<int, float>();
            List<BoneWeight1> boneWeights = new List<BoneWeight1>();

            float v1len = Mathf.Abs((Hit - v1).magnitude);
            float v2len = Mathf.Abs((Hit - v2).magnitude);
            float v3len = Mathf.Abs((Hit - v3).magnitude);

            float totalDistance = v1len + v2len + v3len;

            float v1Perc = v1len / totalDistance;
            float v2Perc = v2len / totalDistance;
            float v3Perc = v3len / totalDistance;

            AddBoneWeights(boneDictionary, v1Perc, v1x, VertexBoneWeightOffset, basebonesPerVertex, baseboneWeights);
            AddBoneWeights(boneDictionary, v2Perc, v2x, VertexBoneWeightOffset, basebonesPerVertex, baseboneWeights);
            AddBoneWeights(boneDictionary, v3Perc, v3x, VertexBoneWeightOffset, basebonesPerVertex, baseboneWeights);

            foreach(var kp in boneDictionary)
            {
                BoneWeight1 b = new BoneWeight1();
                b.boneIndex = kp.Key;
                b.weight = kp.Value;
                boneWeights.Add(b);
            }
            boneWeights.Sort((a, b) => (0-(a.weight.CompareTo(b.weight))));
            return boneWeights.ToArray();
        }

        private void AddBoneWeights(Dictionary<int, float> boneDictionary,float percentage, int vertexnumber, 
            Dictionary<int, int> VertexBoneWeightOffset, 
            NativeArray<byte> basebonesPerVertex, 
            NativeArray<BoneWeight1> baseboneWeights)
        {
            int boneStart = VertexBoneWeightOffset[vertexnumber];
            int numBones = basebonesPerVertex[vertexnumber];
            for(int i = boneStart; i < (boneStart + numBones); i++)
            {
                BoneWeight1 b = baseboneWeights[i];
#if _INDEX
                if (!boneDictionary.ContainsKey(b.boneIndex)) boneDictionary.Add(b.boneIndex, 0.0f);
                boneDictionary[b.boneIndex] += b.weight * percentage;
#else
                int boneNum = b.boneIndex;
                int boneHash = UMAUtils.StringToHash(boneNames[boneNum]);

                if (!boneDictionary.ContainsKey(boneHash))
                {
                    boneDictionary.Add(boneHash, 0.0f);
                }

                boneDictionary[boneHash] += b.weight * percentage;

#endif
                if (logbones)
                {
                    if (FoundBones.ContainsKey(b.boneIndex) == false)
                    {
                        FoundBones.Add(b.boneIndex, boneNames[b.boneIndex]);
                    }
                }
            }
            //logbones = false;
        }

        private void ProcessEvents(InteractiveUMAWindow sceneView)
        {
            if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                FreezeCurrentMesh(sceneView);
                Ray ray = sceneView.GUIPointToWorldRay(Event.current.mousePosition);
    
                RaycastHit hit;
                PhysicsScene physcene = PhysicsSceneExtensions.GetPhysicsScene(sceneView.CurrentScene);
                if (!physcene.Raycast(ray.origin, ray.direction, out hit, 512.0f, LayerMask.GetMask("Player")))
                {
                    return;
                }

                if (decalIndicator != null)
                {
                    if ((hit.transform.gameObject == sceneView.avatarGo) || (hit.transform.parent == sceneView.avatarGo.transform))
                    {
                        // Event.current.Use();
                        // Indicator.transform.position = hit.point;
                        decalIndicator.gameObject.transform.position = hit.point;
                        decalIndicator.Ray = ray;
                        decalIndicator.gameObject.transform.forward = ray.direction;
                        decalIndicator.LocalEuler = decalIndicator.gameObject.transform.localEulerAngles;
                    }
                }
            }
        }

        private Mesh FreezeCurrentMesh(InteractiveUMAWindow sceneView)
        {
            SkinnedMeshRenderer smr = sceneView.avatarGo.GetComponentInChildren<SkinnedMeshRenderer>();

            boneNames.Clear();
            var t = smr.bones;
            for (int i=0;i<t.Length;i++)
            {
                boneNames.Add(t[i].name);
            }
            MeshCollider mc = sceneView.avatarGo.GetComponentInChildren<MeshCollider>();
            if (smr != null)
            {
                Mesh mesh = new Mesh();
                smr.BakeMesh(mesh);

                Physics.BakeMesh(mesh.GetInstanceID(), false);
                mc.sharedMesh = mesh;
                Physics.SyncTransforms();
                return mesh;
            }
            return null;
        }

        public void ShowHelp(bool isShown)
        {
            showHelp = isShown;
        }

        public Material FindBlockerMaterial(GameObject root)
        {
            MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>();

            foreach (MeshRenderer r in mrs)
            {
                if (r.gameObject.name == "screenBlocker")
                {
                    return r.sharedMaterial;
                } 
            }
            return null;
        }

        public void InitializationComplete(GameObject root)
        {
            sceneRoot = root;
            float scale = EditorPrefs.GetFloat("DecalIndicatorScale", 1.0f);
            decalIndicator = root.GetComponentInChildren<DecalIndicator>();

            this.ScreenBlocker = FindBlockerMaterial(root);
            string[] texs = ScreenBlocker.GetTexturePropertyNames();
            foreach(string s in texs)
            {
                Texture tex = ScreenBlocker.GetTexture(s);
                if (tex != null)
                {
                    blockerTex = tex;
                }
            }

            foreach (Transform t in root.transform)
            {
                if (t.name == "Sphere")
                {
                    PlanesMarker = t.gameObject;
                }

                if (t.name == "VertexMarker")
                {
                    VertexMarker = t.gameObject;
                }
            }

            if (decalIndicator != null)
            {
                decalIndicator.transform.localScale = new Vector3(scale, scale, scale);
            }
            avatar = root.GetComponentInChildren<DynamicCharacterAvatar>();
            Character = avatar.gameObject;
            decalManager = root.GetComponentInChildren<DecalManager>();

            var races = UMAAssetIndexer.Instance.GetAllAssets<RaceData>();


            foreach(RaceData r in races)
            {
                if (r.raceName == avatar.activeRace.name)
                {
                    raceNumber = RaceNames.Count;
                }
                RaceNames.Add(r.raceName);
            }
            
            var mr = decalIndicator.visualPlane.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                decalMaterial = mr.sharedMaterial;
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint.Editor
{
    // Store shader parameters, never generated texture references. Those are rebound from
    // the layer stack when a document opens. A shader mismatch must not apply stale settings.
    [Serializable]
    internal sealed class TexturePaintPreviewMaterialState
    {
        public string surfaceId, shaderName;
        public string[] keywords;
        public List<Value> values = new List<Value>();
        [Serializable] internal sealed class Value
        {
            public string name;
            public ShaderPropertyType type;
            public Vector4 vector;
            public float scalar;
            public int integer;
        }

        internal static TexturePaintPreviewMaterialState Capture(Material material, string surfaceId)
        {
            var state = new TexturePaintPreviewMaterialState
                { surfaceId = surfaceId, shaderName = material.shader.name, keywords = material.shaderKeywords };
            Shader shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                ShaderPropertyType type = shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Texture) continue;
                var value = new Value { name = shader.GetPropertyName(i), type = type };
                switch (type)
                {
                    case ShaderPropertyType.Color: value.vector = material.GetColor(value.name); break;
                    case ShaderPropertyType.Vector: value.vector = material.GetVector(value.name); break;
                    case ShaderPropertyType.Int: value.integer = material.GetInteger(value.name); break;
                    default: value.scalar = material.GetFloat(value.name); break;
                }
                state.values.Add(value);
            }
            return state;
        }

        internal bool Apply(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != shaderName) return false;
            foreach (Value value in values)
            {
                int index = material.shader.FindPropertyIndex(value.name);
                if (index < 0 || material.shader.GetPropertyType(index) != value.type) continue;
                switch (value.type)
                {
                    case ShaderPropertyType.Color: material.SetColor(value.name, value.vector); break;
                    case ShaderPropertyType.Vector: material.SetVector(value.name, value.vector); break;
                    case ShaderPropertyType.Int: material.SetInteger(value.name, value.integer); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: material.SetFloat(value.name, value.scalar); break;
                }
            }
            material.shaderKeywords = keywords ?? Array.Empty<string>();
            return true;
        }
    }

    [Serializable] internal sealed class TexturePaintPreviewMaterialStates
    {
        public List<TexturePaintPreviewMaterialState> materials = new List<TexturePaintPreviewMaterialState>();
    }

    public sealed partial class TexturePaintStageWindow
    {
        [NonSerialized] private TexturePaintPreviewMaterialStates previewMaterialStates = new TexturePaintPreviewMaterialStates();
        internal TextureSet MaterialWindowTarget => ActiveTextureSet;

        internal void PreviewMaterialChanged(TextureSet set)
        {
            if (set?.previewMaterial == null) return;
            previewMaterialStates ??= new TexturePaintPreviewMaterialStates();
            previewMaterialStates.materials.RemoveAll(s => s.surfaceId == set.persistentId);
            previewMaterialStates.materials.Add(TexturePaintPreviewMaterialState.Capture(set.previewMaterial, set.persistentId));
            // Shader controls do not change texture pixels or generator inputs.
            SetDocumentDirtyFlags();
            RepaintAll();
        }

        private string CapturePreviewMaterialState()
        {
            if (previewMaterialStates == null) return null;
            foreach (TextureSet set in controller.Textures.Sets)
            {
                int index = previewMaterialStates.materials.FindIndex(s => s.surfaceId == set.persistentId);
                if (index >= 0 && set.previewMaterial != null)
                    previewMaterialStates.materials[index] = TexturePaintPreviewMaterialState.Capture(set.previewMaterial, set.persistentId);
            }
            return JsonUtility.ToJson(previewMaterialStates);
        }

        private void RestorePreviewMaterialState(string json)
        {
            // Loading another document in the same stage must not inherit its predecessor's overrides.
            if (previewMaterialStates != null)
                foreach (TextureSet set in controller.Textures.Sets)
                    if (set.surface?.sourceMaterial != null && set.previewMaterial != null &&
                        previewMaterialStates.materials.Exists(s => s.surfaceId == set.persistentId))
                    {
                        TexturePaintPreviewMaterialState.Capture(set.surface.sourceMaterial, set.persistentId).Apply(set.previewMaterial);
                        set.BindPreviewTextures(false);
                    }
            previewMaterialStates = string.IsNullOrEmpty(json) ? new TexturePaintPreviewMaterialStates() :
                JsonUtility.FromJson<TexturePaintPreviewMaterialStates>(json) ?? new TexturePaintPreviewMaterialStates();
            foreach (TextureSet set in controller.Textures.Sets)
            {
                var settings = previewMaterialStates.materials.Find(s => s.surfaceId == set.persistentId);
                if (settings?.Apply(set.previewMaterial) == true) set.BindPreviewTextures(false);
            }
        }
    }

    public sealed class TexturePaintMaterialWindow : EditorWindow
    {
        private static readonly HashSet<TexturePaintMaterialWindow> OpenWindows = new HashSet<TexturePaintMaterialWindow>();
        [SerializeField] private Vector2 scroll;
        [SerializeField] private bool showTextures;
        private MaterialEditor materialEditor;
        private Material boundMaterial;
        private TextureSet boundSet;
        private string lastState;
        private bool refreshPreview;

        [MenuItem("Window/UMA/Overlay Painter Material", false, 2024)]
        public static void ShowWindow()
        {
            var window = GetWindow<TexturePaintMaterialWindow>("Overlay Painter Material", true,
                typeof(TexturePaintPropertiesWindow), typeof(TexturePaintBrushWindow), typeof(TexturePaintDockWindow));
            window.Show(); window.Focus();
        }

        internal static void RepaintOpenWindows()
        { foreach (var window in OpenWindows) if (window != null) { window.refreshPreview = true; window.Repaint(); } }

        internal static void CloseOpenWindowsForLayoutChange()
        { foreach (var window in OpenWindows.ToArray()) if (window != null) window.Close(); }

        private void OnEnable()
        {
            OpenWindows.Add(this);
            titleContent = new GUIContent("Overlay Painter Material", EditorGUIUtility.IconContent("Material Icon").image);
            minSize = new Vector2(300, 350);
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            OpenWindows.Remove(this);
            Undo.undoRedoPerformed -= OnUndoRedo;
            ReleaseEditor();
        }

        private void ReleaseEditor()
        {
            if (materialEditor != null) DestroyImmediate(materialEditor);
            materialEditor = null; boundMaterial = null; boundSet = null; lastState = null;
        }

        private void OnInspectorUpdate()
        {
            // Active targets can change outside this window; repaint without recomposing textures.
            if (TexturePaintStageWindow.ActiveStage?.MaterialWindowTarget != boundSet) Repaint();
        }

        private void OnUndoRedo()
        {
            var stage = TexturePaintStageWindow.ActiveStage;
            if (boundMaterial != null && stage?.MaterialWindowTarget == boundSet &&
                JsonUtility.ToJson(TexturePaintPreviewMaterialState.Capture(boundMaterial, boundSet.persistentId)) != lastState)
                NotifyChanged(stage);
            Repaint();
        }

        private void NotifyChanged(TexturePaintStageWindow stage)
        {
            materialEditor.PropertiesChanged();
            stage.PreviewMaterialChanged(boundSet);
            lastState = JsonUtility.ToJson(TexturePaintPreviewMaterialState.Capture(boundMaterial, boundSet.persistentId));
        }

        private void OnGUI()
        {
            var stage = TexturePaintStageWindow.ActiveStage;
            TextureSet set = stage?.MaterialWindowTarget;
            Material material = set?.previewMaterial;
            if (material == null)
            {
                ReleaseEditor();
                EditorGUILayout.HelpBox("Open Overlay Painter and select a paint target to edit its preview material.", MessageType.Info);
                return;
            }
            if (material != boundMaterial)
            {
                ReleaseEditor(); boundSet = set; boundMaterial = material;
                materialEditor = (MaterialEditor)UnityEditor.Editor.CreateEditor(material);
                lastState = JsonUtility.ToJson(TexturePaintPreviewMaterialState.Capture(material, set.persistentId));
            }
            EditorGUILayout.LabelField(set.surface?.slotName ?? set.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(material.shader.name, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show on Character")) stage.SetScenePreviewMode(TexturePaintScenePreviewMode.Material);
                using (new EditorGUI.DisabledScope(set.surface?.sourceMaterial == null || stage.IsPersistenceActive))
                    if (GUILayout.Button("Reset to Source"))
                    {
                        Undo.RecordObject(material, "Reset Preview Material");
                        TexturePaintPreviewMaterialState.Capture(set.surface.sourceMaterial, set.persistentId).Apply(material);
                        set.BindPreviewTextures(false);
                        NotifyChanged(stage);
                    }
            }
            float height = Mathf.Clamp(position.height * .32f, 120, 280);
            Rect previewRect = GUILayoutUtility.GetRect(80, height, GUILayout.ExpandWidth(true));
            if (refreshPreview) { materialEditor.PropertiesChanged(); refreshPreview = false; }
            materialEditor.OnInteractivePreviewGUI(previewRect, EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) materialEditor.OnPreviewSettings();
            EditorGUILayout.HelpBox("Preview shader settings save with this painter project. Texture export uses the original UMA material. Edit texture maps in the layer channels.", MessageType.None);
            using (var scrolling = new EditorGUILayout.ScrollViewScope(scroll))
            using (new EditorGUI.DisabledScope(stage.IsPersistenceActive))
            {
                scroll = scrolling.scrollPosition;
                MaterialProperty[] properties = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
                EditorGUI.BeginChangeCheck();
                foreach (MaterialProperty property in properties)
                    if ((property.propertyFlags & (ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData)) == 0 &&
                        property.propertyType != ShaderPropertyType.Texture)
                        materialEditor.ShaderProperty(property, property.displayName);
                if (EditorGUI.EndChangeCheck()) NotifyChanged(stage);
                showTextures = EditorGUILayout.Foldout(showTextures, "Layer Textures", true);
                if (showTextures)
                    using (new EditorGUI.DisabledScope(true))
                        foreach (MaterialProperty property in properties)
                            if (property.propertyType == ShaderPropertyType.Texture &&
                                (property.propertyFlags & ShaderPropertyFlags.HideInInspector) == 0)
                                EditorGUILayout.ObjectField(property.displayName, property.textureValue, typeof(Texture), false);
            }
        }
    }
}

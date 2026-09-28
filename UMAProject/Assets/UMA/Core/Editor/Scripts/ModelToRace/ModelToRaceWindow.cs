using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Editors.ModelToRace
{
    public sealed class ModelToRaceWindow : EditorWindow
    {
        private const string Files = "Assets/UMA/Core/Editor/Scripts/ModelToRace/";
        private static readonly string[] Steps = { "1  Source", "2  Parts & wardrobe", "3  DNA & shapes", "4  Review & create" };
        [SerializeField] private ModelToRacePlan plan = new ModelToRacePlan();
        [SerializeField] private int page;
        [SerializeField] private string completedFolder;
        private ScrollView content;
        private string search = "";

        [MenuItem("UMA/Model to Race & Clothing...", priority = 21)]
        public static void Open() => GetWindow<ModelToRaceWindow>("Model to UMA");

        private const string ProjectMenu = "Assets/UMA/Model To Race/Clothing...";

        [MenuItem(ProjectMenu, false, 2000)]
        private static void OpenSelectedModel()
        {
            var model = SelectedProjectModel();
            if (model == null) return;
            var window = GetWindow<ModelToRaceWindow>("Model to UMA");
            window.plan = new ModelToRacePlan { source = model };
            window.plan.Scan();
            window.page = 0;
            window.search = "";
            window.completedFolder = null;
            window.Render();
            window.Show();
            window.Focus();
        }

        [MenuItem(ProjectMenu, true)]
        private static bool ValidateSelectedModel() => SelectedProjectModel() != null;

        private static GameObject SelectedProjectModel()
        {
            var selected = Selection.activeObject;
            if (Selection.objects.Length != 1 || !(selected is GameObject || selected is Mesh) ||
                !AssetDatabase.Contains(selected)) return null;
            // Resolve model children/submeshes to the asset root so the complete rig is scanned.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(selected));
            return model != null && model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null ? model : null;
        }

        public void CreateGUI()
        {
            minSize = new Vector2(620, 480);
            rootVisualElement.Clear();
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Files + "ModelToRaceWindow.uxml");
            if (template == null) { rootVisualElement.Add(new HelpBox("Model to UMA UI files are missing. Reimport the ModelToRace folder.", HelpBoxMessageType.Error)); return; }
            template.CloneTree(rootVisualElement);
            content = rootVisualElement.Q<ScrollView>("content");
            rootVisualElement.Q<Button>("help").clicked += () => UMAMarkdownViewer.Open("Assets/UMA/Docs/ModelToRace.md");
            rootVisualElement.Q<Button>("back").clicked += () => { page = Mathf.Max(0, page - 1); Render(); };
            rootVisualElement.Q<Button>("next").clicked += () => { if (page < 3) { page++; plan.RefreshDNA(); Render(); } else CreateAssets(); };
            Render();
        }

        private void Render()
        {
            if (content == null) return;
            content.Clear();
            var steps = rootVisualElement.Q("steps"); steps.Clear();
            for (int i = 0; i < Steps.Length; i++)
            {
                int p = i;
                var button = new Button(() => { page = p; plan.RefreshDNA(); Render(); }) { text = Steps[i] };
                button.EnableInClassList("current", i == page); steps.Add(button);
            }
            rootVisualElement.Q<Button>("back").SetEnabled(page > 0);
            rootVisualElement.Q<Button>("next").text = page < 3 ? "Next" : "Create assets";
            rootVisualElement.Q<Button>("next").SetEnabled(true);
            rootVisualElement.Q<Label>("status").text = string.IsNullOrEmpty(completedFolder) ? "Nothing is written until Create assets. Each import gets a new folder." : "Created: " + completedFolder;
            switch (page) { case 0: Source(); break; case 1: Parts(); break; case 2: DNA(); break; default: Review(); break; }
        }

        private VisualElement Section(string title)
        {
            var section = new VisualElement(); section.AddToClassList("section");
            var label = new Label(title); label.AddToClassList("section-title"); section.Add(label); content.Add(section); return section;
        }

        private static void Explain(VisualElement parent, string text) { var label = new Label(text); label.AddToClassList("description"); parent.Add(label); }
        private static void Text(VisualElement parent, string label, string value, Action<string> changed)
        { var field = new TextField(label) { value = value ?? "", isDelayed = true }; field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field); }
        private static void Toggle(VisualElement parent, string label, bool value, Action<bool> changed)
        { var field = new Toggle(label) { value = value }; field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field); }
        private static void ObjectField<T>(VisualElement parent, string label, T value, Action<T> changed, bool scene = false) where T : Object
        { var field = new ObjectField(label) { objectType = typeof(T), allowSceneObjects = scene, value = value }; field.RegisterValueChangedCallback(e => changed(e.newValue as T)); parent.Add(field); }

        private void Source()
        {
            var section = Section("Choose your source and destination");
            var mode = new EnumField("Workflow", plan.mode);
            mode.RegisterValueChangedCallback(e => { plan.mode = (ModelImportMode)e.newValue; plan.boneDNA = plan.IsNewRace; foreach (var part in plan.parts) if (part.role != ModelPartRole.Skip) part.role = plan.IsNewRace ? ModelPartRole.Body : ModelPartRole.Clothing; Render(); }); section.Add(mode);
            ObjectField<GameObject>(section, "Model root", plan.source, value => { plan.source = value; plan.Scan(); completedFolder = null; Render(); }, true);
            Explain(section, "Choose a prefab/FBX or a scene root containing the complete skeleton and skinned meshes, in its rest pose. Static meshes must first be skinned (Scene Mesh Slot Builder can transfer weights). LOD1+ renderers start skipped.");
            if (plan.source != null)
            {
                section.Add(new Button(() => { plan.Scan(); Render(); }) { text = "Rescan source (reset part choices)" });
                Explain(section, plan.parts.Count + " skinned material parts found. Assign each to Body, Clothing or Skip on the next page.");
                var preview = AssetPreview.GetAssetPreview(plan.source);
                if (preview != null) { var image = new Image { image = preview, scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("thumbnail"); section.Add(image); }
            }
            if (!plan.IsNewRace) ObjectField<RaceData>(section, "Target race", plan.targetRace, value => { plan.targetRace = value; Render(); });
            Text(section, plan.IsNewRace ? "New race name" : "Clothing collection name", plan.name, value => plan.name = ModelToRacePlan.SafeName(value));
            ObjectField<DefaultAsset>(section, "Output parent folder", AssetDatabase.LoadAssetAtPath<DefaultAsset>(plan.outputParent), value => { if (value != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(value))) plan.outputParent = AssetDatabase.GetAssetPath(value); });
            ObjectField<RuntimeAnimatorController>(section, "Animator controller (optional)", plan.animatorController, value => plan.animatorController = value);
            Toggle(section, "Register in UMA Global Library", plan.registerAssets, value => plan.registerAssets = value);
            Toggle(section, "Create avatar prefab", plan.createAvatarPrefab, value => plan.createAvatarPrefab = value);
            if (!plan.IsNewRace) Explain(section, "Existing-race clothing must already fit that race's rest skeleton, scale and bone names. This workflow does not auto-retarget or transfer weights. The existing race body is never replaced.");
        }

        private void Parts()
        {
            var intro = Section("Build a body and a wardrobe");
            Explain(intro, "Body parts form the base race recipe. Clothing pieces with the same outfit name become ONE wardrobe recipe (including all their material submeshes). Different outfits in the same region are alternatives. Every non-empty submesh gets a slot and overlay.");
            Explain(intro, "Materials are copied with their existing shaders/textures. No lossy shader conversion or texture rebake is performed. This preserves appearance but does not automatically atlas materials or hide the body under clothing; use UMA Material setup and Mesh Hide tools afterwards if needed.");
            if (plan.IsNewRace) Text(intro, "Wardrobe regions", string.Join(", ", plan.regions), value => { plan.regions = value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList(); Render(); });
            foreach (var part in plan.parts)
            {
                var section = Section(part.renderer != null ? part.renderer.name + (part.sourceSubmesh >= 0 ? " / submesh " + part.sourceSubmesh : " / all submeshes") : "Missing source — rescan");
                var role = new EnumField("Use as", part.role); role.RegisterValueChangedCallback(e => { part.role = (ModelPartRole)e.newValue; Render(); }); section.Add(role);
                if (part.renderer == null) continue;
                section.Add(new Button(() => EditorGUIUtility.PingObject(part.renderer)) { text = "Locate source part" });
                if (part.role == ModelPartRole.Skip) continue;
                Text(section, "Part name", part.name, value => part.name = ModelToRacePlan.SafeName(value));
                var mesh = part.renderer.sharedMesh;
                Explain(section, mesh == null ? "No mesh" : $"{mesh.vertexCount:N0} vertices · {mesh.subMeshCount} submeshes · {mesh.blendShapeCount} blendshapes");
                Explain(section, "Material: " + string.Join(", ", part.renderer.sharedMaterials.Where((m, i) => part.sourceSubmesh < 0 || part.sourceSubmesh == i).Select(m => m != null ? m.name : "MISSING")));
                if (part.role != ModelPartRole.Clothing) continue;
                Text(section, "Outfit name", part.outfit, value => part.outfit = ModelToRacePlan.SafeName(value));
                var choices = plan.RegionChoices.Where(s => s != "None").Distinct().ToList();
                if (!choices.Contains(part.region)) choices.Insert(0, "Choose a region");
                var region = new PopupField<string>("Wardrobe region", choices, Mathf.Max(0, choices.IndexOf(part.region)));
                region.RegisterValueChangedCallback(e => part.region = e.newValue); section.Add(region);
                Toggle(section, "Equip on generated prefab", part.equip, value => part.equip = value);
            }
        }

        private void DNA()
        {
            var intro = Section("Choose the controls your character needs");
            Toggle(intro, "Generate bone DNA", plan.boneDNA, value => { plan.boneDNA = value; Render(); });
            Toggle(intro, "Generate blendshape DNA", plan.blendshapeDNA, value => { plan.blendshapeDNA = value; Render(); });
            if (!plan.IsNewRace)
            {
                Toggle(intro, "Attach DNA to existing race", plan.attachDnaToExistingRace, value => plan.attachDnaToExistingRace = value);
                Explain(intro, "Off: DNA groups are created as separate assets for you to review/attach. On: adds those groups and selected blendshape names to the existing UMA 3 race; it does not replace existing DNA. Review duplicate controls before attaching additional clothing collections.");
            }
            Text(intro, "Filter names", search, value => { search = value; Render(); });
            bool Match(string name) => string.IsNullOrEmpty(search) || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            var shapes = Section($"Blendshapes — {plan.shapes.Count(s => s.include)} / {plan.shapes.Count} included");
            Explain(shapes, "All start included. Uncheck a shape to remove its frames from generated slots as well as omit its DNA. Shared names share one control; the first included renderer supplies the default. Range/default are source weights, not normalized slider values (usually 100 = full shape).");
            var buttons = new VisualElement(); buttons.AddToClassList("row"); shapes.Add(buttons);
            buttons.Add(new Button(() => { plan.shapes.ForEach(s => s.include = true); Render(); }) { text = "Include all" });
            buttons.Add(new Button(() => { plan.shapes.ForEach(s => s.include = false); Render(); }) { text = "Include none" });
            foreach (var shape in plan.shapes.Where(s => Match(s.name)))
            {
                var row = new VisualElement(); row.AddToClassList("section"); shapes.Add(row);
                Toggle(row, shape.name, shape.include, value => shape.include = value);
                var fold = new Foldout { text = "Weight range & default", value = false }; row.Add(fold);
                Number(fold, "Minimum", shape.minimum, value => shape.minimum = value);
                Number(fold, "Maximum", shape.maximum, value => shape.maximum = value);
                Number(fold, "Default weight", shape.defaultWeight, value => shape.defaultWeight = value);
            }
            var bones = Section("Bone DNA — local-axis scale");
            bones.SetEnabled(plan.boneDNA);
            Explain(bones, "Each checked bone gets a size control: 0.5 preserves the rest pose; 0 / 1 scales by 1 − range / 1 + range. Defaults are ±20% uniformly, so arbitrary bone orientations work. Children inherit scale. For anatomical length/width controls, edit the local X/Y/Z ranges or refine the generated DNA effects. This is a starting rig, not automatically authored facial expressions.");
            var all = new VisualElement(); all.AddToClassList("row"); bones.Add(all);
            all.Add(new Button(() => { plan.bones.ForEach(b => b.include = true); Render(); }) { text = "All bones" });
            all.Add(new Button(() => { plan.bones.ForEach(b => b.include = false); Render(); }) { text = "No bones" });
            foreach (var bone in plan.bones.Where(b => Match(b.bone)))
            {
                var row = new VisualElement(); row.AddToClassList("section"); bones.Add(row);
                Toggle(row, bone.bone, bone.include, value => bone.include = value);
                var field = new Vector3Field("Local scale range") { value = bone.scaleRange }; field.RegisterValueChangedCallback(e => bone.scaleRange = e.newValue); row.Add(field);
            }
        }

        private static void Number(VisualElement parent, string label, float value, Action<float> changed)
        { var field = new FloatField(label) { value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => changed(e.newValue)); parent.Add(field); }

        private void Review()
        {
            plan.RefreshDNA();
            var section = Section("Review before creating assets");
            Explain(section, "Destination: " + plan.outputParent + "/" + plan.name + " (a new numbered folder if already present)");
            Explain(section, plan.IsNewRace ? "Creates: RaceData, rest TPose, base recipe, selected DNA, slots, overlays, copied materials, clothing recipes and optional avatar prefab." : "Creates: clothing slots, overlays, copied materials, wardrobe recipes, selected DNA groups and optional avatar prefab. The existing body/base recipe is unchanged.");
            Explain(section, $"{plan.IncludedParts.Count(p => p.role == ModelPartRole.Body)} body parts; {plan.IncludedParts.Count(p => p.role == ModelPartRole.Clothing)} clothing parts; {plan.shapes.Count(s => s.include)} included blendshapes; {(plan.boneDNA ? plan.bones.Count(b => b.include) : 0)} bone controls.");
            foreach (var outfit in plan.IncludedParts.Where(p => p.role == ModelPartRole.Clothing).GroupBy(p => p.outfit)) Explain(section, outfit.Key + " → " + outfit.First().region + " (" + outfit.Count() + " parts)");
            if (plan.IncludedParts.Where(p => p.role == ModelPartRole.Clothing && p.equip).GroupBy(p => p.region).Any(g => g.Select(p => p.outfit).Distinct().Count() > 1))
                section.Add(new HelpBox("Several default outfits use the same region. Only the first is enabled on the generated prefab; all recipes are created.", HelpBoxMessageType.Warning));
            if (!plan.registerAssets) section.Add(new HelpBox("Registration is off. Register the generated race, slots, overlays and recipes in the Global Library before building an avatar.", HelpBoxMessageType.Warning));
            if (!plan.IsNewRace && plan.attachDnaToExistingRace) section.Add(new HelpBox("This also modifies the target race: adds generated DNA groups and included blendshape names.", HelpBoxMessageType.Warning));
            if (!plan.IsNewRace && !plan.attachDnaToExistingRace && plan.shapes.Any(s => s.include)) section.Add(new HelpBox("The target race may filter out these blendshapes. Enable Attach DNA, or add the included shape names to its unbaked blendshape list yourself.", HelpBoxMessageType.Warning));
            var errors = plan.Validate();
            foreach (var error in errors) section.Add(new HelpBox(error, HelpBoxMessageType.Error));
            rootVisualElement.Q<Button>("next").SetEnabled(errors.Count == 0 && !EditorApplication.isPlaying);
            if (errors.Count == 0) section.Add(new HelpBox("Ready. Source meshes, import settings, materials and prefabs will not be changed.", HelpBoxMessageType.Info));
            if (!string.IsNullOrEmpty(completedFolder)) section.Add(new Button(() => { Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(completedFolder); EditorGUIUtility.PingObject(Selection.activeObject); }) { text = "Show created assets" });
        }

        private void CreateAssets()
        {
            if (!plan.IsNewRace && plan.attachDnaToExistingRace && !EditorUtility.DisplayDialog("Modify target race?", "Add the generated DNA groups and selected blendshape names to " + plan.targetRace.raceName + "? Existing DNA and body slots will be preserved.", "Add DNA and create", "Cancel")) return;
            try
            {
                var result = ModelToRaceBuilder.Build(plan, (text, progress) => EditorUtility.DisplayProgressBar("Model to UMA", text, progress));
                completedFolder = result.folder;
                Selection.activeObject = result.avatarPrefab != null ? (Object)result.avatarPrefab : result.race;
                EditorGUIUtility.PingObject(Selection.activeObject);
                EditorUtility.DisplayDialog("Model to UMA complete", $"Created {result.slots.Count} slots, {result.overlays.Count} overlays, {result.wardrobe.Count} wardrobe recipes and {result.dnaGroups.Count} DNA groups.\n\n" + result.folder + "\n\nDrag the avatar prefab into a scene with an UMA context/generator. Test animation and the DNA sliders before publishing the race.", "OK");
            }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("Import stopped", e.Message + "\n\nThe new output folder was rolled back; existing source assets were not replaced.", "OK"); }
            finally { EditorUtility.ClearProgressBar(); Render(); }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UMA;
using UMA.Editors;
using UMA.Editors.PackageSupport;

[InitializeOnLoad]
public static class UMAPluginMigrationSmoke
{
    private const string Active = "UMA.PluginMigrationSmoke.Active";
    private const string Root = "Assets/UMA/OverlayPainter";
    private const string Data = "Assets/UMAProjectData/OverlayPainter/PluginMigrationSmoke.asset";
    private const string Settings = "Assets/UMAProjectData/OverlayPainter/Settings.asset";
    private static string Phase => Argument("-umaPluginPhase");
    private static UMAContentKind Kind => Phase == "InstallTests" ? UMAContentKind.OverlayPainterTests : UMAContentKind.OverlayPainter;
    private static string Argument(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : "";
    }
    static UMAPluginMigrationSmoke()
    {
        if (SessionState.GetBool(Active, false)) EditorApplication.update += Tick;
    }
    public static void Run()
    {
        try
        {
            if (AssetDatabase.IsValidFolder("Assets/RelocatedOverlayPainter") && !AssetDatabase.IsValidFolder(Root))
            {
                string restore = AssetDatabase.MoveAsset("Assets/RelocatedOverlayPainter", Root);
                Require(string.IsNullOrEmpty(restore), restore);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            Require(Application.unityVersion == Argument("-umaExpectedUnity"), "Unexpected Unity version.");
            var settings = AssetDatabase.LoadAssetAtPath<UMASettings>(UMAPathUtility.ResolveInstallAssetPath("InternalDataStore/InGame/Resources/UMASettings.asset"));
            Require(settings != null && settings.UMAVersion == Argument("-umaExpectedUma"), "Actual UMASettings version does not match the source checkout.");
            Require(UMAPluginApi.Version == 1, "Wrong plugin API version.");
            if (Phase == "Absent" || Phase == "Removed")
            {
                Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("UMA.TexturePaint", StringComparison.Ordinal)), "Painter assemblies remain loaded.");
                Require(!File.Exists(Root + "/Editor/UMA.TexturePaint.Editor.asmdef"), "Painter source remains installed.");
                var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
                var editor = Editor.CreateEditor(slot);
                Require(editor != null, "Core slot inspector cannot be created.");
                UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(slot);
                Require(typeof(UMAData.UMARecipe).GetField("texturePaintStageState").FieldType == typeof(string), "Opaque recipe state was removed.");
                if (Phase == "Removed")
                {
                    Require(File.Exists(Data) && File.Exists(Settings), "Removal deleted authored data or settings.");
                    Require(File.ReadAllText(Root + "/UserNotes.txt") == "keep my notes", "Removal deleted an unowned file.");
                    Require(File.ReadAllText(Root + "/README.md").Contains("LOCAL MODIFICATION"), "Removal deleted a modified owned file.");
                }
                Pass(); return;
            }
            if (Phase == "Installed" || Phase == "Reinstalled")
            {
                ValidateInstalled();
                if (Phase == "Installed")
                {
                    var type = Type.GetType("UMA.TexturePaint.TexturePaintDocument, UMA.TexturePaint.Runtime", true);
                    UMAPathUtility.EnsureAssetFolder("Assets/UMAProjectData/OverlayPainter");
                    if (!File.Exists(Data))
                    {
                        var document = ScriptableObject.CreateInstance(type);
                        AssetDatabase.CreateAsset(document, Data);
                    }
                    var settingsType = Type.GetType("UMA.TexturePaint.Editor.TexturePaintProjectSettings, UMA.TexturePaint.Editor", true);
                    settingsType.GetProperty("TexturePaintCompactView").GetValue(null);
                    File.WriteAllText("Library/plugin-data-guids.txt", AssetDatabase.AssetPathToGUID(Data) + "\n" + AssetDatabase.AssetPathToGUID(Settings));
                    File.WriteAllText(Root + "/UserNotes.txt", "keep my notes");
                    File.AppendAllText(Root + "/README.md", "\nLOCAL MODIFICATION\n");
                    AssetDatabase.Refresh();
                    CheckRelocation();
                }
                else
                {
                    string expected = File.ReadAllText("Library/plugin-data-guids.txt");
                    Require(expected == AssetDatabase.AssetPathToGUID(Data) + "\n" + AssetDatabase.AssetPathToGUID(Settings), "Reinstall changed user-data GUIDs.");
                    Require(AssetDatabase.LoadMainAssetAtPath(Data) != null, "Authored document did not recover after reinstall.");
                }
                Pass(); return;
            }
            if (Phase == "Remove")
            {
                ValidateInstalled();
                Require(File.ReadAllText(Root + "/UserNotes.txt") == "keep my notes", "Update deleted unowned files.");
                Require(!File.ReadAllText(Root + "/README.md").Contains("LOCAL MODIFICATION"), "Update did not replace the owned file.");
                File.AppendAllText(Root + "/README.md", "\nLOCAL MODIFICATION\n");
                AssetDatabase.ImportAsset(Root + "/README.md");
                Require(UMAPluginPackageRemoval.TryRemove(UMAContentKind.OverlayPainter, out string error), error);
                Pass(); return;
            }
            SessionState.SetBool(Active, true);
            SessionState.SetFloat(Active + ".Started", (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            var policy = Phase == "Install" ? UMAContentConflictPolicy.Abort : UMAContentConflictPolicy.BackupAndReplace;
            Require(UMAContentPackageInstaller.InstallFromFileForAutomation(Kind,
                Argument("-umaPluginArchive"), policy, out string importError), importError);
        }
        catch (Exception exception) { Fail(exception); }
    }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (UMAContentPackageInstaller.GetState(Kind) == UMAContentInstallationState.Installed)
            { ValidateInstalled(); Pass(); }
            else if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Started", 0) > 180)
                throw new TimeoutException("Plugin import did not complete.");
        }
        catch (Exception exception) { Fail(exception); }
    }
    private static void ValidateInstalled()
    {
        Require(UMAContentPackageInstaller.GetState(UMAContentKind.OverlayPainter) == UMAContentInstallationState.Installed, "Plugin is not installed.");
        Require(AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/StrokeRasterize.compute") != null, "Packaged compute shader is missing.");
        Require(Type.GetType("UMA.TexturePaint.Editor.TexturePaintStageWindow, UMA.TexturePaint.Editor") != null, "Painter editor did not compile.");
        var registry = typeof(UMAPluginGUI).Assembly.GetType("UMA.Editors.UMAPluginRegistry", true);
        var actions = (Array)registry.GetProperty("Actions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        int painter = 0;
        foreach (object action in actions)
        {
            var method = (MethodInfo)action.GetType().GetField("Method", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(action);
            if (method.DeclaringType.FullName == "UMA.TexturePaint.Editor.TexturePaintPluginActions") painter++;
        }
        Require(painter == 2, "Painter launcher discovery failed.");
    }
    private static void CheckRelocation()
    {
        const string moved = "Assets/RelocatedOverlayPainter";
        EditorApplication.LockReloadAssemblies();
        try
        {
            string error = AssetDatabase.MoveAsset(Root, moved); Require(string.IsNullOrEmpty(error), error);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var type = Type.GetType("UMA.TexturePaint.Editor.TexturePaintAssets, UMA.TexturePaint.Editor", true);
            Require((string)type.GetProperty("Root").GetValue(null) == moved, "Resources did not follow the installation anchor.");
        }
        finally
        {
            if (Directory.Exists(moved))
            {
                string error = AssetDatabase.MoveAsset(moved, Root);
                if (!string.IsNullOrEmpty(error)) Debug.LogError(error);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorApplication.UnlockReloadAssemblies();
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pass()
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        File.WriteAllText("Library/plugin-" + Phase + ".txt", "PASS " + Phase + "; Unity " + Application.unityVersion + "; " + UMASettings.GetSettings().UMAVersion);
        EditorApplication.Exit(0);
    }
    private static void Fail(Exception exception)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        Debug.LogException(exception); File.WriteAllText("Library/plugin-" + Phase + ".txt", exception.ToString());
        EditorApplication.Exit(1);
    }
}

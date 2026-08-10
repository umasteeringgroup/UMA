#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UMA.Editors
{
    public sealed class UMAReleaseAssetValidationWindow : EditorWindow
    {
        private const string WindowTitle = "UMA Release Asset Validation";
        private const float DetailHeight = 335f;
        private const float RowHeight = 22f;

        [SerializeField] private int selectedIssueIndex = -1;
        [SerializeField] private string searchText = string.Empty;
        [SerializeField] private Vector2 detailScroll;
        [SerializeField] private Vector2 gridScroll;

        private UMAReleaseValidationReport report;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;
        private bool waitingForReport;
        private DateTime reportWriteTimeBeforeRun;
        private TestRunnerApi testRunnerApi;

        [MenuItem("UMA/Testing/Release Asset Validation...", priority = 2004)]
        public static void OpenWindow()
        {
            UMAReleaseAssetValidationWindow window =
                GetWindow<UMAReleaseAssetValidationWindow>(WindowTitle);
            window.minSize = new Vector2(820f, 600f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += PollForReport;
            ReloadReport(false);
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollForReport;
        }

        private void OnGUI()
        {
            DrawDetailPane();
            DrawGridPane();
        }

        private void DrawDetailPane()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
                GUILayout.Height(DetailHeight)))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    if (GUILayout.Button("Reload Report", EditorStyles.toolbarButton,
                        GUILayout.Width(95f))) ReloadReport(true);
                    if (GUILayout.Button(waitingForReport ? "Validation Running..." :
                        "Run Validation", EditorStyles.toolbarButton, GUILayout.Width(120f)) &&
                        !waitingForReport) RunValidation();
                    if (GUILayout.Button("Reveal JSON", EditorStyles.toolbarButton,
                        GUILayout.Width(85f))) RevealReport();
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(report == null || waitingForReport))
                    {
                        if (GUILayout.Button(new GUIContent(
                            "Remove all Non-Applicable shader properties from all Materials",
                            "Clean every unique material that owns an issue in the current report. Each material is processed once."),
                            EditorStyles.toolbarButton, GUILayout.Width(360f)))
                            RunAllMaterialCleanup();
                        if (GUILayout.Button("Auto", EditorStyles.toolbarButton,
                            GUILayout.Width(70f))) RunAutoRepair();
                    }
                }

                if (!string.IsNullOrEmpty(statusMessage))
                    EditorGUILayout.HelpBox(statusMessage, statusType);

                if (report == null)
                {
                    EditorGUILayout.HelpBox(
                        "No validation report is available. Run UMA Release Tests / Asset " +
                        "Validation to generate " +
                        UMAReleaseValidationReport.ProjectRelativePath + ".",
                        MessageType.Info);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    string generated = ParseUtc(report.generatedUtc);
                    EditorGUILayout.LabelField(report.passed ? "PASS" : "ISSUES",
                        report.passed ? SuccessStyle() : ErrorStyle(), GUILayout.Width(70f));
                    EditorGUILayout.LabelField(report.issueCount + " issue" +
                        (report.issueCount == 1 ? string.Empty : "s") + " • " + generated);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Schema " + report.schemaVersion,
                        GUILayout.Width(75f));
                }

                UMAReleaseValidationIssueReport issue = SelectedIssue;
                if (issue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Select an issue in the grid below to inspect and repair it.",
                        MessageType.Info);
                    return;
                }

                bool canReserialize =
                    UMAReleaseValidationRepairUtility.CanReserializeStaleSource(
                        issue, out string reserializeDiagnosis);

                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                DrawDetailLine("Issue", issue.kind);
                DrawDetailLine("Scope", issue.scope);
                DrawDetailLine("Source Asset", issue.ownerAssetName);
                DrawDetailLine("Source Location", issue.ownerAssetPath);
                DrawDetailLine("Referenced Asset", EmptyAsMissing(issue.referencedAssetName));
                DrawDetailLine("Referenced Location",
                    EmptyAsMissing(issue.referencedAssetPath));
                if (!string.IsNullOrEmpty(issue.referencedAssetGuid))
                    DrawDetailLine("Referenced GUID", issue.referencedAssetGuid);
                if (!string.IsNullOrEmpty(issue.propertyPath))
                    DrawDetailLine(issue.kind == "Missing GUID reference"
                        ? "Serialized Field"
                        : "Property", issue.propertyPath);
                if (issue.sourceLine > 0)
                    DrawDetailLine("Serialized Source Line", issue.sourceLine.ToString());
                DrawDetailLine("Suggested", issue.suggestedAction);
                if (!string.IsNullOrEmpty(issue.detail))
                    DrawDetailLine("Detail", issue.detail);
                if (issue.kind == "Missing GUID reference" &&
                    !string.IsNullOrEmpty(reserializeDiagnosis))
                    EditorGUILayout.HelpBox(reserializeDiagnosis,
                        canReserialize ? MessageType.Info : MessageType.Warning);
                DrawReferrerSummary(issue);
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Highlight Source", GUILayout.Width(115f)))
                        PingPath(issue.ownerAssetPath);
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(
                        issue.referencedAssetPath)))
                    {
                        if (GUILayout.Button("Highlight Referenced", GUILayout.Width(140f)))
                            PingPath(issue.referencedAssetPath);
                    }
                    using (new EditorGUI.DisabledScope(!canReserialize || waitingForReport))
                    {
                        if (GUILayout.Button(new GUIContent(
                            "Reserialize Source Asset",
                            "Rewrite the source asset using its current serialized fields, removing stale YAML left by removed or renamed fields."),
                            GUILayout.Width(165f)))
                            ReserializeSelected(issue);
                    }
                    GUILayout.FlexibleSpace();
                    bool canCleanMaterial =
                        UMAReleaseValidationRepairUtility.TryBuildMaterialCleanupPlan(
                            issue.ownerAssetPath, out UMAReleaseMaterialCleanupPlan cleanupPlan);
                    using (new EditorGUI.DisabledScope(!canCleanMaterial || waitingForReport))
                    {
                        if (GUILayout.Button(new GUIContent(
                            "Remove All Non-Applicable Shader Properties",
                            "Remove saved material properties whose internal names are not declared by the material's current shader."),
                            GUILayout.Width(270f)))
                            RemoveSelectedMaterialProperties(cleanupPlan);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    bool canRelocate = UMAReleaseValidationRepairUtility.CanRelocate(issue);
                    using (new EditorGUI.DisabledScope(!canRelocate || waitingForReport))
                    {
                        if (GUILayout.Button("Move", GUILayout.Width(72f))) MoveSelected(false);
                        if (GUILayout.Button("Copy", GUILayout.Width(72f))) CopySelected();
                        if (GUILayout.Button("Universal", GUILayout.Width(84f))) MoveSelected(true);
                    }
                    using (new EditorGUI.DisabledScope(
                        !UMAReleaseValidationRepairUtility.CanDeleteSource(issue) ||
                        waitingForReport))
                    {
                        if (GUILayout.Button("Delete Source", GUILayout.Width(105f)))
                            DeleteSelectedSource();
                    }
                }
            }
        }

        private void DrawGridPane()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    EditorGUILayout.LabelField("Validation Items", EditorStyles.boldLabel,
                        GUILayout.Width(115f));
                    GUILayout.FlexibleSpace();
                    GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ??
                        EditorStyles.toolbarTextField;
                    searchText = GUILayout.TextField(searchText ?? string.Empty, searchStyle,
                        GUILayout.Width(260f));
                    if (!string.IsNullOrEmpty(searchText) && GUILayout.Button("×",
                        EditorStyles.toolbarButton, GUILayout.Width(22f)))
                        searchText = string.Empty;
                }

                DrawGridHeader();
                gridScroll = EditorGUILayout.BeginScrollView(gridScroll);
                if (report != null && report.issues != null)
                {
                    int visibleRow = 0;
                    for (int i = 0; i < report.issues.Count; i++)
                    {
                        UMAReleaseValidationIssueReport issue = report.issues[i];
                        if (!MatchesSearch(issue)) continue;
                        DrawGridRow(i, issue, visibleRow++);
                    }
                    if (visibleRow == 0)
                        EditorGUILayout.HelpBox("No issues match the current filter.",
                            MessageType.Info);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawGridHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.75f, 0.75f, 0.75f));
            DrawColumns(rect, "Scope", "Issue", "Source Asset", "Referenced Asset",
                "Field / Referenced Location", EditorStyles.boldLabel);
        }

        private void DrawGridRow(int issueIndex, UMAReleaseValidationIssueReport issue,
            int visibleRow)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
            if (issueIndex == selectedIssueIndex)
                EditorGUI.DrawRect(rect, SelectionColor());
            else if ((visibleRow & 1) != 0)
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.025f));

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                rect.Contains(current.mousePosition))
            {
                selectedIssueIndex = issueIndex;
                detailScroll = Vector2.zero;
                PingSelected(issue);
                current.Use();
                Repaint();
            }
            DrawColumns(rect, issue.scope, issue.kind, issue.ownerAssetName,
                EmptyAsMissing(issue.referencedAssetName),
                !string.IsNullOrEmpty(issue.propertyPath)
                    ? issue.propertyPath
                    : EmptyAsMissing(issue.referencedAssetPath), EditorStyles.label);
        }

        private static void DrawColumns(Rect rect, string scope, string issue,
            string source, string referenced, string location, GUIStyle style)
        {
            const float scopeWidth = 55f;
            const float issueWidth = 205f;
            const float sourceWidth = 175f;
            const float referenceWidth = 175f;
            const float gap = 5f;
            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y, scopeWidth, rect.height), scope ?? string.Empty, style);
            x += scopeWidth + gap;
            GUI.Label(new Rect(x, rect.y, issueWidth, rect.height), issue ?? string.Empty, style);
            x += issueWidth + gap;
            GUI.Label(new Rect(x, rect.y, sourceWidth, rect.height), source ?? string.Empty, style);
            x += sourceWidth + gap;
            GUI.Label(new Rect(x, rect.y, referenceWidth, rect.height),
                referenced ?? string.Empty, style);
            x += referenceWidth + gap;
            GUI.Label(new Rect(x, rect.y, Mathf.Max(10f, rect.xMax - x - 4f), rect.height),
                location ?? string.Empty, style);
        }

        private void DrawReferrerSummary(UMAReleaseValidationIssueReport issue)
        {
            if (report.references == null || string.IsNullOrEmpty(issue.referencedAssetPath))
                return;
            var referrers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < report.references.Count; i++)
            {
                UMAReleaseValidationReferenceReport reference = report.references[i];
                if (reference != null && string.Equals(reference.referencedAssetPath,
                    issue.referencedAssetPath, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(reference.sourceAssetPath))
                    referrers.Add(reference.sourceAssetPath);
            }
            if (referrers.Count == 0) return;
            var text = new StringBuilder();
            int count = 0;
            foreach (string referrer in referrers)
            {
                if (count++ > 0) text.Append("; ");
                text.Append(referrer);
                if (count == 5 && referrers.Count > count)
                {
                    text.Append("; +").Append(referrers.Count - count).Append(" more");
                    break;
                }
            }
            DrawDetailLine("Recorded Referrers", text.ToString());
        }

        private void MoveSelected(bool universal)
        {
            UMAReleaseValidationIssueReport issue = SelectedIssue;
            if (issue == null) return;
            UMAReleaseDestinationScope scope = universal
                ? UMAReleaseDestinationScope.Universal
                : UMAReleaseValidationRepairUtility.DestinationForIssue(issue);
            string destination = UMAReleaseValidationRepairUtility.GetProposedDestination(
                issue.referencedAssetPath, scope);
            string title = universal ? "Move Asset to Universal UMA" : "Move Referenced Asset";
            if (!EditorUtility.DisplayDialog(title,
                "Move the referenced asset?\n\nFrom: " + issue.referencedAssetPath +
                "\nTo: " + destination +
                "\n\nUnity will preserve its GUID, so all project references follow the move.",
                universal ? "Universal" : "Move", "Cancel")) return;
            HandleResult(UMAReleaseValidationRepairUtility.MoveReferencedAsset(issue, scope));
        }

        private void CopySelected()
        {
            UMAReleaseValidationIssueReport issue = SelectedIssue;
            if (issue == null) return;
            UMAReleaseDestinationScope scope =
                UMAReleaseValidationRepairUtility.DestinationForIssue(issue);
            string destination = UMAReleaseValidationRepairUtility.GetProposedDestination(
                issue.referencedAssetPath, scope);
            if (!EditorUtility.DisplayDialog("Copy and Retarget Asset",
                "Copy the referenced asset and update recorded " + scope +
                " referrers?\n\nFrom: " + issue.referencedAssetPath +
                "\nTo: " + destination +
                "\n\nThe original asset will remain unchanged.", "Copy", "Cancel")) return;
            HandleResult(UMAReleaseValidationRepairUtility.CopyAndRetarget(report, issue));
        }

        private void DeleteSelectedSource()
        {
            UMAReleaseValidationIssueReport issue = SelectedIssue;
            if (issue == null) return;
            if (!EditorUtility.DisplayDialog("Delete Source Asset",
                "Permanently delete the source/owning asset?\n\n" + issue.ownerAssetPath +
                "\n\nThis does not delete the referenced asset. Unity will move the source " +
                "asset to the recycle bin, and other assets may be left with missing references.",
                "Delete Source", "Cancel")) return;
            HandleResult(UMAReleaseValidationRepairUtility.DeleteSourceAsset(issue));
        }

        private void ReserializeSelected(UMAReleaseValidationIssueReport issue)
        {
            if (issue == null) return;
            if (!EditorUtility.DisplayDialog(
                "Reserialize Source Asset",
                "Force Unity to rewrite this asset using only fields known by its current " +
                "serialized type?\n\nSource: " + issue.ownerAssetPath +
                "\nSerialized field: " + issue.propertyPath +
                "\nMissing GUID: " + issue.referencedAssetGuid +
                "\n\nThis is intended for stale YAML left by a removed or renamed field. " +
                "The YAML rewrite cannot be registered with Unity Undo; review or restore it " +
                "through source control if necessary.",
                "Reserialize",
                "Cancel")) return;

            HandleResult(
                UMAReleaseValidationRepairUtility.ReserializeStaleSource(issue));
        }

        private void RemoveSelectedMaterialProperties(
            UMAReleaseMaterialCleanupPlan cleanupPlan)
        {
            UMAReleaseValidationIssueReport issue = SelectedIssue;
            if (issue == null || cleanupPlan == null) return;
            var preview = new StringBuilder();
            preview.Append("Remove ").Append(cleanupPlan.PropertyCount)
                .Append(" saved material ")
                .Append(cleanupPlan.PropertyCount == 1 ? "property" : "properties")
                .Append(" that are not declared by the current shader?\n\nMaterial: ")
                .Append(cleanupPlan.materialPath).Append("\nShader: ")
                .Append(cleanupPlan.shaderName).Append("\n\n");
            int shown = Mathf.Min(cleanupPlan.PropertyCount, 12);
            for (int i = 0; i < shown; i++)
                preview.Append("• ").Append(cleanupPlan.propertyEntries[i]).Append('\n');
            if (shown < cleanupPlan.PropertyCount)
                preview.Append("…and ").Append(cleanupPlan.PropertyCount - shown)
                    .Append(" more.\n");
            preview.Append("\nProperties declared by the current shader are always preserved.");
            if (!EditorUtility.DisplayDialog("Remove Non-Applicable Shader Properties",
                preview.ToString(), "Remove Properties", "Cancel")) return;
            HandleResult(
                UMAReleaseValidationRepairUtility.RemoveNonApplicableShaderProperties(issue));
        }

        private void RunAutoRepair()
        {
            List<UMAReleaseMaterialCleanupPlan> materialPlans =
                UMAReleaseValidationRepairUtility.BuildAutoMaterialCleanupPlan(report);
            List<UMAReleaseAutoMovePlan> plans =
                UMAReleaseValidationRepairUtility.BuildAutoMovePlan(report);
            if (plans.Count == 0 && materialPlans.Count == 0)
            {
                EditorUtility.DisplayDialog("Automatic Repair",
                    "No non-applicable material properties or unambiguous asset moves were found. " +
                    "Assets referenced from both UMA2 and UMA3, or from an unknown location, were left unchanged.",
                    "OK");
                return;
            }
            var preview = new StringBuilder();
            preview.Append("Automatic repair will:\n\n• Clean ")
                .Append(materialPlans.Count).Append(" material")
                .Append(materialPlans.Count == 1 ? string.Empty : "s").Append(" (")
                .Append(CountMaterialProperties(materialPlans))
                .Append(" non-applicable shader properties)\n• Move ")
                .Append(plans.Count).Append(" unambiguous asset")
                .Append(plans.Count == 1 ? string.Empty : "s")
                .Append("\n\nAuto never copies or deletes assets. Shader-supported material properties are preserved.\n\n");
            int shownMaterials = Mathf.Min(materialPlans.Count, 6);
            for (int i = 0; i < shownMaterials; i++)
                preview.Append("• Clean ").Append(materialPlans[i].materialPath).Append(" (")
                    .Append(materialPlans[i].PropertyCount).Append(")\n");
            if (shownMaterials < materialPlans.Count)
                preview.Append("…and ").Append(materialPlans.Count - shownMaterials)
                    .Append(" more materials.\n");
            int shown = Mathf.Min(plans.Count, 12);
            for (int i = 0; i < shown; i++)
                preview.Append("• ").Append(plans[i].sourcePath).Append(" → ")
                    .Append(plans[i].destinationFolder).Append('\n');
            if (shown < plans.Count)
                preview.Append("…and ").Append(plans.Count - shown).Append(" more.");
            if (!EditorUtility.DisplayDialog("Automatic Repair", preview.ToString(),
                "Auto", "Cancel")) return;
            HandleResult(UMAReleaseValidationRepairUtility.ExecuteAutoRepair(
                materialPlans, plans), false);
        }

        private void RunAllMaterialCleanup()
        {
            List<UMAReleaseMaterialCleanupPlan> materialPlans =
                UMAReleaseValidationRepairUtility.BuildAutoMaterialCleanupPlan(report);
            if (materialPlans.Count == 0)
            {
                EditorUtility.DisplayDialog("Remove Non-Applicable Shader Properties",
                    "None of the materials that own issues in the current report contain non-applicable properties that can be removed safely.",
                    "OK");
                return;
            }

            var preview = new StringBuilder();
            preview.Append("Remove ").Append(CountMaterialProperties(materialPlans))
                .Append(" non-applicable shader properties from ")
                .Append(materialPlans.Count).Append(" unique material")
                .Append(materialPlans.Count == 1 ? string.Empty : "s")
                .Append("?\n\nEach material will be processed once, saved, and registered with Unity Undo. Properties declared by the current shader are always preserved.\n\n");
            int shown = Mathf.Min(materialPlans.Count, 12);
            for (int i = 0; i < shown; i++)
                preview.Append("• ").Append(materialPlans[i].materialPath).Append(" (")
                    .Append(materialPlans[i].PropertyCount).Append(")\n");
            if (shown < materialPlans.Count)
                preview.Append("…and ").Append(materialPlans.Count - shown)
                    .Append(" more materials.");
            if (!EditorUtility.DisplayDialog(
                "Remove Non-Applicable Shader Properties from All Materials",
                preview.ToString(), "Remove Properties", "Cancel")) return;

            UMAReleaseRepairResult result =
                UMAReleaseValidationRepairUtility.ExecuteMaterialCleanupPlan(materialPlans);
            if (result.succeeded) RemoveIssuesOwnedByMaterials(materialPlans);
            HandleResult(result, false);
        }

        private void RemoveIssuesOwnedByMaterials(
            IList<UMAReleaseMaterialCleanupPlan> materialPlans)
        {
            if (report?.issues == null) return;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; materialPlans != null && i < materialPlans.Count; i++)
            {
                string path = materialPlans[i]?.materialPath;
                if (!string.IsNullOrEmpty(path)) paths.Add(path.Replace('\\', '/'));
            }
            report.issues.RemoveAll(issue => issue != null && paths.Contains(
                (issue.ownerAssetPath ?? string.Empty).Replace('\\', '/')));
            report.issueCount = report.issues.Count;
            report.passed = report.issueCount == 0;
        }

        private static int CountMaterialProperties(
            IList<UMAReleaseMaterialCleanupPlan> plans)
        {
            int count = 0;
            for (int i = 0; plans != null && i < plans.Count; i++)
                count += plans[i]?.PropertyCount ?? 0;
            return count;
        }

        private void HandleResult(UMAReleaseRepairResult result,
            bool removeSelectedIssue = true)
        {
            statusMessage = result.message;
            statusType = result.succeeded ? MessageType.Info : MessageType.Error;
            if (!result.succeeded) return;
            if (removeSelectedIssue && report?.issues != null && selectedIssueIndex >= 0 &&
                selectedIssueIndex < report.issues.Count)
            {
                report.issues.RemoveAt(selectedIssueIndex);
                report.issueCount = report.issues.Count;
                report.passed = report.issueCount == 0;
            }
            selectedIssueIndex = -1;
            detailScroll = Vector2.zero;
            EditorApplication.delayCall += RunValidation;
            Repaint();
        }

        private void RunValidation()
        {
            if (waitingForReport) return;
            string path = UMAReleaseValidationReport.GetAbsolutePath();
            reportWriteTimeBeforeRun = File.Exists(path)
                ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            waitingForReport = true;
            statusMessage = "Running Asset Validation. The grid will reload when it finishes.";
            statusType = MessageType.Info;
            testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            testRunnerApi.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                categoryNames = new[] { "Asset Validation" }
            }));
            Repaint();
        }

        private void PollForReport()
        {
            if (!waitingForReport) return;
            string path = UMAReleaseValidationReport.GetAbsolutePath();
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= reportWriteTimeBeforeRun)
                return;
            waitingForReport = false;
            ReloadReport(false);
            statusMessage = report != null && report.passed
                ? "Validation completed successfully."
                : "Validation completed; review the remaining issues below.";
            statusType = report != null && report.passed ? MessageType.Info : MessageType.Warning;
            Repaint();
        }

        private void ReloadReport(bool showStatus)
        {
            try
            {
                report = UMAReleaseValidationReport.LoadLastReport();
                if (report != null)
                {
                    report.scopes ??= new List<UMAReleaseValidationScopeReport>();
                    report.assets ??= new List<UMAReleaseValidationAssetReport>();
                    report.references ??= new List<UMAReleaseValidationReferenceReport>();
                    report.issues ??= new List<UMAReleaseValidationIssueReport>();
                }
                if (selectedIssueIndex < 0 || report == null ||
                    selectedIssueIndex >= report.issues.Count) selectedIssueIndex = -1;
                if (showStatus)
                {
                    statusMessage = report == null ? "No report was found." : "Report reloaded.";
                    statusType = report == null ? MessageType.Warning : MessageType.Info;
                }
            }
            catch (Exception exception)
            {
                report = null;
                selectedIssueIndex = -1;
                statusMessage = "Could not load the report: " + exception.Message;
                statusType = MessageType.Error;
            }
            Repaint();
        }

        private void RevealReport()
        {
            string path = UMAReleaseValidationReport.GetAbsolutePath();
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
            else EditorUtility.DisplayDialog(WindowTitle, "No report exists at:\n" + path, "OK");
        }

        private bool MatchesSearch(UMAReleaseValidationIssueReport issue)
        {
            if (issue == null) return false;
            string search = (searchText ?? string.Empty).Trim();
            if (search.Length == 0) return true;
            return Contains(issue.scope, search) || Contains(issue.kind, search) ||
                Contains(issue.ownerAssetName, search) || Contains(issue.ownerAssetPath, search) ||
                Contains(issue.referencedAssetName, search) ||
                Contains(issue.referencedAssetPath, search) ||
                Contains(issue.referencedAssetGuid, search) ||
                Contains(issue.propertyPath, search) || Contains(issue.detail, search);
        }

        private static bool Contains(string value, string search) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(search,
                StringComparison.OrdinalIgnoreCase) >= 0;

        private static void DrawDetailLine(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.Width(130f));
                EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.label,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static void PingSelected(UMAReleaseValidationIssueReport issue)
        {
            string path = !string.IsNullOrEmpty(issue.referencedAssetPath)
                ? issue.referencedAssetPath : issue.ownerAssetPath;
            PingPath(path);
        }

        private static void PingPath(string path)
        {
            UnityEngine.Object asset = string.IsNullOrEmpty(path) ? null :
                AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static string ParseUtc(string value)
        {
            return DateTime.TryParse(value, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed.ToLocalTime().ToString("g") : value;
        }

        private static string EmptyAsMissing(string value) =>
            string.IsNullOrEmpty(value) ? "(missing/unresolved)" : value;

        private static Color SelectionColor() => EditorGUIUtility.isProSkin
            ? new Color(0.18f, 0.38f, 0.62f, 0.85f)
            : new Color(0.25f, 0.50f, 0.85f, 0.45f);

        private static GUIStyle SuccessStyle() => new(EditorStyles.boldLabel)
        {
            normal = { textColor = new Color(0.25f, 0.75f, 0.35f) }
        };

        private static GUIStyle ErrorStyle() => new(EditorStyles.boldLabel)
        {
            normal = { textColor = new Color(0.95f, 0.42f, 0.30f) }
        };

        private UMAReleaseValidationIssueReport SelectedIssue => report != null &&
            report.issues != null && selectedIssueIndex >= 0 &&
            selectedIssueIndex < report.issues.Count ? report.issues[selectedIssueIndex] : null;
    }
}

#endif

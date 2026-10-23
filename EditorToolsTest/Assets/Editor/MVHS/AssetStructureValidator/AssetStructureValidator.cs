using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;



namespace MVHS
{
    /// <summary>
    /// Asset Structure Validator
    /// A Unity Editor tool for auditing and migrating assets into your
    /// canonical project folder structure.
    /// 
    /// NOTE:
    /// This code was largly created using Claude Code. Last code generation date (6/27/26)
    /// I will do testing using this tool for upcoming projects for MVHS Game Programming course Spring 27
    ///
    /// SETUP:
    ///   Place this file anywhere inside an Editor folder in your project.
    ///   e.g. Assets/Editor/AssetStructureValidator.cs
    ///
    /// OPEN:
    ///   Unity menu → Window → Asset Structure Validator
    /// </summary>
    public class AssetStructureValidator : EditorWindow
    {
        // ─── Tab state ────────────────────────────────────────────────────────────
        private enum Tabs
        {
            ProjectDependencyViewer,
            SceneDependencyViewer,
            GameObjectDependencyView,
            Rules,
            Orphans
        };
        private Tabs m_SelectedTab = Tabs.ProjectDependencyViewer;
        private Tabs m_LastSelectedTab = Tabs.ProjectDependencyViewer;
        private readonly string[] m_TabLabels = {"Project Dependency Viewer", "Scene Dependency Viewer", "GameObject Dependency Viewer", "Rules", "Orphans"};


        // ─── Shared scroll positions (one per tab) ────────────────────────────────
        private Vector2 m_DependencyScrollPos;
        private Vector2 m_RulesScrollPos;

        // ─── Styles (initialised lazily so GUISkin is ready) ─────────────────────
        private GUIStyle m_HeaderStyle;
        private GUIStyle m_SubHeaderStyle;
        private GUIStyle m_HintStyle;
        private bool m_StylesInitialised;

        // ─── Colours ──────────────────────────────────────────────────────────────
        // A palette that reads clearly in both Unity's dark and light themes.
        private static readonly Color AccentBlue = new Color(0.27f, 0.60f, 0.98f);   // action buttons
        private static readonly Color PassGreen = new Color(0.35f, 0.78f, 0.47f);   // rule pass
        private static readonly Color FailRed = new Color(0.93f, 0.36f, 0.36f);   // rule fail
        private static readonly Color WarnYellow = new Color(0.97f, 0.78f, 0.28f);   // warnings
        private static readonly Color SeparatorCol = new Color(0.15f, 0.15f, 0.15f, 0.6f);

        // ─── Rules data model ─────────────────────────────────────────────────────
        ValidatorRules m_ValidatorRules = new ValidatorRules();


        private GameObject m_LastSelectedObject;

        // ─── Scan state ───────────────────────────────────────────────────────────
        private GameObject m_LastScannedObject;
        private ScanResults m_LastScanResults = new ScanResults();
        private List<string>  m_OrphanResults    = new List<string>();
        private bool m_OrphanHasScanned = false;
        private bool[] m_OrphanSelected;
        private Vector2 m_OrphanScrollPos;
        private int m_OrphanSceneCount;

        // Filter toggles shown above the results list
        private bool m_ShowPass = true;
        private bool m_ShowFail = true;
        private bool m_ShowNoRule = false;  // off by default — keeps the list focused

        // ─────────────────────────────────────────────────────────────────────────
        //  Open the window
        // ─────────────────────────────────────────────────────────────────────────
        [MenuItem("Tools/MVHS/Asset Structure Validator", false, 30)]
        public static void OpenWindow()
        {
            var window = GetWindow<AssetStructureValidator>();
            window.titleContent = new GUIContent("Asset Validator", EditorGUIUtility.IconContent("d_Folder Icon").image);
            window.minSize = new Vector2(420f, 500f);
            window.Show();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            // Styles can't be created here (GUISkin not ready), so we flag for
            // lazy init on the first OnGUI call instead.
            m_StylesInitialised = false;
            m_ValidatorRules.LoadRules();
        }

        private void OnGUI()
        {
            InitStylesIfNeeded();

            DrawHeader();
            DrawTabBar();
            DrawDivider();

            // Route to the active tab

            if (m_SelectedTab == Tabs.ProjectDependencyViewer)
            {
                if (m_LastSelectedTab == Tabs.SceneDependencyViewer || m_LastSelectedTab == Tabs.GameObjectDependencyView)
                {
                    m_LastScannedObject = null;
                    m_LastSelectedObject = null;
                    m_LastScanResults.Clear();
                }
                DrawProjectDependencyViewerTab();
                m_LastSelectedTab = Tabs.ProjectDependencyViewer;
            }
            else if (m_SelectedTab == Tabs.SceneDependencyViewer)
            {
                if (m_LastSelectedTab == Tabs.GameObjectDependencyView || m_LastSelectedTab == Tabs.ProjectDependencyViewer)
                {
                    m_LastScannedObject = null;
                    m_LastSelectedObject = null;
                    m_LastScanResults.Clear();
                }
                DrawSceneDependencyViewerTab();
                m_LastSelectedTab = Tabs.SceneDependencyViewer;
            }
            else if (m_SelectedTab == Tabs.GameObjectDependencyView)
            {
                if (m_LastSelectedTab == Tabs.SceneDependencyViewer || m_LastSelectedTab == Tabs.ProjectDependencyViewer)
                {
                    m_LastScannedObject = null;
                    m_LastSelectedObject = null;
                    m_LastScanResults.Clear();
                }
                DrawGameObjectDependencyViewerTab();
                m_LastSelectedTab = Tabs.GameObjectDependencyView;
            }
            else if (m_SelectedTab == Tabs.Rules)
            {
                DrawRulesTab();
                m_SelectedTab = Tabs.Rules;
            }
            else if (m_SelectedTab == Tabs.Orphans)
            {
                DrawOrphanScannerTab();
                m_SelectedTab = Tabs.Orphans;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Header
        // ─────────────────────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Asset Structure Validator", m_HeaderStyle);
            EditorGUILayout.LabelField("Audit dependencies · validate rules · migrate assets", m_HintStyle);
            EditorGUILayout.Space(6);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Tab bar
        // ─────────────────────────────────────────────────────────────────────────
        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            m_SelectedTab = (Tabs)GUILayout.Toolbar((int)m_SelectedTab, m_TabLabels, GUILayout.Height(28));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProjectDependencyViewerTab()
        {
            GUI.backgroundColor = AccentBlue;
            if (GUILayout.Button("Scan Dependencies", GUILayout.Height(28), GUILayout.Width(150)))
                OnScanProjectClicked();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // Clear button — only shown after a scan
            if (m_LastScanResults.HasScanned)
            {
                if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(60)))
                {
                    m_LastScanResults.Clear();
                    m_LastScannedObject = null;
                }
            }

            GUILayout.Space(8);


            EditorGUILayout.Space(8);
            DrawDivider();

            // ── Pre-scan empty state ───────────────────────────────────────────
            if (!m_LastScanResults.HasScanned)
            {
                DrawEmptyState("Click Scan Dependencies to inspect project.");
                return;
            }

            DrawScanResults();
        }

        private void DrawSceneDependencyViewerTab()
        {
            GUI.backgroundColor = AccentBlue;
            if (GUILayout.Button("Scan Dependencies", GUILayout.Height(28), GUILayout.Width(150)))
                OnScaneSceneClicked();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // Clear button — only shown after a scan
            if (m_LastScanResults.HasScanned)
            {
                if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(60)))
                {
                    m_LastScanResults.Clear();
                    m_LastScannedObject = null;
                }
            }

            GUILayout.Space(8);


            EditorGUILayout.Space(8);
            DrawDivider();

            // ── Pre-scan empty state ───────────────────────────────────────────
            if (!m_LastScanResults.HasScanned)
            {
                DrawEmptyState("Click Scan Dependencies to inspect scene");
                return;
            }

            DrawScanResults();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Tab: Dependency Viewer
        // ─────────────────────────────────────────────────────────────────────────
        private void DrawGameObjectDependencyViewerTab()
        {

            EditorGUILayout.Space(8);

            // ── Scan controls ──────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Selected Object", GUILayout.Width(104));

            GameObject selected = Selection.activeGameObject;

            if (m_LastScanResults.HasScanned && selected != null && selected != m_LastSelectedObject)
            {
                m_LastScanResults.Clear();
                m_LastScannedObject = null;
            }
            m_LastSelectedObject = selected;


            GUI.enabled = false;
            EditorGUILayout.ObjectField(selected, typeof(GameObject), true);
            GUI.enabled = true;
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUI.enabled = selected != null;
            GUI.backgroundColor = AccentBlue;
            if (GUILayout.Button("Scan Dependencies", GUILayout.Height(28), GUILayout.Width(150)))
                OnScanClicked();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // Clear button — only shown after a scan
            if (m_LastScanResults.HasScanned)
            {
                if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(60)))
                {
                    m_LastScanResults.Clear();
                    m_LastScannedObject = null;
                }
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            DrawDivider();

            // ── Pre-scan empty state ───────────────────────────────────────────
            if (!m_LastScanResults.HasScanned)
            {
                DrawEmptyState(
                    selected == null
                        ? "Select a GameObject in the Hierarchy, then click Scan."
                        : $"Click  Scan Dependencies  to inspect  \"{selected.name}\".");
                return;
            }
            DrawScanResults();
        }

        public void DrawScanResults()
        {
            // ── Summary bar ────────────────────────────────────────────────────
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            // Object name — guard against the object being destroyed between scan and repaint
            string objLabel = (m_LastScannedObject != null) ? $"\"{m_LastScannedObject.name}\"" : "previous scan";
            EditorGUILayout.LabelField($"Results for {objLabel}", m_SubHeaderStyle);

            GUILayout.FlexibleSpace();

            // Pass / Fail / NoRule counts as coloured badges
            DrawCountBadge($"✓ {m_LastScanResults.passCount}", PassGreen);
            GUILayout.Space(4);
            DrawCountBadge($"✗ {m_LastScanResults.failCount}", FailRed);
            GUILayout.Space(4);
            DrawCountBadge($"— {m_LastScanResults.noRuleCount}", new Color(0.5f, 0.5f, 0.5f));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // ── Filter toggles ─────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Show:", GUILayout.Width(36));

            GUI.backgroundColor = m_ShowPass ? PassGreen : Color.white;
            if (GUILayout.Button("Pass", EditorStyles.miniButton, GUILayout.Width(48)))
                m_ShowPass = !m_ShowPass;

            GUI.backgroundColor = m_ShowFail ? FailRed : Color.white;
            if (GUILayout.Button("Fail", EditorStyles.miniButton, GUILayout.Width(48)))
                m_ShowFail = !m_ShowFail;

            GUI.backgroundColor = m_ShowNoRule ? WarnYellow : Color.white;
            if (GUILayout.Button("No Rule", EditorStyles.miniButton, GUILayout.Width(58)))
                m_ShowNoRule = !m_ShowNoRule;

            GUI.backgroundColor = Color.white;
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            DrawDivider();

            // ── Column headers ─────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Status", EditorStyles.miniLabel, GUILayout.Width(58));
            EditorGUILayout.LabelField("Ext", EditorStyles.miniLabel, GUILayout.Width(72));
            EditorGUILayout.LabelField("Asset Path", EditorStyles.miniLabel);

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            DrawDivider();

            // ── Results list ───────────────────────────────────────────────────
            m_DependencyScrollPos = EditorGUILayout.BeginScrollView(m_DependencyScrollPos);

            int rowIndex = 0;
            bool anyVisible = false;

            //foreach (var result in _scanResults)
            foreach (DependencyResult result in m_LastScanResults.dependencyResults)
            {
                // Apply filter
                if (result.status == AssetStatus.Pass && !m_ShowPass) continue;
                if (result.status == AssetStatus.Fail && !m_ShowFail) continue;
                if (result.status == AssetStatus.NoRule && !m_ShowNoRule) continue;

                anyVisible = true;

                // Alternate row tint
                var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
                if (rowIndex % 2 == 0)
                    EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.07f));
                rowIndex++;

                GUILayout.Space(8);

                // Status badge — show ✓ MOVED if already migrated this session
                if (result.moved)
                {
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = PassGreen;
                    GUILayout.Label(" ✓ MOVED ", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                    GUI.backgroundColor = prev;
                }
                else
                {
                    switch (result.status)
                    {
                        case AssetStatus.Pass: DrawBadge(BadgeType.Pass); break;
                        case AssetStatus.Fail: DrawBadge(BadgeType.Fail); break;
                        case AssetStatus.NoRule: DrawBadge(BadgeType.Warn); break;
                    }
                }

                GUILayout.Space(4);

                // Extension
                GUI.backgroundColor = AccentBlue;
                GUILayout.Label(result.extension, EditorStyles.miniButton, GUILayout.Width(62));
                GUI.backgroundColor = Color.white;

                GUILayout.Space(4);

                // Asset path — truncated display but full path on hover
                string displayPath = TruncatePath(result.assetPath, 42);
                EditorGUILayout.LabelField(new GUIContent(displayPath, result.assetPath));

                // Move button — only for unresolved failures
                if (result.status == AssetStatus.Fail && !result.moved)
                {
                    GUI.backgroundColor = WarnYellow;
                    if (GUILayout.Button("Move", EditorStyles.miniButton,
                        GUILayout.Width(42), GUILayout.Height(16)))
                    {
                        MoveAsset(result);
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();

                // Inline hint for failing assets — what folder they should be in
                if (result.status == AssetStatus.Fail && !result.moved)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(76);
                    GUI.color = FailRed;
                    EditorGUILayout.LabelField(
                        $"→ should be somewhere under  …/{result.requiredFolder}/",
                        m_HintStyle);
                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (!anyVisible)
                DrawEmptyState("No results match the current filters.");

            EditorGUILayout.EndScrollView();

            // ── Footer summary ─────────────────────────────────────────────────
            EditorGUILayout.Space(4);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Tab: Rules (read-only reference)
        // ─────────────────────────────────────────────────────────────────────────
        private void DrawRulesTab()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Folder Rules", m_SubHeaderStyle);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(
                "These rules define where each file type must live in your project. " +
                "An asset passes if its path contains the required folder name.",
                m_HintStyle, GUILayout.Height(36));

            EditorGUILayout.Space(4);

            // ── JSON file path + reload button ─────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField($"Source:  {ValidatorRules.RulesPath}", m_HintStyle);
            if (GUILayout.Button("↺ Reload", EditorStyles.miniButton, GUILayout.Width(64), GUILayout.Height(18)))
                m_ValidatorRules.LoadRules();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            DrawDivider();

            if (m_ValidatorRules.RulesCount == 0)
            {
                // ── Missing or empty JSON ───────────────────────────────────────
                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                GUI.color = WarnYellow;
                EditorGUILayout.LabelField(
                    $"⚠  No rules loaded. Make sure  {ValidatorRules.RulesPath}  exists.",
                    m_HintStyle);
                GUI.color = Color.white;
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
                return;
            }

            // ── Column headers ─────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Extension", EditorStyles.miniLabel, GUILayout.Width(140));
            EditorGUILayout.LabelField("Must live under", EditorStyles.miniLabel, GUILayout.Width(160));
            EditorGUILayout.LabelField("Note", EditorStyles.miniLabel);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            DrawDivider();
            EditorGUILayout.Space(2);

            // ── Rule rows ──────────────────────────────────────────────────────
            m_RulesScrollPos = EditorGUILayout.BeginScrollView(m_RulesScrollPos);

            IReadOnlyList<FolderRule> rules = m_ValidatorRules.GetActiveRules();
            for (int i = 0; i < m_ValidatorRules.RulesCount; i++)
            {
                FolderRule rule = rules[i];

                // Alternate row tint
                var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
                if (i % 2 == 0)
                    EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.08f));

                GUILayout.Space(8);

                // Extension badge
                GUI.backgroundColor = AccentBlue;
                GUILayout.Label(rule.extension, EditorStyles.boldLabel, GUILayout.Width(130));
                GUI.backgroundColor = Color.white;

                GUILayout.Space(10);

                // Required folder — monospace so it reads like a path
                EditorGUILayout.LabelField($"…/{rule.requiredFolder}/", EditorStyles.label, GUILayout.Width(150));

                // Optional description
                if (!string.IsNullOrEmpty(rule.description))
                    EditorGUILayout.LabelField(rule.description, m_HintStyle);

                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // ── Footer ─────────────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            DrawDivider();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField(
                $"{m_ValidatorRules.RulesCount} rule{(m_ValidatorRules.RulesCount != 1 ? "s" : "")}  ·  edit  {ValidatorRules.RulesPath}  to make changes",
                m_HintStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private void DrawOrphanScannerTab()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Orphan Scanner", m_SubHeaderStyle);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(
                "Finds assets inside  Assets/Project/  that are not referenced by any " +
                "scene, prefab, or asset in the project. Review carefully before deleting.",
                m_HintStyle, GUILayout.Height(40));

            EditorGUILayout.Space(6);

            // ── Toolbar ────────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUI.backgroundColor = AccentBlue;
            if (GUILayout.Button("Scan All Scenes + Prefabs", GUILayout.Height(28), GUILayout.Width(190)))
                OnOrphanScanClicked();
            GUI.backgroundColor = Color.white;

            if (m_OrphanHasScanned && m_OrphanResults.Count > 0)
            {
                GUILayout.Space(8);

                if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(34), GUILayout.Height(18)))
                    for (int i = 0; i < m_OrphanSelected.Length; i++) m_OrphanSelected[i] = true;

                if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(38), GUILayout.Height(18)))
                    for (int i = 0; i < m_OrphanSelected.Length; i++) m_OrphanSelected[i] = false;

                GUILayout.FlexibleSpace();

                int checkedCount = 0;
                foreach (var b in m_OrphanSelected) if (b) checkedCount++;

                GUI.enabled = checkedCount > 0;
                GUI.backgroundColor = FailRed;
                if (GUILayout.Button($"Delete Selected ({checkedCount})", GUILayout.Height(28), GUILayout.Width(160)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Delete Orphaned Assets",
                        $"Permanently delete {checkedCount} asset(s)?\n\n" +
                        "This cannot be undone. Make sure you have committed or backed up your project first.",
                        "Delete", "Cancel"))
                    {
                        OnDeleteOrphansClicked();
                    }
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            DrawDivider();

            // ── Pre-scan empty state ───────────────────────────────────────────
            if (!m_OrphanHasScanned)
            {
                DrawEmptyState("Click  Scan All Scenes + Prefabs  to find unreferenced assets.");
                return;
            }

            // ── Scan summary ───────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField(
                $"Scanned {m_OrphanSceneCount} scene(s) + all prefabs in Assets/Project/",
                m_HintStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
            DrawDivider();

            if (m_OrphanResults.Count == 0)
            {
                DrawEmptyState("✓  No orphaned assets found. Your project folder is clean.");
                return;
            }

            // ── Column headers ─────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("", GUILayout.Width(20));
            EditorGUILayout.LabelField("Ext", EditorStyles.miniLabel, GUILayout.Width(72));
            EditorGUILayout.LabelField("Asset Path", EditorStyles.miniLabel);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            DrawDivider();

            // ── Orphan rows ────────────────────────────────────────────────────
            m_OrphanScrollPos = EditorGUILayout.BeginScrollView(m_OrphanScrollPos);

            for (int i = 0; i < m_OrphanResults.Count; i++)
            {
                string path = m_OrphanResults[i];
                string ext = Path.GetExtension(path).ToLower();

                var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
                if (i % 2 == 0)
                    EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.07f));

                GUILayout.Space(8);

                m_OrphanSelected[i] = EditorGUILayout.Toggle(m_OrphanSelected[i], GUILayout.Width(20));

                GUI.backgroundColor = WarnYellow;
                GUILayout.Label(string.IsNullOrEmpty(ext) ? "(none)" : ext,
                    EditorStyles.miniButton, GUILayout.Width(62));
                GUI.backgroundColor = Color.white;

                GUILayout.Space(4);

                string display = TruncatePath(path, 52);
                EditorGUILayout.LabelField(new GUIContent(display, path));

                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // ── Footer ─────────────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            DrawDivider();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUI.color = WarnYellow;
            EditorGUILayout.LabelField(
                $"⚠  {m_OrphanResults.Count} orphaned asset(s) found. " +
                "Check each one before deleting — some may be intentionally unused.",
                m_HintStyle);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Orphan scan logic
        // ─────────────────────────────────────────────────────────────────────────

        private void OnOrphanScanClicked()
        {
            m_OrphanResults.Clear();
            m_OrphanHasScanned = false;

            // ── 1. Collect every asset referenced by all scenes + prefabs ─────
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Every scene in the project (not just the open one)
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            m_OrphanSceneCount = sceneGuids.Length;

            var allRoots = new List<string>();
            foreach (var guid in sceneGuids)
                allRoots.Add(AssetDatabase.GUIDToAssetPath(guid));

            // Every prefab inside Assets/Project/
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Project" });
            foreach (var guid in prefabGuids)
                allRoots.Add(AssetDatabase.GUIDToAssetPath(guid));

            // GetDependencies expands everything recursively in one call
            if (allRoots.Count > 0)
            {
                string[] deps = AssetDatabase.GetDependencies(allRoots.ToArray(), recursive: true);
                foreach (var d in deps)
                    referenced.Add(d);
            }

            // Mark the root files themselves as referenced too
            foreach (var r in allRoots)
                referenced.Add(r);

            // ── 2. Find every asset inside Assets/Project/ ─────────────────────
            string[] projectAssetGuids = AssetDatabase.FindAssets("", new[] { "Assets/Project" });

            foreach (var guid in projectAssetGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                if (!referenced.Contains(path))
                    m_OrphanResults.Add(path);
            }

            m_OrphanResults.Sort();
            m_OrphanSelected = new bool[m_OrphanResults.Count]; // all unchecked by default
            m_OrphanHasScanned = true;

            Debug.Log($"[AssetValidator] Orphan scan: {m_OrphanResults.Count} unreferenced asset(s) in Assets/Project/");
            Repaint();
        }

        private void OnDeleteOrphansClicked()
        {
            var toDelete = new List<string>();
            for (int i = 0; i < m_OrphanResults.Count; i++)
                if (m_OrphanSelected[i]) toDelete.Add(m_OrphanResults[i]);

            int deleted = 0;
            int failed = 0;

            foreach (var path in toDelete)
            {
                if (AssetDatabase.DeleteAsset(path))
                    deleted++;
                else
                {
                    Debug.LogWarning($"[AssetValidator] Could not delete: {path}");
                    failed++;
                }
            }

            // Rebuild list removing successfully deleted entries
            var deletedSet = new HashSet<string>(toDelete.GetRange(0, deleted),
                StringComparer.OrdinalIgnoreCase);
            m_OrphanResults.RemoveAll(p => deletedSet.Contains(p));
            m_OrphanSelected = new bool[m_OrphanResults.Count];

            AssetDatabase.Refresh();

            string summary = $"Deleted {deleted} asset(s).";
            if (failed > 0) summary += $"\n{failed} could not be deleted — check the Console.";
            EditorUtility.DisplayDialog("Delete Complete", summary, "OK");

            Debug.Log($"[AssetValidator] Orphan delete: {deleted} deleted, {failed} failed.");
            Repaint();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Shared UI helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Draws a subtle full-width horizontal separator.</summary>
        private void DrawDivider()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            rect.x = 0; rect.width = position.width;
            EditorGUI.DrawRect(rect, SeparatorCol);
        }

        /// <summary>Centred hint shown when a panel has no content yet.</summary>
        private void DrawEmptyState(string message)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(message, m_HintStyle, GUILayout.MaxWidth(340));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Colour badge helper (used by Steps 3-5 for pass / fail / warn labels)
        // ─────────────────────────────────────────────────────────────────────────
        public enum BadgeType { Pass, Fail, Warn }

        protected void DrawBadge(BadgeType type)
        {
            string label;
            Color col;
            switch (type)
            {
                case BadgeType.Pass: label = " ✓ OK "; col = PassGreen; break;
                case BadgeType.Fail: label = " ✗ FAIL "; col = FailRed; break;
                default: label = " ⚠ WARN "; col = WarnYellow; break;
            }
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = col;
            GUILayout.Label(label, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = prevBg;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Public accessors — Step 3 (dependency scanner) calls these
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks a single asset path against loaded rules.
        /// Returns null  → no rule covers this extension (no opinion).
        /// Returns true  → path satisfies its rule.
        /// Returns false → path violates its rule.
        /// </summary>
        public bool? ValidatePath(string assetPath, IReadOnlyList<FolderRule> rules)
        {
            string ext = Path.GetExtension(assetPath).ToLower();
            foreach (var rule in rules)
            {
                if (!string.Equals(rule.extension, ext, StringComparison.OrdinalIgnoreCase)) continue;
                return assetPath.IndexOf(rule.requiredFolder, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Scanner
        // ─────────────────────────────────────────────────────────────────────────

        private void OnScanProjectClicked()
        {
            m_LastScanResults = DependencyScanner.Scan("Assets/Project", m_ValidatorRules);
            m_LastScannedObject = null;
            Repaint();
        }

        private void OnScaneSceneClicked()
        {
            //DependencyScanner scanner = new DependencyScanner();
            m_LastScanResults = DependencyScanner.Scan(m_ValidatorRules);
            m_LastScannedObject = null;
            Repaint();
        }


        private void OnScanClicked()
        {
            //DependencyScanner scanner = new DependencyScanner();
            m_LastScanResults = DependencyScanner.Scan(Selection.activeGameObject, m_ValidatorRules);
            m_LastScannedObject = Selection.activeGameObject;
            Repaint();
        }


        // ─────────────────────────────────────────────────────────────────────────
        //  Migration
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Moves a single failing asset into a subfolder the student picks.
        /// Uses a folder browser (not a file picker) so the student can navigate
        /// into nested subfolders — e.g. choosing Art/Textures/Environment/
        /// rather than just Art/.
        ///
        /// Flow:
        ///   1. Open a folder browser pre-aimed at Assets/Project/{requiredFolder}/
        ///   2. Validate the chosen folder still satisfies the rule
        ///   3. Warn (but allow override) if the destination breaks the rule
        ///   4. Move via AssetDatabase.MoveAsset so GUIDs + references are preserved
        /// </summary>
        private void MoveAsset(DependencyResult result)
        {
            bool moveSuccess = AssetMover.MoveAsset(result.assetPath, result.requiredFolder, m_ValidatorRules);
            if (moveSuccess)
            {

                if (m_SelectedTab == Tabs.SceneDependencyViewer)
                {
                    m_LastScanResults = DependencyScanner.Scan(m_ValidatorRules);
                }
                else if (m_SelectedTab == Tabs.GameObjectDependencyView)
                {
                    m_LastScanResults = DependencyScanner.Scan(Selection.activeGameObject, m_ValidatorRules);
                }

                Repaint();
            }
        }

        /// <summary>
        /// Shortens a long asset path for display by keeping the filename and
        /// as much of the tail as fits within maxChars, prefixing with "…/".
        /// The full path is still shown in the tooltip.
        /// </summary>
        private static string TruncatePath(string path, int maxChars)
        {
            if (path.Length <= maxChars) return path;
            // Try to preserve the last two segments (folder/file.ext)
            int slash = path.LastIndexOf('/', path.Length - 1);
            if (slash > 0) slash = path.LastIndexOf('/', slash - 1);
            string tail = slash >= 0 ? path.Substring(slash) : path;
            return tail.Length <= maxChars ? "…" + tail : "…" + tail.Substring(tail.Length - maxChars);
        }

        /// <summary>Small coloured count pill used in the summary bar.</summary>
        private void DrawCountBadge(string label, Color col)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = col;
            GUILayout.Label(label, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = prev;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Style initialisation
        // ─────────────────────────────────────────────────────────────────────────
        private void InitStylesIfNeeded()
        {
            if (m_StylesInitialised) return;

            m_HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0)
            };

            m_SubHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0)
            };

            m_HintStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 0, 0),
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };

            m_StylesInitialised = true;
        }
    }
}
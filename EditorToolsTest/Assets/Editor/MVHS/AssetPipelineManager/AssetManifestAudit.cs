using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MVHS
{
    /// <summary>
    /// Read-only instructor tool that shows every file recorded across all
    /// manifests, whether it exists on disk, and whether any scene or prefab
    /// in the project actually references it.
    ///
    /// Use this to identify stale manifest entries — files that appear in a
    /// manifest but are no longer referenced anywhere in the project.
    /// To clean up, manually edit the relevant manifest JSON files and commit.
    ///
    /// Usage: Tools > MVHS > Utilities > Manifest Audit
    /// </summary>
    public class AssetManifestAudit : EditorWindow
    {
        // -------------------------------------------------------------------------
        // Data
        // -------------------------------------------------------------------------

        private enum FileStatus { OnDisk, Missing }
        private enum RefStatus { Referenced, Unreferenced, Unchecked }

        private struct AuditRow
        {
            public string assetPath;
            public FileStatus fileStatus;
            public RefStatus refStatus;
            public List<string> appearsInManifests; // manifest filenames
        }

        private List<AuditRow> m_Rows = new List<AuditRow>();
        private Vector2 m_Scroll;
        private bool m_IsScanning = false;
        private bool m_HasResults = false;

        // Filter toggles
        private bool m_ShowOnDisk = true;
        private bool m_ShowMissing = true;
        private bool m_ShowReferenced = true;
        private bool m_ShowUnreferenced = true;
        private bool m_ShowUnchecked = true;

        // -------------------------------------------------------------------------
        // Window
        // -------------------------------------------------------------------------

        [MenuItem("Tools/MVHS/Utilities/Manifest Audit", false, 102)]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetManifestAudit>("Manifest Audit");
            window.minSize = new Vector2(600, 400);
        }

        // -------------------------------------------------------------------------
        // GUI
        // -------------------------------------------------------------------------

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(6);
            DrawScanButton();
            EditorGUILayout.Space(6);

            if (m_HasResults)
            {
                DrawFilters();
                EditorGUILayout.Space(4);
                DrawSummary();
                EditorGUILayout.Space(4);
                DrawTable();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Manifest Audit  —  Instructor Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Read-only. Shows every file recorded in any manifest, whether it exists on disk, " +
                "and whether any scene or prefab in the project references it.\n\n" +
                "To remove a stale entry: manually edit the manifest JSON file(s) listed and commit.",
                MessageType.Info);
        }

        private void DrawScanButton()
        {
            GUI.enabled = !m_IsScanning;
            if (GUILayout.Button(m_IsScanning ? "Scanning…" : "Scan Manifests", GUILayout.Height(30)))
            {
                RunScan();
            }
            GUI.enabled = true;
        }

        private void DrawFilters()
        {
            EditorGUILayout.LabelField("Show:", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();

            m_ShowOnDisk = GUILayout.Toggle(m_ShowOnDisk, "On Disk", EditorStyles.miniButton, GUILayout.Width(80));
            m_ShowMissing = GUILayout.Toggle(m_ShowMissing, "Missing", EditorStyles.miniButton, GUILayout.Width(80));
            GUILayout.Space(10);
            m_ShowReferenced = GUILayout.Toggle(m_ShowReferenced, "Referenced", EditorStyles.miniButton, GUILayout.Width(90));
            m_ShowUnreferenced = GUILayout.Toggle(m_ShowUnreferenced, "Unreferenced", EditorStyles.miniButton, GUILayout.Width(100));
            m_ShowUnchecked = GUILayout.Toggle(m_ShowUnchecked, "Not checked", EditorStyles.miniButton, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            int total = m_Rows.Count;
            int onDisk = m_Rows.Count(r => r.fileStatus == FileStatus.OnDisk);
            int missing = m_Rows.Count(r => r.fileStatus == FileStatus.Missing);
            int referenced = m_Rows.Count(r => r.refStatus == RefStatus.Referenced);
            int unreferenced = m_Rows.Count(r => r.refStatus == RefStatus.Unreferenced);

            EditorGUILayout.LabelField(
                $"Total: {total}   |   On disk: {onDisk}   Missing: {missing}   |   Referenced: {referenced}   Unreferenced: {unreferenced}",
                EditorStyles.miniLabel);
        }

        private void DrawTable()
        {
            // Column header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Asset Path", EditorStyles.toolbarButton, GUILayout.MinWidth(300));
            EditorGUILayout.LabelField("On Disk", EditorStyles.toolbarButton, GUILayout.Width(70));
            EditorGUILayout.LabelField("Referenced", EditorStyles.toolbarButton, GUILayout.Width(90));
            EditorGUILayout.LabelField("In Manifests", EditorStyles.toolbarButton, GUILayout.Width(160));
            EditorGUILayout.EndHorizontal();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            foreach (var row in m_Rows)
            {
                // Apply filters
                if (row.fileStatus == FileStatus.OnDisk && !m_ShowOnDisk) continue;
                if (row.fileStatus == FileStatus.Missing && !m_ShowMissing) continue;
                if (row.refStatus == RefStatus.Referenced && !m_ShowReferenced) continue;
                if (row.refStatus == RefStatus.Unreferenced && !m_ShowUnreferenced) continue;
                if (row.refStatus == RefStatus.Unchecked && !m_ShowUnchecked) continue;

                DrawRow(row);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(AuditRow row)
        {
            // Highlight rows that are both unreferenced AND on disk — likely stale
            bool isStale = row.fileStatus == FileStatus.OnDisk && row.refStatus == RefStatus.Unreferenced;
            bool isMissing = row.fileStatus == FileStatus.Missing;

            Color bg = GUI.backgroundColor;
            if (isStale) GUI.backgroundColor = new Color(1f, 0.85f, 0.4f); // amber
            if (isMissing) GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);  // red

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = bg;

            // Asset path — clicking pings the asset in the Project window
            string shortPath = row.assetPath.Length > 55
                ? "…" + row.assetPath.Substring(row.assetPath.Length - 52)
                : row.assetPath;

            if (GUILayout.Button(new GUIContent(shortPath, row.assetPath),
                EditorStyles.miniLabel, GUILayout.MinWidth(300)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(row.assetPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }

            // On disk
            string diskLabel = row.fileStatus == FileStatus.OnDisk ? "✓" : "✗";
            GUIStyle diskStyle = row.fileStatus == FileStatus.OnDisk
                ? EditorStyles.miniLabel : EditorStyles.miniLabel;
            EditorGUILayout.LabelField(diskLabel, GUILayout.Width(70));

            // Referenced
            string refLabel;
            if (row.refStatus == RefStatus.Referenced) refLabel = "✓";
            else if (row.refStatus == RefStatus.Unreferenced) refLabel = "✗";
            else refLabel = "—";
            EditorGUILayout.LabelField(refLabel, GUILayout.Width(90));

            // Which manifests
            string manifests = string.Join(", ", row.appearsInManifests);
            EditorGUILayout.LabelField(new GUIContent(manifests, manifests),
                EditorStyles.miniLabel, GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();
        }

        // -------------------------------------------------------------------------
        // Scan Logic
        // -------------------------------------------------------------------------

        private void RunScan()
        {
            m_Rows.Clear();
            m_HasResults = false;
            m_IsScanning = true;
            Repaint();

            try
            {
                // Step 1 — collect all files from all manifests
                // key: assetPath, value: list of manifest filenames that mention it
                var allFiles = new Dictionary<string, List<string>>();

                string diskManifestPath = AssetPipelineConfig.GetDiskPath(
                    AssetPipelineConfig.GetManifestFolderAssetPath());

                if (Directory.Exists(diskManifestPath))
                {
                    string[] manifestFiles = Directory.GetFiles(diskManifestPath, "*.json")
                        .Where(f => Path.GetFileName(f) != "package_urls.json")
                        .ToArray();

                    foreach (string file in manifestFiles)
                    {
                        string manifestName = Path.GetFileName(file);
                        try
                        {
                            string json = File.ReadAllText(file);
                            var manifest = JsonUtility.FromJson<AssetPipelineManifest>(json);
                            if (manifest?.folders == null) continue;

                            foreach (var folder in manifest.folders)
                            {
                                foreach (string assetPath in folder.files)
                                {
                                    if (!allFiles.ContainsKey(assetPath))
                                        allFiles[assetPath] = new List<string>();
                                    if (!allFiles[assetPath].Contains(manifestName))
                                        allFiles[assetPath].Add(manifestName);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[ManifestAudit] Could not read {manifestName}: {e.Message}");
                        }
                    }
                }

                // Step 2 — build the set of all assets referenced by scenes and prefabs
                // This is the expensive step — we collect dependencies of every scene and prefab
                HashSet<string> referencedAssets = BuildReferencedAssetSet();

                // Step 3 — build the audit rows
                string projectRoot = Path.GetDirectoryName(Application.dataPath);

                foreach (var kvp in allFiles.OrderBy(k => k.Key))
                {
                    string assetPath = kvp.Key;
                    string fullDiskPath = Path.Combine(projectRoot, assetPath);

                    bool existsOnDisk = File.Exists(fullDiskPath);

                    RefStatus refStatus;
                    if (!existsOnDisk)
                    {
                        // Can't check references for a file that isn't on disk
                        refStatus = RefStatus.Unchecked;
                    }
                    else if (referencedAssets.Contains(assetPath))
                    {
                        refStatus = RefStatus.Referenced;
                    }
                    else
                    {
                        refStatus = RefStatus.Unreferenced;
                    }

                    m_Rows.Add(new AuditRow
                    {
                        assetPath = assetPath,
                        fileStatus = existsOnDisk ? FileStatus.OnDisk : FileStatus.Missing,
                        refStatus = refStatus,
                        appearsInManifests = kvp.Value
                    });
                }

                m_HasResults = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManifestAudit] Scan failed: {e.Message}");
            }
            finally
            {
                m_IsScanning = false;
                Repaint();
            }
        }

        /// <summary>
        /// Collects all asset paths that are dependencies of any scene or prefab
        /// in Assets/Project/. Returns asset paths in the same format as manifests
        /// (e.g. "Assets/Project/Art/Sprites/ship.png").
        /// </summary>
        private HashSet<string> BuildReferencedAssetSet()
        {
            var referenced = new HashSet<string>();

            // Find all scenes and prefabs under Assets/Project
            string[] guids = AssetDatabase.FindAssets("t:Scene t:Prefab", new[] { "Assets/Project" });

            int total = guids.Length;
            for (int i = 0; i < total; i++)
            {
                string guid = guids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);

                EditorUtility.DisplayProgressBar(
                    "Manifest Audit",
                    $"Checking dependencies… ({i + 1}/{total})",
                    (float)(i + 1) / total);

                try
                {
                    string[] deps = AssetDatabase.GetDependencies(path, recursive: true);
                    foreach (string dep in deps)
                        referenced.Add(dep);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ManifestAudit] Could not get dependencies for {path}: {e.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            return referenced;
        }
    }
}
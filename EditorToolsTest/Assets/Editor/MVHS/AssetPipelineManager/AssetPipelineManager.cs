using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MVHS
{
    /// <summary>
    /// Editor window for importing asset packages and monitoring folder status.
    ///
    /// Shows which folders have missing binary assets and which Google Drive
    /// package each manifest points to, so students know exactly what to import.
    ///
    /// Export is handled by Pre-PR Checklist (Tools > MVHS > Pre-PR Checklist).
    ///
    /// Usage: Tools > MVHS > Asset Pipeline Manager
    /// </summary>
    public class AssetPipelineManager : EditorWindow
    {
        // -------------------------------------------------------------------------
        // Data Structures
        // -------------------------------------------------------------------------

        public struct FolderStatus
        {
            public string path;
            public FolderState state;
            public int diskFileCount;
            public int expectedFileCount;
        }

        /// <summary>
        /// One manifest file and its associated Drive URL (if recorded).
        /// Used to display import guidance per-manifest.
        /// </summary>
        private struct ManifestImportInfo
        {
            public string manifestFileName;   // e.g. "manifest_apetr.json"
            public string exportedBy;
            public string exportDate;
            public string driveUrl;           // from package_urls.json, empty if not recorded
            public List<string> missingFiles; // files listed in this manifest not on disk
        }

        public enum FolderState
        {
            Ready,              // Has assets on disk — good to go.
            NeedsImport,        // Manifest says it should have files, but they're missing.
            EmptyNoManifest,    // Empty and no manifest mentions it — nobody has added assets yet.
            Missing             // Folder doesn't exist on disk at all.
        }

        // -------------------------------------------------------------------------
        // Fields
        // -------------------------------------------------------------------------

        private Vector2 m_ScrollPosition;

        // Cached state — rebuilt on Refresh().
        private List<FolderStatus> m_FolderStatuses = new List<FolderStatus>();
        private Dictionary<string, List<string>> m_ExpectedFiles = new Dictionary<string, List<string>>();

        // Import guidance — manifests that have missing files, newest first.
        private List<ManifestImportInfo> m_ManifestsNeedingImport = new List<ManifestImportInfo>();

        // -------------------------------------------------------------------------
        // Window Setup
        // -------------------------------------------------------------------------

        [MenuItem("Tools/MVHS/Asset Pipeline Manager", false, 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetPipelineManager>("Asset Pipeline Manager");
            window.minSize = new Vector2(450, 550);
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnFocus()
        {
            Refresh();
        }

        // -------------------------------------------------------------------------
        // Refresh — rebuild all cached state
        // -------------------------------------------------------------------------

        private void Refresh()
        {
            LoadCombinedManifest();
            RefreshFolderStatuses();
            BuildManifestImportList();
        }

        // -------------------------------------------------------------------------
        // Manifest Loading — combines all manifest files into one union picture
        // -------------------------------------------------------------------------

        private void LoadCombinedManifest()
        {
            m_ExpectedFiles.Clear();
            string diskManifestPath = AssetPipelineConfig.GetDiskPath(AssetPipelineConfig.GetManifestFolderAssetPath());

            if (!Directory.Exists(diskManifestPath))
                return;

            string[] manifestFiles = Directory.GetFiles(diskManifestPath, "*.json");

            foreach (string file in manifestFiles)
            {
                // Skip package_urls.json — it's not a manifest.
                if (Path.GetFileName(file) == "package_urls.json") continue;

                try
                {
                    string json = File.ReadAllText(file);
                    var manifest = JsonUtility.FromJson<AssetPipelineManifest>(json);

                    if (manifest?.folders == null) continue;

                    foreach (var folder in manifest.folders)
                    {
                        if (!m_ExpectedFiles.ContainsKey(folder.folderPath))
                            m_ExpectedFiles[folder.folderPath] = new List<string>();

                        foreach (string f in folder.files)
                        {
                            if (!m_ExpectedFiles[folder.folderPath].Contains(f))
                                m_ExpectedFiles[folder.folderPath].Add(f);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AssetPipelineManager] Could not read manifest {file}: {e.Message}");
                }
            }
        }

        // -------------------------------------------------------------------------
        // Folder Status
        // -------------------------------------------------------------------------

        private static bool AssetPathExists(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullPath = Path.Combine(projectRoot, assetPath);
            return File.Exists(fullPath);
        }

        private void RefreshFolderStatuses()
        {
            m_FolderStatuses.Clear();

            foreach (string folder in AssetPipelineConfig.projectFolders)
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string diskPath = Path.Combine(projectRoot, folder).Replace("\\", "/");

                bool existsOnDisk = Directory.Exists(diskPath);
                int diskCount = 0;

                if (existsOnDisk)
                {
                    foreach (string file in Directory.GetFiles(diskPath, "*.*", SearchOption.AllDirectories))
                    {
                        if (!file.EndsWith(".meta")) diskCount++;
                    }
                }

                bool hasManifestEntry = m_ExpectedFiles.ContainsKey(folder);
                int expectedCount = hasManifestEntry ? m_ExpectedFiles[folder].Count : 0;

                int missingCount = 0;
                if (hasManifestEntry)
                {
                    foreach (string expectedFile in m_ExpectedFiles[folder])
                    {
                        if (!AssetPathExists(expectedFile)) missingCount++;
                    }
                }

                FolderState state;
                if (hasManifestEntry && missingCount > 0)
                    state = FolderState.NeedsImport;
                else if (diskCount > 0)
                    state = FolderState.Ready;
                else if (!existsOnDisk)
                    state = FolderState.Missing;
                else
                    state = FolderState.EmptyNoManifest;

                m_FolderStatuses.Add(new FolderStatus
                {
                    path = folder,
                    state = state,
                    diskFileCount = diskCount,
                    expectedFileCount = expectedCount
                });
            }
        }

        // -------------------------------------------------------------------------
        // Import Guidance — per-manifest missing-file list, with Drive URLs
        // -------------------------------------------------------------------------

        private void BuildManifestImportList()
        {
            m_ManifestsNeedingImport.Clear();

            string diskManifestPath = AssetPipelineConfig.GetDiskPath(AssetPipelineConfig.GetManifestFolderAssetPath());
            if (!Directory.Exists(diskManifestPath)) return;

            // Load the URL map once.
            PackageUrlMap urlMap = LoadPackageUrlMap(diskManifestPath);

            // Collect all manifest files, sort newest first by last-write time.
            string[] manifestFiles = Directory.GetFiles(diskManifestPath, "*.json")
                .Where(f => Path.GetFileName(f) != "package_urls.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToArray();

            // Track which asset paths we've already found as present — so we
            // only report the *first* (newest) manifest that mentions a missing file.
            HashSet<string> alreadyPresent = new HashSet<string>();

            foreach (string file in manifestFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var manifest = JsonUtility.FromJson<AssetPipelineManifest>(json);
                    if (manifest?.folders == null) continue;

                    string manifestFileName = Path.GetFileName(file);

                    // Find which files from this manifest are not on disk.
                    var missing = new List<string>();
                    foreach (var folder in manifest.folders)
                    {
                        foreach (string assetPath in folder.files)
                        {
                            if (!alreadyPresent.Contains(assetPath) && !AssetPathExists(assetPath))
                                missing.Add(assetPath);
                        }
                    }

                    // Mark the present ones so older manifests don't re-report them.
                    foreach (var folder in manifest.folders)
                    {
                        foreach (string assetPath in folder.files)
                        {
                            if (AssetPathExists(assetPath))
                                alreadyPresent.Add(assetPath);
                        }
                    }

                    if (missing.Count == 0) continue;

                    // Look up the Drive URL for this manifest.
                    string driveUrl = "";
                    foreach (var entry in urlMap.entries)
                    {
                        if (entry.manifestFileName == manifestFileName)
                        {
                            driveUrl = entry.driveUrl;
                            break;
                        }
                    }

                    m_ManifestsNeedingImport.Add(new ManifestImportInfo
                    {
                        manifestFileName = manifestFileName,
                        exportedBy = manifest.exportedBy ?? "",
                        exportDate = manifest.exportDate ?? "",
                        driveUrl = driveUrl,
                        missingFiles = missing
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AssetPipelineManager] Could not read manifest {file}: {e.Message}");
                }
            }
        }

        private PackageUrlMap LoadPackageUrlMap(string diskManifestPath)
        {
            string urlFilePath = Path.Combine(diskManifestPath, "package_urls.json").Replace('\\', '/');
            if (!File.Exists(urlFilePath)) return new PackageUrlMap();

            try
            {
                string json = File.ReadAllText(urlFilePath);
                return JsonUtility.FromJson<PackageUrlMap>(json) ?? new PackageUrlMap();
            }
            catch
            {
                return new PackageUrlMap();
            }
        }

        // -------------------------------------------------------------------------
        // UI Drawing
        // -------------------------------------------------------------------------

        private void OnGUI()
        {
            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

            DrawHeader();

            DrawFolderOverview();
            EditorGUILayout.Space(10);
            DrawImportSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Asset Pipeline Manager", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Shows which asset folders need importing and provides the Google Drive link for each package.\n\n" +
                "To export a package, use Tools > MVHS > Pre-PR Checklist.",
                MessageType.Info);
            EditorGUILayout.Space(5);
        }

        // ── Folder Overview ────────────────────────────────────────────────────────

        private void DrawFolderOverview()
        {
            EditorGUILayout.LabelField("Folder Status", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("●  Has assets", EditorStyles.miniLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("⬇  Needs import", EditorStyles.miniLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("○  No assets yet", EditorStyles.miniLabel, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            foreach (var status in m_FolderStatuses)
            {
                EditorGUILayout.BeginHorizontal();

                string icon;
                string tooltip;
                string countLabel;

                switch (status.state)
                {
                    case FolderState.Ready:
                        icon = "●";
                        tooltip = "Assets present on disk";
                        countLabel = status.diskFileCount + " file(s)";
                        break;

                    case FolderState.NeedsImport:
                        icon = "⬇";
                        tooltip = "Manifest expects " + status.expectedFileCount + " file(s) — import the asset package";
                        countLabel = "need " + (status.expectedFileCount - status.diskFileCount);
                        break;

                    case FolderState.EmptyNoManifest:
                        icon = "○";
                        tooltip = "No assets here yet and no manifest references it";
                        countLabel = "unused";
                        break;

                    case FolderState.Missing:
                    default:
                        icon = "○";
                        tooltip = "Folder does not exist yet — no one has added assets here";
                        countLabel = "unused";
                        break;
                }

                EditorGUILayout.LabelField(new GUIContent(icon, tooltip), GUILayout.Width(20));
                EditorGUILayout.LabelField(status.path);
                EditorGUILayout.LabelField(countLabel, EditorStyles.miniLabel, GUILayout.Width(80));

                EditorGUILayout.EndHorizontal();
            }

            int needsImportCount = m_FolderStatuses.Count(s => s.state == FolderState.NeedsImport);
            if (needsImportCount > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    needsImportCount + " folder(s) have assets in the manifest but not on disk. " +
                    "Import the asset package(s) below before saving any scenes or prefabs.",
                    MessageType.Warning);
            }
        }

        // ── Import Section ─────────────────────────────────────────────────────────

        private void DrawImportSection()
        {
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);

            if (m_ManifestsNeedingImport.Count == 0)
            {
                bool anyNeedsImport = m_FolderStatuses.Any(s => s.state == FolderState.NeedsImport);
                if (anyNeedsImport)
                {
                    // Folders need import but no manifest URL recorded yet.
                    EditorGUILayout.HelpBox(
                        "Some folders need assets but no Drive URL has been recorded yet.\n" +
                        "Ask your teammate to complete the Pre-PR Checklist so a URL is saved.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("All folders are up to date — nothing to import.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Import the packages below (newest first). After each import, click Refresh to update the list.",
                    MessageType.Info);
                EditorGUILayout.Space(4);

                foreach (var info in m_ManifestsNeedingImport)
                {
                    DrawManifestImportRow(info);
                    EditorGUILayout.Space(6);
                }
            }

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Refresh", GUILayout.Height(28)))
            {
                Refresh();
            }

            if (GUILayout.Button("Import Package from File…", GUILayout.Height(28)))
            {
                ImportPackageFromFile();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawManifestImportRow(ManifestImportInfo info)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header: who exported this and when.
            string header = string.IsNullOrEmpty(info.exportedBy)
                ? info.manifestFileName
                : $"{info.exportedBy}  ({info.exportDate})";
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            EditorGUILayout.LabelField($"Missing: {info.missingFiles.Count} file(s)", EditorStyles.miniLabel);

            // Show first few missing files as a hint.
            int previewCount = Mathf.Min(info.missingFiles.Count, 3);
            for (int i = 0; i < previewCount; i++)
                EditorGUILayout.LabelField("  " + info.missingFiles[i], EditorStyles.miniLabel);
            if (info.missingFiles.Count > previewCount)
                EditorGUILayout.LabelField($"  … and {info.missingFiles.Count - previewCount} more", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            if (!string.IsNullOrEmpty(info.driveUrl))
            {
                // Show URL and Open button side by side.
                EditorGUILayout.BeginHorizontal();

                // Truncate long URLs for display.
                string displayUrl = info.driveUrl.Length > 50
                    ? info.driveUrl.Substring(0, 47) + "..."
                    : info.driveUrl;
                EditorGUILayout.SelectableLabel(displayUrl, EditorStyles.miniLabel, GUILayout.Height(16));

                if (GUILayout.Button("Open Drive", GUILayout.Width(90)))
                {
                    Application.OpenURL(info.driveUrl);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Download the .unitypackage from Drive, then drag it into the Project window or use 'Import Package from File…' below.", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No Drive URL recorded for this manifest.\n" +
                    "Ask the student who exported it to complete the Pre-PR Checklist so the URL is saved.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        // -------------------------------------------------------------------------
        // Import Logic
        // -------------------------------------------------------------------------

        private void ImportPackageFromFile()
        {
            string openPath = EditorUtility.OpenFilePanel(
                "Select Asset Package",
                "",
                "unitypackage");

            if (string.IsNullOrEmpty(openPath)) return;

            AssetDatabase.ImportPackage(openPath, true);
            Debug.Log($"[AssetPipelineManager] Import started from {openPath}");
        }

        // -------------------------------------------------------------------------
        // Utility
        // -------------------------------------------------------------------------

        private void EnsureFolderExists(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
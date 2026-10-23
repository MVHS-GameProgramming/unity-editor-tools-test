using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace MVHS
{
    public class PrePRChecklist : EditorWindow
    {
        [MenuItem("Tools/MVHS/Pre-PR Checklist", false, 10)]
        public static void ShowWindow()
        {
            GetWindow<PrePRChecklist>("Pre-PR Checklist");
        }

        private enum StepStatus { Pending, Pass, Fail }

        // Step statuses
        private StepStatus _scanStatus = StepStatus.Pending;
        private StepStatus _refCheckStatus = StepStatus.Pending;
        private StepStatus _manifestStatus = StepStatus.Pending;
        private StepStatus _exportStatus = StepStatus.Pending;
        private StepStatus _urlStatus = StepStatus.Pending;

        // Step messages
        private string _scanMessage = "";
        private string _refCheckMessage = "";
        private string _manifestMessage = "";
        private string _urlMessage = "";

        // Missing reference details (shown when step 2 fails)
        private List<string> _missingRefDetails = new List<string>();
        private Vector2 _missingRefScroll;

        // Drive URL input
        private string _driveUrl = "";

        // Manifest held in memory until Step 5
        private AssetPipelineManifest _pendingManifest = null;
        private string _pendingManifestFileName = "";

        private ValidatorRules _validatorRules = new ValidatorRules();

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void OnEnable()
        {
            Reset();
        }

        private void Reset()
        {
            _scanStatus = StepStatus.Pending;
            _refCheckStatus = StepStatus.Pending;
            _manifestStatus = StepStatus.Pending;
            _exportStatus = StepStatus.Pending;
            _urlStatus = StepStatus.Pending;
            _scanMessage = "";
            _refCheckMessage = "";
            _manifestMessage = "";
            _urlMessage = "";
            _driveUrl = "";
            _pendingManifest = null;
            _pendingManifestFileName = "";
            _missingRefDetails.Clear();
            _validatorRules = new ValidatorRules();
            _validatorRules.LoadRules();
        }

        // -------------------------------------------------------------------------
        // GUI
        // -------------------------------------------------------------------------

        private void OnGUI()
        {
            GUILayout.Label("Pre-PR Checklist", EditorStyles.boldLabel);
            GUILayout.Space(4);
            GUILayout.Label("Complete each step in order before opening your Pull Request.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(12);

            // ── Step 1: Project Scan ──────────────────────────────────────
            DrawStepHeader("1. Run Project Scan", _scanStatus);
            GUILayout.Label("All assets in Assets/Project/ must be in the correct folder.", EditorStyles.wordWrappedLabel);

            // Warn about unsaved changes
            if (HasUnsavedChanges())
            {
                EditorGUILayout.HelpBox(
                    "⚠ You have unsaved changes. Save your scenes (Ctrl+Shift+S) before running the scan so the results reflect what is on disk.",
                    MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(_scanMessage))
                GUILayout.Label(_scanMessage, EditorStyles.wordWrappedLabel);

            GUI.enabled = true;
            if (GUILayout.Button("Run Project Scan"))
            {
                RunProjectScan();
            }

            GUILayout.Space(12);

            // ── Step 2: Missing Reference Check ──────────────────────────
            GUI.enabled = true;
            DrawStepHeader("2. Check for Missing References", _refCheckStatus);
            GUILayout.Label("All prefabs and scenes must have no missing asset references.", EditorStyles.wordWrappedLabel);

            if (!string.IsNullOrEmpty(_refCheckMessage))
                GUILayout.Label(_refCheckMessage, EditorStyles.wordWrappedLabel);

            // Show broken references list when step fails
            if (_refCheckStatus == StepStatus.Fail && _missingRefDetails.Count > 0)
            {
                _missingRefScroll = EditorGUILayout.BeginScrollView(_missingRefScroll, GUILayout.MaxHeight(80));
                foreach (string line in _missingRefDetails)
                    GUILayout.Label(line, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }

            GUI.enabled = _scanStatus == StepStatus.Pass && _refCheckStatus == StepStatus.Pending;
            if (GUILayout.Button("Check References"))
            {
                RunReferenceCheck();
            }

            GUILayout.Space(12);

            // ── Step 3: Generate Manifest ─────────────────────────────────
            GUI.enabled = true;
            DrawStepHeader("3. Generate Manifest", _manifestStatus);
            GUILayout.Label("Saves all open scenes and assets, then scans Assets/Project/ for binary assets. Manifest is held in memory until Step 5.", EditorStyles.wordWrappedLabel);

            if (!string.IsNullOrEmpty(_manifestMessage))
                GUILayout.Label(_manifestMessage, EditorStyles.wordWrappedLabel);

            GUI.enabled = _refCheckStatus == StepStatus.Pass && _manifestStatus == StepStatus.Pending;
            if (GUILayout.Button("Generate Manifest"))
            {
                RunGenerateManifest();
            }

            GUILayout.Space(12);

            // ── Step 4: Export Package ────────────────────────────────────
            GUI.enabled = true;
            DrawStepHeader("4. Export Package", _exportStatus);
            GUILayout.Label("Exports binary assets only as a .unitypackage file.", EditorStyles.wordWrappedLabel);

            GUI.enabled = _manifestStatus == StepStatus.Pass;
            if (GUILayout.Button("Export Asset Package"))
            {
                string savePath = EditorUtility.SaveFilePanel(
                    "Save Asset Package",
                    "",
                    SuggestPackageName(),
                    "unitypackage");

                if (!string.IsNullOrEmpty(savePath))
                {
                    ExportBinaryAssets(savePath);
                    _exportStatus = StepStatus.Pass;
                }
            }

            GUILayout.Space(12);

            // ── Step 5: Upload to Drive & paste URL ───────────────────────
            GUI.enabled = true;
            DrawStepHeader("5. Upload to Shared Drive", _urlStatus);
            GUILayout.Label("Upload the .unitypackage to the shared Google Drive folder, then paste the share link below.", EditorStyles.wordWrappedLabel);

            if (!string.IsNullOrEmpty(_urlMessage))
                GUILayout.Label(_urlMessage, EditorStyles.wordWrappedLabel);

            GUI.enabled = _exportStatus == StepStatus.Pass;
            EditorGUILayout.BeginHorizontal();
            _driveUrl = EditorGUILayout.TextField(_driveUrl);
            if (GUILayout.Button("Save URL", GUILayout.Width(80)))
            {
                if (string.IsNullOrWhiteSpace(_driveUrl))
                {
                    _urlMessage = "✗ Please paste a Drive URL before saving.";
                    _urlStatus = StepStatus.Fail;
                }
                else if (!AssetPackageURLCheck(_driveUrl, out string errorMessage))
                {
                    _urlMessage = errorMessage;
                    _urlStatus = StepStatus.Fail;
                }
                else
                {
                    WriteManifestToDisk(_pendingManifest, _pendingManifestFileName);
                    SavePackageUrl(_pendingManifestFileName, _driveUrl);
                    _urlStatus = StepStatus.Pass;
                    _urlMessage = "✓ Manifest and URL saved.";
                    AssetDatabase.Refresh();
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            // ── Done ──────────────────────────────────────────────────────
            GUI.enabled = true;
            if (_urlStatus == StepStatus.Pass)
            {
                EditorGUILayout.HelpBox(
                    "✓ All steps complete!\n\n" +
                    "Next steps:\n" +
                    "1. Commit the manifest and package_urls.json\n" +
                    "2. Open your Pull Request on GitHub",
                    MessageType.Info);
            }

            GUI.enabled = true;
        }

        private void DrawStepHeader(string label, StepStatus status)
        {
            string icon;
            if (status == StepStatus.Pass) icon = "✓";
            else if (status == StepStatus.Fail) icon = "✗";
            else icon = "○";
            GUILayout.Label($"{icon}  {label}", EditorStyles.boldLabel);
        }

        // -------------------------------------------------------------------------
        // Unsaved Changes Check
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns true if any open scene has unsaved modifications.
        /// </summary>
        private bool HasUnsavedChanges()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    return true;
            }
            return false;
        }

        // -------------------------------------------------------------------------
        // Step 1 — Project Scan
        // -------------------------------------------------------------------------

        private void RunProjectScan()
        {
            ScanResults results = DependencyScanner.Scan("Assets/Project", _validatorRules);
            if (results.failCount > 0)
            {
                _scanStatus = StepStatus.Fail;
                _scanMessage = $"✗ {results.failCount} asset(s) in the wrong folder. Fix them in the Asset Structure Validator before continuing.";

                // Reset downstream steps
                _refCheckStatus = StepStatus.Pending;
                _refCheckMessage = "";
                _missingRefDetails.Clear();
                _manifestStatus = StepStatus.Pending;
                _manifestMessage = "";
                _exportStatus = StepStatus.Pending;
                _urlStatus = StepStatus.Pending;
                _urlMessage = "";
            }
            else
            {
                _scanStatus = StepStatus.Pass;
                _scanMessage = $"✓ {results.passCount} asset(s) passed. No violations found.";
            }
        }

        // -------------------------------------------------------------------------
        // Step 2 — Missing Reference Check
        // Prefabs are checked independently FIRST so that scene overrides cannot
        // shadow a broken reference that lives in the prefab asset itself.
        // -------------------------------------------------------------------------

        private void RunReferenceCheck()
        {
            _missingRefDetails.Clear();
            var broken = new List<string>();

            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            // ── Pass 1: Prefabs ──────────────────────────────────────────
            // Interrogate each prefab asset directly so that any broken reference
            // in the prefab file is caught even when a scene instance overrides it
            // with a valid sprite (which would otherwise hide the broken prefab).
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Project" });
            int prefabTotal = prefabGuids.Length;

            for (int i = 0; i < prefabTotal; i++)
            {
                string guid = prefabGuids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                EditorUtility.DisplayProgressBar(
                    "Checking References",
                    $"Prefab {i + 1}/{prefabTotal}: {Path.GetFileName(assetPath)}",
                    (float)(i + 1) / (prefabTotal + 1)); // +1 so scene pass starts above 0

                try
                {
                    string[] deps = AssetDatabase.GetDependencies(assetPath, recursive: true);
                    foreach (string dep in deps)
                    {
                        if (dep.StartsWith("Packages/") || dep.StartsWith("Library/")) continue;
                        string fullPath = Path.Combine(projectRoot, dep);
                        if (!File.Exists(fullPath))
                        {
                            string shortAsset = Path.GetFileName(assetPath);
                            string shortDep = Path.GetFileName(dep);
                            broken.Add($"  [Prefab] {shortAsset}  →  missing: {shortDep}  ({dep})");
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PrePRChecklist] Could not check prefab {assetPath}: {e.Message}");
                }
            }

            // ── Pass 2: Scenes ───────────────────────────────────────────
            // Check scene-level references separately. Scenes can reference assets
            // directly (not via a prefab), so we still need to scan them.
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Project" });
            int sceneTotal = sceneGuids.Length;

            for (int i = 0; i < sceneTotal; i++)
            {
                string guid = sceneGuids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                EditorUtility.DisplayProgressBar(
                    "Checking References",
                    $"Scene {i + 1}/{sceneTotal}: {Path.GetFileName(assetPath)}",
                    (float)(prefabTotal + i + 1) / (prefabTotal + sceneTotal));

                try
                {
                    string[] deps = AssetDatabase.GetDependencies(assetPath, recursive: true);
                    foreach (string dep in deps)
                    {
                        if (dep.StartsWith("Packages/") || dep.StartsWith("Library/")) continue;
                        string fullPath = Path.Combine(projectRoot, dep);
                        if (!File.Exists(fullPath))
                        {
                            string shortAsset = Path.GetFileName(assetPath);
                            string shortDep = Path.GetFileName(dep);
                            // Only add if not already reported by the prefab pass
                            string entry = $"  [Scene]  {shortAsset}  →  missing: {shortDep}  ({dep})";
                            if (!broken.Any(b => b.Contains(shortDep) && b.Contains("[Prefab]")))
                                broken.Add(entry);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PrePRChecklist] Could not check scene {assetPath}: {e.Message}");
                }
            }

            EditorUtility.ClearProgressBar();

            int checkedTotal = prefabTotal + sceneTotal;

            if (broken.Count > 0)
            {
                _refCheckStatus = StepStatus.Fail;
                _refCheckMessage = $"✗ {broken.Count} missing reference(s) found. Fix these before continuing:";
                _missingRefDetails = broken;

                // Reset downstream steps
                _manifestStatus = StepStatus.Pending;
                _manifestMessage = "";
                _exportStatus = StepStatus.Pending;
                _urlStatus = StepStatus.Pending;
                _urlMessage = "";
            }
            else
            {
                _refCheckStatus = StepStatus.Pass;
                _refCheckMessage = $"✓ All references valid — {prefabTotal} prefab(s) and {sceneTotal} scene(s) checked.";
                _missingRefDetails.Clear();
            }
        }

        // -------------------------------------------------------------------------
        // Step 3 — Save + Generate Manifest
        // -------------------------------------------------------------------------

        private void RunGenerateManifest()
        {
            // Save all open scenes and assets before snapshotting binary files.
            // This ensures the manifest reflects what is actually on disk, not
            // unsaved in-memory state.
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            int count = BuildManifestInMemory(out _pendingManifest, out _pendingManifestFileName);
            _manifestStatus = StepStatus.Pass;
            _manifestMessage = $"✓ Saved all scenes. Found {count} binary asset(s). Manifest ready — not saved to disk yet.";
        }

        private int BuildManifestInMemory(out AssetPipelineManifest manifest, out string manifestFileName)
        {
            string[] allGuids = AssetDatabase.FindAssets("", new[] { "Assets/Project" });
            Dictionary<string, List<string>> byFolder = new Dictionary<string, List<string>>();

            foreach (string guid in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.IndexOf("Assets/Project/Dev/", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string ext = Path.GetExtension(path).ToLower();
                bool isBinary = false;
                foreach (string binaryExt in AssetPipelineConfig.binaryExtensions)
                {
                    if (ext == binaryExt) { isBinary = true; break; }
                }
                if (!isBinary) continue;

                string folder = Path.GetDirectoryName(path).Replace('\\', '/') + "/";
                if (!byFolder.ContainsKey(folder))
                    byFolder[folder] = new List<string>();
                byFolder[folder].Add(path);
            }

            var manifestFolders = new List<ManifestFolder>();
            int totalCount = 0;
            foreach (var kvp in byFolder)
            {
                manifestFolders.Add(new ManifestFolder { folderPath = kvp.Key, files = kvp.Value });
                totalCount += kvp.Value.Count;
            }

            string safeName = System.Environment.UserName.Replace(' ', '_');
            manifestFileName = $"manifest_{safeName}.json";

            manifest = new AssetPipelineManifest
            {
                exportedBy = System.Environment.UserName,
                exportDate = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                folders = manifestFolders
            };

            return totalCount;
        }

        // -------------------------------------------------------------------------
        // Step 5 — Write to Disk
        // -------------------------------------------------------------------------

        private void WriteManifestToDisk(AssetPipelineManifest manifest, string manifestFileName)
        {
            string manifestFolderAssetPath = AssetPipelineConfig.GetManifestFolderAssetPath();
            string manifestDiskPath = AssetPipelineConfig.GetDiskPath(manifestFolderAssetPath);
            Directory.CreateDirectory(manifestDiskPath);

            string fullPath = Path.Combine(manifestDiskPath, manifestFileName).Replace('\\', '/');
            File.WriteAllText(fullPath, JsonUtility.ToJson(manifest, prettyPrint: true));

            Debug.Log($"[PrePRChecklist] Manifest written: {manifestFileName}");
        }

        private void SavePackageUrl(string manifestFileName, string url)
        {
            string manifestFolderAssetPath = AssetPipelineConfig.GetManifestFolderAssetPath();
            string manifestDiskPath = AssetPipelineConfig.GetDiskPath(manifestFolderAssetPath);
            string urlFilePath = Path.Combine(manifestDiskPath, "package_urls.json").Replace('\\', '/');

            PackageUrlMap urlMap = new PackageUrlMap();
            if (File.Exists(urlFilePath))
            {
                try
                {
                    string existing = File.ReadAllText(urlFilePath);
                    urlMap = JsonUtility.FromJson<PackageUrlMap>(existing) ?? new PackageUrlMap();
                }
                catch
                {
                    urlMap = new PackageUrlMap();
                }
            }

            bool found = false;
            for (int i = 0; i < urlMap.entries.Count; i++)
            {
                if (urlMap.entries[i].manifestFileName == manifestFileName)
                {
                    urlMap.entries[i].driveUrl = url;
                    found = true;
                    break;
                }
            }
            if (!found)
                urlMap.entries.Add(new PackageUrlEntry { manifestFileName = manifestFileName, driveUrl = url });

            File.WriteAllText(urlFilePath, JsonUtility.ToJson(urlMap, prettyPrint: true));

            Debug.Log($"[PrePRChecklist] Package URL saved for {manifestFileName}");
        }

        // -------------------------------------------------------------------------
        // Step 4 — Export
        // -------------------------------------------------------------------------

        private void ExportBinaryAssets(string savePath)
        {
            string[] allGuids = AssetDatabase.FindAssets("", new[] { "Assets/Project" });
            List<string> toExport = new List<string>();

            foreach (string guid in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.IndexOf("Assets/Project/Dev/", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string ext = Path.GetExtension(path).ToLower();
                foreach (string binaryExt in AssetPipelineConfig.binaryExtensions)
                {
                    if (ext == binaryExt) { toExport.Add(path); break; }
                }
            }

            AssetDatabase.ExportPackage(toExport.ToArray(), savePath, ExportPackageOptions.Default);
            Debug.Log($"[PrePRChecklist] Exported {toExport.Count} binary asset(s) to {savePath}");
        }

        private string SuggestPackageName()
        {
            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return $"{System.Environment.UserName}_{timestamp}";
        }

        // -------------------------------------------------------------------------
        // URL Validation
        // -------------------------------------------------------------------------

        private bool AssetPackageURLCheck(string url, out string errorMessage)
        {
            AssetPackageURLCheckMode mode = AssetPipelineConfig.assetPackageURLCheckMode;

            if (mode == AssetPackageURLCheckMode.HTTPS)
            {
                bool pass = url.StartsWith("https://");
                errorMessage = pass ? string.Empty : "✗ URL should start with https://";
                return pass;
            }
            else if (mode == AssetPackageURLCheckMode.GoogleDrive)
            {
                url = url.Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    errorMessage = "✗ URL not valid format";
                    return false;
                }
                if (uri.Scheme != Uri.UriSchemeHttps)
                {
                    errorMessage = "✗ URL not valid format";
                    return false;
                }
                if (uri.Host != "drive.google.com")
                {
                    errorMessage = "✗ URL must be to Google Drive";
                    return false;
                }
                if (!uri.AbsolutePath.Contains("/d/"))
                {
                    errorMessage = "✗ URL not valid format";
                    return false;
                }
                errorMessage = string.Empty;
                return true;
            }
            else
            {
                errorMessage = string.Empty;
                return true;
            }
        }
    }

    [System.Serializable]
    public class PackageUrlEntry
    {
        public string manifestFileName;
        public string driveUrl;
    }

    [System.Serializable]
    public class PackageUrlMap
    {
        public List<PackageUrlEntry> entries = new List<PackageUrlEntry>();
    }
}
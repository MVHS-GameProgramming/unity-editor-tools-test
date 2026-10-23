using MVHS;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;


[Serializable]
public struct SaveFileParams
{
    public string systemUserName;
    public string saveTime;
}


public static class AssetPipelineUtils
{

    public static SaveFileParams getSaveFileParams()
    {
        string name = System.Environment.UserName.Replace(' ', '_');
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return new SaveFileParams { systemUserName = name, saveTime = timestamp };
    }

    public static int GenerateManifest(SaveFileParams saveFileParams)
    {

        string[] allGuids = AssetDatabase.FindAssets("", new[] { "Assets/Project" });

        // Group binary assets by folder, matching AssetPipelineManifest structure
        Dictionary<string, List<string>> byFolder = new Dictionary<string, List<string>>();

        foreach (string guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue;
            if (path.Contains("Assets/Project/Dev/", System.StringComparison.OrdinalIgnoreCase)) continue;

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

        // Build manifest folders list
        var manifestFolders = new List<ManifestFolder>();
        int totalCount = 0;
        foreach (var kvp in byFolder)
        {
            manifestFolders.Add(new ManifestFolder { folderPath = kvp.Key, files = kvp.Value });
            totalCount += kvp.Value.Count;
        }

        // Build and serialize the manifest
        var manifest = new AssetPipelineManifest
        {
            exportedBy = saveFileParams.systemUserName,
            exportDate = saveFileParams.saveTime,
            folders = manifestFolders
        };

        // Ensure manifest folder exists
        string manifestFolderAssetPath = AssetPipelineConfig.GetManifestFolderAssetPath();
        string manifestDiskPath = AssetPipelineConfig.GetDiskPath(manifestFolderAssetPath);
        Directory.CreateDirectory(manifestDiskPath);

        // Write as JSON with same naming convention as AssetPipelineManager
        string fileName = $"manifest_{saveFileParams.systemUserName}_{saveFileParams.saveTime}.json";
        string fullPath = Path.Combine(manifestDiskPath, fileName).Replace('\\', '/');

        string json = JsonUtility.ToJson(manifest, prettyPrint: true);
        File.WriteAllText(fullPath, json);

        return totalCount;
    }

    public static void ExportBinaryAssets(string savePath)
    {

        string[] allGuids = AssetDatabase.FindAssets("", new[] { "Assets/Project" });
        List<string> toExport = new List<string>();

        foreach (string guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue;
            if (path.Contains("Assets/Project/Dev/", System.StringComparison.OrdinalIgnoreCase)) continue;

            string ext = Path.GetExtension(path).ToLower();
            foreach (string binaryExt in AssetPipelineConfig.binaryExtensions)
            {
                if (ext == binaryExt)
                {
                    toExport.Add(path);
                    break;
                }
            }
        }

        AssetDatabase.ExportPackage(
            toExport.ToArray(),
            savePath,
            ExportPackageOptions.Default);

        Debug.Log($"[PrePRChecklist] Exported {toExport.Count} binary asset(s) to {savePath}");
    }
}

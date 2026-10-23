using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MVHS
{
    /// <summary>Validation status of a single scanned asset.</summary>
    public enum AssetStatus
    {
        Pass,       // path satisfies its rule
        Fail,       // path violates its rule
        NoRule,     // no rule covers this extension — neutral
    }

    /// <summary>One row in the dependency results list.</summary>
    public class DependencyResult
    {
        public string assetPath;      // e.g. "Assets/_ImportedAssets/Pack/sound.wav"
        public string extension;      // e.g. ".wav"
        public AssetStatus status;
        public string requiredFolder; // populated when status == Fail, for the hint
        public bool moved;          // true after a successful migration this session
    }

    public class ScanResults
    {
        public int passCount;
        public int failCount;
        public int noRuleCount;
        public List<DependencyResult> dependencyResults = new List<DependencyResult>();

        public bool HasScanned
        {
            get
            {
                return passCount > 0 || failCount > 0 || noRuleCount > 0 || dependencyResults.Count > 0;
            }
        }

        public void Clear()
        {
            passCount = 0;
            failCount = 0;
            noRuleCount = 0;
            dependencyResults.Clear();
        }
    }

    public static class DependencyScanner
    {
        public static ScanResults Scan(ValidatorRules validatorRules)
        {
            List<GameObject> allRootGameObjects = new List<GameObject>();
            HashSet<string> allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                allPaths.Add(scene.path);
                GameObject[] rootGameObjects = scene.GetRootGameObjects();
                foreach (GameObject gameObject in rootGameObjects)
                {
                    allRootGameObjects.Add(gameObject);
                }
            }

            
            foreach (GameObject go in allRootGameObjects)
            {
                List<string> assetPaths = GetAllAssetDependencyPaths(go);
                foreach (string assetPath in assetPaths)
                {
                    allPaths.Add(assetPath);
                }
            }

            List<string> projectAssetPaths = allPaths.ToList();
            return GenerateScanResults(projectAssetPaths, validatorRules);
        }


        public static ScanResults Scan(GameObject go, ValidatorRules validatorRules)
        {
            if (go == null)
            {
                Debug.LogWarning("[DependencyScanner] No GameObject selected.");
                return new ScanResults();
            }

            List<string> projectAssetPaths = GetAllAssetDependencyPaths(go);
            return GenerateScanResults(projectAssetPaths, validatorRules);
            
        }

        public static ScanResults Scan(String rootFolder, ValidatorRules validatorRules)
        {
            // Get all asset paths under the root folder, excluding Dev
            string[] allAssetGuids = AssetDatabase.FindAssets("", new[] { rootFolder });

            HashSet<string> allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in allAssetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (assetPath.Contains("Assets/Project/Dev/", StringComparison.OrdinalIgnoreCase))
                    continue; // skip Dev assets as direct scan targets

                allPaths.Add(assetPath);

                // Dependencies of non-Dev assets are always included, even if they're in Dev
                string[] dependencies = AssetDatabase.GetDependencies(assetPath, recursive: true);
                foreach (string dep in dependencies)
                {
                    if (dep.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        allPaths.Add(dep); // no Dev skip here
                }
            }

            return GenerateScanResults(allPaths.ToList(), validatorRules);
        }

        public static ScanResults GenerateScanResults(List<string> projectAssetPaths, ValidatorRules validatorRules)
        {
            if (projectAssetPaths.Count == 0)
            {
                return new ScanResults();
            }


            ScanResults results = new ScanResults();

            foreach (string assetPath in projectAssetPaths)
            {
                AssetStatus assetStatus;

                string assetExtension = Path.GetExtension(assetPath).ToLower();

                // TODO: Make fix in MVHS Unity Editor Tools
                // Only skip Dev scenes, not Dev assets referenced by real prefabs
                if (assetPath.Contains("Assets/Project/Dev/", StringComparison.OrdinalIgnoreCase)
                    && assetExtension == ".unity")
                {
                    continue;
                }

                // Skip folders theselves
                if (AssetDatabase.IsValidFolder(assetPath))
                    continue;

                string requiredFolder = "";
                ValidateResult validateResult = validatorRules.Validate(assetPath);
                if (validateResult == ValidateResult.Pass)
                {
                    results.passCount++;
                    assetStatus = AssetStatus.Pass;
                }
                else if (validateResult == ValidateResult.Fail)
                {
                    results.failCount++;
                    requiredFolder = validatorRules.GetRequiredFolder(assetPath);
                    assetStatus = AssetStatus.Fail;
                }
                else
                {
                    results.noRuleCount++;
                    assetStatus = AssetStatus.NoRule;
                }




                DependencyResult dependencyResult = new DependencyResult
                {
                    assetPath = assetPath,
                    extension = string.IsNullOrEmpty(assetExtension) ? "(none)" : assetExtension,
                    status = assetStatus,
                    requiredFolder = requiredFolder,
                };
                results.dependencyResults.Add(dependencyResult);
            }

            // Sort: failures first
            results.dependencyResults.Sort((a, b) => b.status.CompareTo(a.status));

            Debug.Log($"[AssetValidator] Scan Complete {projectAssetPaths.Count} dependencies, {results.failCount} violation(s).");

            return results;
        }


        public static List<string> GetAllAssetDependencyPaths(GameObject go)
        {
            // Get the prefab asset path for this GameObject
            // Works for both prefab instances in the scene and assets in the Project.
            string rootPath = AssetDatabase.GetAssetPath(go);

            // If it's a scene instance, grab the prefab source instead.
            if (string.IsNullOrEmpty(rootPath))
            {
                GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabSource != null)
                {
                    rootPath = AssetDatabase.GetAssetPath(prefabSource);
                }
            }

            // Collect asset paths to scan — start with rootPath if we have one,
            // then walk all components on the GameObject and its children to
            // pull in any directly-referenced assets (materials, audio clips, etc.)
            HashSet<string> pathsToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(rootPath))
            {
                pathsToScan.Add(rootPath);
            }

            GetComponentAssetPaths(go, pathsToScan);

            // Recursively expand via AssetDatabase.GetDependencies
            // GetDependencies is the key Unity API: given a set of asset paths it
            // returns every asset those assets reference, recursively (textures
            // inside materials, rigs inside FBX, etc.).
            HashSet<string> allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pathsToScan.Count > 0)
            {
                string[] expanded = AssetDatabase.GetDependencies(
                    new List<string>(pathsToScan).ToArray(),
                    recursive: true);

                foreach (string path in expanded)
                {
                    allPaths.Add(path);
                }
            }

            // Also add the seed (root) paths in case GetDependencies didn't include them
            foreach (string path in pathsToScan)
            {
                allPaths.Add(path);
            }

            // Filter out all UNity built in packages.
            // Only get assets that are in the project's Assets folder
            List<string> projectAssetPaths = new List<string>();
            foreach (string path in allPaths)
            {
                if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    projectAssetPaths.Add(path);
                }
            }

            return projectAssetPaths;
        }


        public static void GetComponentAssetPaths(GameObject root, HashSet<string> paths)
        {
            Component[] components = root.GetComponentsInChildren<Component>(includeInactive: true);
            foreach (Component comp in components)
            {
                if (comp == null) continue; // missing script guard

                SerializedObject serializedObject = new SerializedObject(comp);
                SerializedProperty prop = serializedObject.GetIterator();

                // Recursively look at all properties
                while (prop.NextVisible(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.objectReferenceValue == null) continue;

                    string path = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                }
            }
        }
    }
}

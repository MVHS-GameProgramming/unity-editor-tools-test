using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


namespace MVHS
{
    /// <summary>
    /// One rule: a file extension must be found somewhere under a required folder.
    /// e.g.  extension=".wav"  requiredFolder="Audio"
    /// Passes when the asset path contains the requiredFolder string (case-insensitive).
    /// Edit AssetValidatorRules.json directly to change rules — do not modify here.
    /// </summary>
    [Serializable]
    public class FolderRule
    {
        public string extension = ".ext";
        public string requiredFolder = "Folder";
        public string description = "";      // optional note shown in the Rules tab
    }

    /// <summary>Wrapper so JsonUtility can deserialise a top-level list.</summary>
    [Serializable]
    public class RuleSet
    {
        public List<FolderRule> rules = new List<FolderRule>();
    }

    public enum ValidateResult
    {
        Pass,
        Fail,
        NoRule
    }

    public class ValidatorRules
    {

        // Rules loaded once from JSON — read-only at runtime
        private List<FolderRule> m_Rules = new List<FolderRule>();

        // Where the rules JSON lives — commit this file with your project
        public const string RulesPath = "Assets/Editor/MVHS/AssetStructureValidator/AssetStructureValidatorRules.json";

        // Total number of rules loaded
        public int RulesCount { get { return m_Rules.Count; } }

        /// <summary>
        /// Loads rules from AssetValidatorRules.json.
        /// If the file is missing, logs a clear warning — no silent fallback.
        /// Students should never need to call this; it runs automatically on open.
        /// </summary>
        public void LoadRules()
        {
            m_Rules.Clear();

            if (!File.Exists(RulesPath))
            {
                Debug.LogWarning(
                    $"[AssetValidator] Rules file not found at {RulesPath}. " +
                    "Add the file to your project to enable validation.");
                return;
            }

            try
            {
                string json = File.ReadAllText(RulesPath);
                var ruleSet = JsonUtility.FromJson<RuleSet>(json);
                if (ruleSet?.rules != null)
                    m_Rules.AddRange(ruleSet.rules);

                Debug.Log($"[AssetValidator] Loaded {m_Rules.Count} rules from {RulesPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AssetValidator] Could not parse {RulesPath}: {e.Message}");
            }
        }

        /// <summary>Returns all loaded rules.</summary>
        public IReadOnlyList<FolderRule> GetActiveRules()
        {
            return m_Rules.AsReadOnly();
        }

        /// <summary>
        /// Checks a single asset path against loaded rules.
        /// Returns null  → no rule covers this extension (no opinion).
        /// Returns true  → path satisfies its rule.
        /// Returns false → path violates its rule.
        /// </summary>
        public ValidateResult Validate(string assetPath)
        {
            if (assetPath == null)
            {
                return ValidateResult.NoRule;
            }


            string path = Path.GetDirectoryName(assetPath);
            if (path == null || path == string.Empty)
            {
                return ValidateResult.NoRule;
            }

            string assetRequiredFolder = GetRequiredFolder(assetPath);
            if (string.IsNullOrEmpty(assetRequiredFolder))
            {
                return ValidateResult.NoRule;
            }

            // Get a list of the asset directories in order
            string[] folders = path.Split(
                new[] { '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries
                );


            List<string> assetPathDirectories = new List<string>();
            foreach (string folder in folders)
            {
                assetPathDirectories.Add(folder.ToLower());
            }

            // 
            int assetsDirectoryDepth = GetDirectoryDepth(assetPathDirectories, "assets");
            int projectDirectoryDepth = GetDirectoryDepth(assetPathDirectories, "project");
            if (assetsDirectoryDepth < 0 || projectDirectoryDepth < 0 || assetsDirectoryDepth >= projectDirectoryDepth)
            {
                return ValidateResult.Fail;
            }

            int requiredFolderDirectoryDepth = GetDirectoryDepth(assetPathDirectories, assetRequiredFolder);
            return (requiredFolderDirectoryDepth > 0 && requiredFolderDirectoryDepth > projectDirectoryDepth) ? ValidateResult.Pass : ValidateResult.Fail;

        }

        public static int GetDirectoryDepth(List<string> assetDirectories, string directory)
        {
            if (assetDirectories == null || assetDirectories.Count == 0 || directory == null || directory == string.Empty)
            {
                return -1;
            }

            for (int i = 0; i < assetDirectories.Count; i++)
            {
                if (string.Equals(assetDirectories[i], directory.ToLower(), StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Get the required folder for an asset based of its extension and predefined Rule
        /// Returns empty string if required folder is not found
        /// </summary>
        public string GetRequiredFolder(string assetPath)
        {
            if (assetPath == null)
            {
                return string.Empty;
            }

            string assetExtension = Path.GetExtension(assetPath).ToLower();
            foreach (FolderRule rule in m_Rules)
            {
                if (string.Equals(rule.extension, assetExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.requiredFolder;
                }
            }
            return string.Empty;
        }

    }
}

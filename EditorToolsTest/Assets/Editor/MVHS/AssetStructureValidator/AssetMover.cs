using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MVHS
{

    public static class AssetMover
    {
        public static bool MoveAsset(string assetPath, string requiredFolder, ValidatorRules validatorRules)
        {
            string fileName = Path.GetFileName(assetPath);

            string targetDirectory = $"Assets/Project/{requiredFolder}";

            // Ensure targetDirectory exists
            if (!AssetDatabase.IsValidFolder(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                AssetDatabase.Refresh();
            }

            string absolutePathStart = Path.GetFullPath(targetDirectory);
            string chosenAbsolutePath = EditorUtility.OpenFolderPanel(
                title: $"Choose destination for  {fileName}  (must be inside  {requiredFolder}/)",
                folder: absolutePathStart,
                defaultName: ""
            );

            // Asset movement canceled
            if (string.IsNullOrEmpty(chosenAbsolutePath))
            {
                return false;
            }

            // Convert absolute path the unity relative path
            string projectRootPath = Path.GetFullPath(Application.dataPath + "/../");
            string chosenRelativePath = chosenAbsolutePath.Replace('\\', '/');
            string rootNormalisedPath = projectRootPath.Replace('\\', '/').TrimEnd('/') + "/";

            if (!chosenRelativePath.StartsWith(rootNormalisedPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    title: "Invalid Destination",
                    message: $"The chosen folder is outside the Unity project:\n\n{chosenAbsolutePath}\n\n Please choose a folder inside the Assets/ folder.",
                    ok: "OK"
                );
                return false;
            }

            string relativeFolder = "Assets/" + chosenRelativePath.Substring(rootNormalisedPath.Length).TrimStart('/');

            // Strip leading "Assets/" duplication if the user happened to navigate into the Assets folder itself
            if (relativeFolder.StartsWith("Assets/Assets", StringComparison.OrdinalIgnoreCase))
            {
                relativeFolder = relativeFolder.Substring("Assets/".Length);
            }

            string testPath = $"{relativeFolder}/{fileName}";
            ValidateResult result = validatorRules.Validate(testPath);
            if (result == ValidateResult.Fail)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    title: "Destination Warning",
                    message: $"The chosen folder:\n{relativeFolder}\n\n does not contain \"{requiredFolder} as required by the rule for {Path.GetExtension(testPath)}\n\nAre you sure you want to move",
                    ok: "Move Anyway",
                    cancel: "Cancel"
                );
                if (!proceed)
                {
                    return false;
                }
            }

            // Resolve name collision
            string destination = ResolveNameCollision(testPath);

            // Show final confirmation
            bool confirm = EditorUtility.DisplayDialog(
                title: "Confirm Move",
                message: $"\n{assetPath}\n\nTo:\n{destination}\n\nUnity will update all references automatically.",
                ok: "Move",
                cancel: "Cancel"
                );

            if (!confirm)
            {
                return false;
            }

            string error = AssetDatabase.MoveAsset(assetPath, destination);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog(
                    title: "Move Failed",
                    message: $"Unity cound not move {fileName}:\n\n{error}\n\nCheck that no other process has the file open.",
                    ok: "OK"
                );
                return false;
            }

            return true;

        }

        public static string ResolveNameCollision(string targetPath)
        {
            if (!File.Exists(targetPath))
            {
                return targetPath;
            }

            string directory = Path.GetDirectoryName(targetPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(targetPath);
            string extension = Path.GetExtension(targetPath);

            int i = 1;
            string candidate;
            do
            {
                candidate = $"{directory}/{name}_{i++}{extension}";
            }
            while (File.Exists(candidate));

            return candidate;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;


namespace MVHS
{
    public static class ProjectDirectoryCreator
    {
        /// <summary>
        /// Create the default MVHS project directory. Only missing files will be created.
        /// </summary>
        [MenuItem("Tools/MVHS/Utilities/Project Directory Creator", false, 100)]
        public static void CreateProjectDirectory()
        {

            List<string> createdFolders = new List<string>();
            List<string> projectFolders = AssetPipelineConfig.projectFolders.ToList();
            projectFolders.Add("Assets/Project/Dev");

            foreach (string folder in projectFolders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                EnsureFolderExists(folder);
                createdFolders.Add(folder);
            }
            AssetDatabase.Refresh();

            // Display number of folders, and folder names that were created
            if (createdFolders.Count > 0)
            {
                string newFolderCreateList = "";
                foreach (string folder in createdFolders)
                {
                    newFolderCreateList += $"{folder}\n";
                }

                EditorUtility.DisplayDialog(
                    title: "Project Directory Creator",
                    message: $"Created {createdFolders.Count} project directories(s)\n\n" + newFolderCreateList,
                    ok: "OK"
                    );
            }
            else
            {
                EditorUtility.DisplayDialog(
                    title: "Project Directory Creator",
                    message: $"All required project directories already exist",
                    ok: "OK"
                    );
            }
        }

        /// <summary>
        /// Ensure all folders, and parent folders are created and valid. Folders, and their parents
        /// will be created if they do not exist.
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            // Unity needs directories to be split by "/"
            folderPath = folderPath.Replace("\\", "/").TrimEnd('/');

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            string current = parts[0]; // "Assets"

            // Create all parent folders as needed
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}

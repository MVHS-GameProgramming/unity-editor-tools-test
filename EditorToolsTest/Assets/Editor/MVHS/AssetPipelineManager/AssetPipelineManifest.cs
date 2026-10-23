using System;
using System.Collections.Generic;

namespace MVHS
{
    /// <summary>
    /// Data structure for an export manifest file.
    ///
    /// Each time a student exports a .unitypackage, one of these is serialized
    /// to JSON and saved as a new file in the manifest folder. Multiple manifests
    /// can coexist without merge conflicts because each is a separate file.
    ///
    /// On startup, all manifests are loaded and combined to determine which
    /// assets should be present in the project.
    /// </summary>
    [Serializable]
    public class AssetPipelineManifest
    {
        /// <summary>
        /// Display name of the person who exported. Used in the filename
        /// and shown in the Package Manager UI for context.
        /// </summary>
        public string exportedBy;

        /// <summary>
        /// ISO 8601 timestamp of when the export occurred.
        /// </summary>
        public string exportDate;

        /// <summary>
        /// List of folders that contained assets at the time of export.
        /// Only folders with actual files are included.
        /// </summary>
        public List<ManifestFolder> folders = new List<ManifestFolder>();
    }

    [Serializable]
    public class ManifestFolder
    {
        /// <summary>
        /// Folder path relative to Assets/ (e.g., "Art/Models").
        /// </summary>
        public string folderPath;

        /// <summary>
        /// Every asset file in this folder at the time of export.
        /// Paths are relative to Assets/.
        /// </summary>
        public List<string> files = new List<string>();
    }

}
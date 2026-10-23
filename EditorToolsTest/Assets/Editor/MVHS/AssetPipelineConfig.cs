
using System.IO;
using UnityEngine;

namespace MVHS
{
    public enum AssetPackageURLCheckMode
    {
        HTTPS,
        GoogleDrive,
        None
    }

    public static class AssetPipelineConfig
    {

        public static AssetPackageURLCheckMode assetPackageURLCheckMode = AssetPackageURLCheckMode.GoogleDrive;

        public static string[] projectFolders =
        {
            "Assets/Project/",

            "Assets/Project/Art/",
            "Assets/Project/Art/Animations/",
            "Assets/Project/Art/Animators/",
            "Assets/Project/Art/Materials/",
            "Assets/Project/Art/Models/",
            "Assets/Project/Art/Shaders/",
            "Assets/Project/Art/Sprites/",
            "Assets/Project/Art/Textures/",
            "Assets/Project/Art/VFX/",

            "Assets/Project/Audio/",
            "Assets/Project/Audio/Music/",
            "Assets/Project/Audio/SFX/",

            "Assets/Project/PhysicsMaterials/",
            "Assets/Project/Prefabs/",
            "Assets/Project/Scenes/",
            "Assets/Project/ScriptableObjects/",
            "Assets/Project/Scripts/",
            "Assets/Project/UI/"
        };

        public static string[] binaryExtensions =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".bmp", ".exr", ".hdr",
            ".tiff", ".tif",
            ".wav", ".mp3",
            ".ogg", ".aiff", ".aif",
            ".fbx", ".obj", ".blend", ".dae", ".3ds",
            ".ttf", ".otf",
            ".mp4", ".mov", ".avi", ".webm"
        };
        
        public static string GetManifestFolderAssetPath()
        {
            return "Assets/Editor/MVHS/AssetPipelineManager/AssetManifests";
        }

        public static string GetDiskPath(string folder)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string diskPath = Path.Combine(projectRoot, folder);
            diskPath = diskPath.Replace("\\", "/");
            return diskPath;
        }
    }
}

using System.IO;
using UnityEngine;
using UnityEditor;

namespace GalaxyGourd.IconGen
{
    internal static class IconOutput
    {
        /// <summary>
        /// Writes the texture as a PNG to the settings' output directory. Returns the written path
        /// (project-relative when inside Assets). Optionally configures the imported asset as a Sprite.
        /// </summary>
        public static string Save(Texture2D texture, IconGenSettings s, GameObject prefab, out string error)
        {
            error = null;

            string name = string.IsNullOrWhiteSpace(s.fileName) ? prefab.name : s.fileName;
            name = SanitizeFileName(name);
            if (!name.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                name += ".png";

            string dir = s.outputDirectory;
            string fullDir = ToAbsolute(dir);
            try
            {
                Directory.CreateDirectory(fullDir);
            }
            catch (System.Exception e)
            {
                error = $"Could not create directory '{fullDir}': {e.Message}";
                return null;
            }

            string fullPath = Path.Combine(fullDir, name);
            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(fullPath, png);

            string projectRelative = ToProjectRelative(fullPath);
            bool insideAssets = projectRelative != null && projectRelative.StartsWith("Assets");

            if (insideAssets)
            {
                AssetDatabase.ImportAsset(projectRelative, ImportAssetOptions.ForceUpdate);
                if (s.configureAsSprite)
                    ConfigureSprite(projectRelative);
                AssetDatabase.Refresh();
                return projectRelative;
            }

            return fullPath;
        }

        private static void ConfigureSprite(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string ToAbsolute(string dir)
        {
            if (Path.IsPathRooted(dir)) return dir;
            // project-relative (e.g. "Assets/Icons") => combine with the folder above Assets
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, dir);
        }

        private static string ToProjectRelative(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot))
                return normalized.Substring(projectRoot.Length).TrimStart('/');
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageSampleManifestParser
    {
        public static IReadOnlyList<PackageExtraDefinition> ParseSamples(string packageJson)
        {
            if (string.IsNullOrWhiteSpace(packageJson))
            {
                return Array.Empty<PackageExtraDefinition>();
            }

            PackageJsonManifest manifest = JsonUtility.FromJson<PackageJsonManifest>(packageJson);

            if (manifest == null || manifest.samples == null || manifest.samples.Length == 0)
            {
                return Array.Empty<PackageExtraDefinition>();
            }

            List<PackageExtraDefinition> samples = new List<PackageExtraDefinition>();
            HashSet<string> seenSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageJsonSampleEntry sample in manifest.samples.Where(sample => sample != null))
            {
                string samplePath = NormalizeAssetPath(sample.path);
                string displayName = (sample.displayName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = GetDisplayNameFromPath(samplePath);
                }

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(samplePath)
                    ? "name:" + displayName
                    : "path:" + samplePath;

                if (!seenSamples.Add(key))
                {
                    continue;
                }

                samples.Add(new PackageExtraDefinition(
                    displayName,
                    sample.description,
                    displayName,
                    samplePath));
            }

            return samples.ToArray();
        }

        private static string GetDisplayNameFromPath(string samplePath)
        {
            string normalizedPath = NormalizeAssetPath(samplePath);

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return string.Empty;
            }

            return Path.GetFileName(normalizedPath.TrimEnd('/')) ?? string.Empty;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        }

        [Serializable]
        private sealed class PackageJsonManifest
        {
            public PackageJsonSampleEntry[] samples;
        }

        [Serializable]
        private sealed class PackageJsonSampleEntry
        {
            public string displayName;
            public string description;
            public string path;
        }
    }
}

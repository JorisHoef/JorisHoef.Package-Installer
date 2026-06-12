using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageSampleDiscoveryService
    {
        private const string LogPrefix = "[Deucarian Package Installer]";

        private static readonly IReadOnlyList<PackageExtraDefinition> EmptySamples =
            Array.Empty<PackageExtraDefinition>();

        private readonly Dictionary<string, IReadOnlyList<PackageExtraDefinition>> _sampleCache =
            new Dictionary<string, IReadOnlyList<PackageExtraDefinition>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<PackageExtraDefinition> GetSamples(PackageManagerPackageInfo packageInfo)
        {
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                return EmptySamples;
            }

            string packageRootPath = GetPackageRootPath(packageInfo.resolvedPath);

            if (string.IsNullOrWhiteSpace(packageRootPath))
            {
                return EmptySamples;
            }

            string packageJsonPath = Path.Combine(packageRootPath, "package.json");
            string cacheKey = GetCacheKey(packageInfo, packageJsonPath);

            if (_sampleCache.TryGetValue(cacheKey, out IReadOnlyList<PackageExtraDefinition> cachedSamples))
            {
                return cachedSamples;
            }

            IReadOnlyList<PackageExtraDefinition> samples = ReadSamples(packageInfo.name, packageJsonPath);
            _sampleCache[cacheKey] = samples;
            return samples;
        }

        public void ClearCache()
        {
            _sampleCache.Clear();
        }

        private static IReadOnlyList<PackageExtraDefinition> ReadSamples(string packageName, string packageJsonPath)
        {
            if (string.IsNullOrWhiteSpace(packageJsonPath) || !File.Exists(packageJsonPath))
            {
                return EmptySamples;
            }

            try
            {
                string packageJson = File.ReadAllText(packageJsonPath);
                return PackageSampleManifestParser.ParseSamples(packageJson);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    LogPrefix +
                    " Failed to read package samples for " +
                    (string.IsNullOrWhiteSpace(packageName) ? packageJsonPath : packageName) +
                    ": " +
                    exception.GetBaseException().Message);
                return EmptySamples;
            }
        }

        private static string GetCacheKey(PackageManagerPackageInfo packageInfo, string packageJsonPath)
        {
            DateTime lastWriteTime = File.Exists(packageJsonPath)
                ? File.GetLastWriteTimeUtc(packageJsonPath)
                : DateTime.MinValue;

            return (packageInfo.name ?? string.Empty) +
                   "|" +
                   (packageInfo.version ?? string.Empty) +
                   "|" +
                   packageJsonPath +
                   "|" +
                   lastWriteTime.Ticks;
        }

        private static string GetPackageRootPath(string resolvedPath)
        {
            string packagePath = resolvedPath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return string.Empty;
            }

            if (!Path.IsPathRooted(packagePath))
            {
                packagePath = Path.Combine(GetProjectRootPath(), packagePath);
            }

            return Path.GetFullPath(packagePath);
        }

        private static string GetProjectRootPath()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            return projectRoot != null ? projectRoot.FullName : Application.dataPath;
        }
    }
}

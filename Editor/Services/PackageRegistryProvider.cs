using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryProvider
    {
        private const string LogPrefix = "[Deucarian Package Installer]";

        private static readonly PackageRegistryLoader Loader = new PackageRegistryLoader();
        private static readonly IReadOnlyList<PackageDefinition> EmptyPackages =
            Array.Empty<PackageDefinition>();

        private static PackageRegistryLoadResult _currentLoadResult;
        private static IReadOnlyList<PackageDefinition> _allPackages = EmptyPackages;
        private static Task<PackageRegistryLoadResult> _remoteRefreshTask;
        private static bool _bundledLoaded;
        private static bool _remoteRefreshStarted;

        public static event Action RegistryChanged;

        public static IReadOnlyList<PackageDefinition> All
        {
            get
            {
                EnsureLoaded();
                return _allPackages;
            }
        }

        public static IReadOnlyList<PackageDefinition> StandalonePackages =>
            All.Where(package => !package.IsBridge).ToArray();

        public static IReadOnlyList<PackageDefinition> BridgePackages =>
            GetPackagesByCategory("Bridge");

        public static IReadOnlyList<string> Categories =>
            All.Select(package => package.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetCategorySortIndex)
                .ThenBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static PackageRegistryLoadResult CurrentLoadResult
        {
            get
            {
                EnsureLoaded();
                return _currentLoadResult;
            }
        }

        public static bool IsRemoteRefreshing => _remoteRefreshTask != null;

        public static string StatusMessage
        {
            get
            {
                PackageRegistryLoadResult result = CurrentLoadResult;
                return result != null ? result.StatusMessage : "Using bundled registry";
            }
        }

        public static void EnsureLoaded()
        {
            if (!_bundledLoaded)
            {
                ApplyLoadResult(Loader.LoadBundled(), logFailures: true);
                _bundledLoaded = true;
            }

            if (!_remoteRefreshStarted)
            {
                StartRemoteRefresh();
            }
        }

        public static IReadOnlyList<PackageDefinition> GetPackagesByCategory(string category)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(category))
            {
                return EmptyPackages;
            }

            return _allPackages
                .Where(package => string.Equals(
                    package.Category,
                    category,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public static bool TryGetPackage(string packageId, out PackageDefinition packageDefinition)
        {
            EnsureLoaded();

            packageDefinition = _allPackages.FirstOrDefault(definition =>
                string.Equals(definition.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

            return packageDefinition != null;
        }

        public static IEnumerable<PackageDefinition> GetInstallableDependencies(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                yield break;
            }

            foreach (string dependencyId in packageDefinition.Dependencies)
            {
                if (TryGetPackage(dependencyId, out PackageDefinition dependency) && dependency.HasPackageReference)
                {
                    yield return dependency;
                }
            }
        }

        internal static IReadOnlyList<PackageDefinition> CreatePackageDefinitions(PackageRegistry registry)
        {
            if (registry == null || registry.packages == null)
            {
                return EmptyPackages;
            }

            return registry.packages
                .Where(entry => entry != null)
                .Select(CreatePackageDefinition)
                .ToArray();
        }

        private static PackageDefinition CreatePackageDefinition(PackageRegistryEntry entry)
        {
            string category = entry.category != null ? entry.category.Trim() : string.Empty;

            return new PackageDefinition(
                entry.displayName,
                entry.id,
                entry.stableUrl,
                entry.description,
                entry.dependencies,
                ParsePackageType(category),
                entry.developmentUrl,
                category: category);
        }

        private static PackageType ParsePackageType(string category)
        {
            if (string.Equals(category, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return PackageType.UI;
            }

            if (string.Equals(category, "Bridge", StringComparison.OrdinalIgnoreCase))
            {
                return PackageType.Bridge;
            }

            return PackageType.Core;
        }

        private static void StartRemoteRefresh()
        {
            _remoteRefreshStarted = true;
            _remoteRefreshTask = Loader.LoadRemoteAsync(_currentLoadResult != null
                ? _currentLoadResult.Registry
                : null);

            EditorApplication.update -= UpdateRemoteRefresh;
            EditorApplication.update += UpdateRemoteRefresh;
        }

        private static void UpdateRemoteRefresh()
        {
            if (_remoteRefreshTask == null || !_remoteRefreshTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= UpdateRemoteRefresh;

            PackageRegistryLoadResult result;

            try
            {
                result = _remoteRefreshTask.Result;
            }
            catch (Exception exception)
            {
                result = PackageRegistryLoadResult.RemoteFailureUsingBundled(
                    _currentLoadResult != null ? _currentLoadResult.Registry : null,
                    exception.GetBaseException().Message);
            }

            _remoteRefreshTask = null;
            ApplyLoadResult(result, logFailures: true);
        }

        private static void ApplyLoadResult(PackageRegistryLoadResult result, bool logFailures)
        {
            if (result == null)
            {
                return;
            }

            _currentLoadResult = result;

            if (result.IsValid && result.Registry != null)
            {
                _allPackages = CreatePackageDefinitions(result.Registry);
            }
            else if (_allPackages == null)
            {
                _allPackages = EmptyPackages;
            }

            if (!result.IsValid && logFailures)
            {
                Debug.LogWarning(LogPrefix + " Registry load failed: " + result.ErrorMessage);
            }
            else if (result.Source == PackageRegistrySource.RemoteFailedUsingBundled && logFailures)
            {
                Debug.LogWarning(LogPrefix + " Remote registry failed, using bundled registry: " + result.ErrorMessage);
            }

            RegistryChanged?.Invoke();
        }

        private static int GetCategorySortIndex(string category)
        {
            if (string.Equals(category, "Core", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(category, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(category, "Bridge", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(category, "Suites", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return 5;
        }
    }
}

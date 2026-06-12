using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageDependencyInstaller
    {
        private const string LogPrefix = "[Deucarian Package Installer]";

        private readonly PackageInstallService _packageInstallService;
        private readonly PackageDetectionService _packageDetectionService;

        public PackageDependencyInstaller(
            PackageInstallService packageInstallService,
            PackageDetectionService packageDetectionService)
        {
            _packageInstallService = packageInstallService ?? throw new ArgumentNullException(nameof(packageInstallService));
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
        }

        public void InstallWithDependencies(
            PackageDefinition packageDefinition,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (packageDefinition == null)
            {
                Debug.LogError(LogPrefix + " Cannot install a null package definition.");
                return;
            }

            PackageDefinition[] installPlan = CreateInstallPlan(packageDefinition);

            if (installPlan.Length == 0)
            {
                Debug.Log(LogPrefix + " " + packageDefinition.DisplayName + " and its dependencies are already installed.");
                return;
            }

            _packageInstallService.InstallMany(
                installPlan,
                channelSelector,
                "Install " + packageDefinition.DisplayName);
        }

        public void InstallAll(Func<PackageDefinition, PackageChannel> channelSelector)
        {
            PackageDefinition[] installPlan = CreateInstallPlan(PackageRegistryProvider.All);

            if (installPlan.Length == 0)
            {
                Debug.Log(LogPrefix + " All registered packages are already installed.");
                return;
            }

            _packageInstallService.InstallMany(
                installPlan,
                channelSelector,
                "Install All Packages");
        }

        public PackageDefinition[] CreateInstallPlan(PackageDefinition packageDefinition)
        {
            return CreateInstallPlan(new[] { packageDefinition });
        }

        public PackageDefinition[] CreateInstallPlan(IEnumerable<PackageDefinition> packageDefinitions)
        {
            List<PackageDefinition> installPlan = new List<PackageDefinition>();
            HashSet<string> visitedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> plannedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageDefinition packageDefinition in packageDefinitions ?? Array.Empty<PackageDefinition>())
            {
                AddPackageToPlan(packageDefinition, installPlan, visitedPackageIds, plannedPackageIds);
            }

            return installPlan.ToArray();
        }

        public bool AreDependenciesInstalled(PackageDefinition packageDefinition)
        {
            return packageDefinition == null ||
                   packageDefinition.Dependencies.All(_packageDetectionService.IsInstalled);
        }

        public PackageDefinition[] GetMissingDependencies(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return Array.Empty<PackageDefinition>();
            }

            return packageDefinition.Dependencies
                .Where(packageId => !_packageDetectionService.IsInstalled(packageId))
                .Select(packageId =>
                    PackageRegistryProvider.TryGetPackage(packageId, out PackageDefinition dependency)
                        ? dependency
                        : null)
                .Where(dependency => dependency != null)
                .ToArray();
        }

        public PackageDefinition[] GetInstalledDependents(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return Array.Empty<PackageDefinition>();
            }

            return PackageRegistryProvider.All
                .Where(candidate => candidate != null &&
                                    !string.Equals(
                                        candidate.PackageId,
                                        packageDefinition.PackageId,
                                        StringComparison.OrdinalIgnoreCase) &&
                                    candidate.Dependencies.Any(dependencyId =>
                                        string.Equals(
                                            dependencyId,
                                            packageDefinition.PackageId,
                                            StringComparison.OrdinalIgnoreCase)) &&
                                    _packageDetectionService.IsInstalled(candidate.PackageId))
                .ToArray();
        }

        private void AddPackageToPlan(
            PackageDefinition packageDefinition,
            ICollection<PackageDefinition> installPlan,
            ISet<string> visitedPackageIds,
            ISet<string> plannedPackageIds)
        {
            if (packageDefinition == null ||
                string.IsNullOrWhiteSpace(packageDefinition.PackageId) ||
                visitedPackageIds.Contains(packageDefinition.PackageId))
            {
                return;
            }

            visitedPackageIds.Add(packageDefinition.PackageId);

            foreach (PackageDefinition dependency in PackageRegistryProvider.GetInstallableDependencies(packageDefinition))
            {
                AddPackageToPlan(dependency, installPlan, visitedPackageIds, plannedPackageIds);
            }

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId) ||
                !packageDefinition.HasPackageReference ||
                plannedPackageIds.Contains(packageDefinition.PackageId))
            {
                return;
            }

            installPlan.Add(packageDefinition);
            plannedPackageIds.Add(packageDefinition.PackageId);
        }
    }
}

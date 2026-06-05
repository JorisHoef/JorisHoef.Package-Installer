using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class IntegrationInstaller : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        private readonly PackageInstallService _packageInstallService;
        private readonly PackageDetectionService _packageDetectionService;
        private readonly ScriptingDefineService _scriptingDefineService;
        private readonly List<PackageDefinition> _pendingIntegrations = new List<PackageDefinition>();

        public IntegrationInstaller(
            PackageInstallService packageInstallService,
            PackageDetectionService packageDetectionService,
            ScriptingDefineService scriptingDefineService)
        {
            _packageInstallService = packageInstallService ?? throw new ArgumentNullException(nameof(packageInstallService));
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _scriptingDefineService = scriptingDefineService ?? throw new ArgumentNullException(nameof(scriptingDefineService));

            _packageInstallService.QueueCompleted += RefreshInstalledPackages;
            _packageDetectionService.RefreshCompleted += CompletePendingIntegrations;
        }

        public void Dispose()
        {
            _packageInstallService.QueueCompleted -= RefreshInstalledPackages;
            _packageDetectionService.RefreshCompleted -= CompletePendingIntegrations;
        }

        public void InstallPackage(PackageDefinition packageDefinition)
        {
            InstallPackage(packageDefinition, PackageChannel.Stable);
        }

        public void InstallPackage(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (!packageDefinition.HasPackageReference)
            {
                Debug.LogWarning(LogPrefix + " " + packageDefinition.DisplayName + " is not a standalone installable package.");
                return;
            }

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                Debug.Log(LogPrefix + " " + packageDefinition.DisplayName + " is already installed.");
                return;
            }

            _packageInstallService.Install(packageDefinition, channel);
        }

        public void InstallIntegration(PackageDefinition integrationDefinition)
        {
            InstallIntegration(integrationDefinition, null);
        }

        public void InstallIntegration(PackageDefinition integrationDefinition, Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (integrationDefinition == null)
            {
                return;
            }

            if (ArePackageDependenciesInstalled(integrationDefinition))
            {
                EnableIntegrationSymbols(integrationDefinition);
                return;
            }

            QueueIntegrationUntilDependenciesAreDetected(integrationDefinition);
            PackageDefinition[] missingDependencies = GetMissingInstallableDependencies(integrationDefinition);

            if (missingDependencies.Length == 0 && !_packageInstallService.IsBusy)
            {
                RemovePendingIntegration(integrationDefinition);
                Debug.LogWarning(LogPrefix + " Cannot enable " + integrationDefinition.DisplayName +
                                 " because its dependencies are not installed or installable from PackageRegistry.");
                return;
            }

            _packageInstallService.InstallMany(missingDependencies, channelSelector);

            if (!_packageInstallService.IsBusy)
            {
                RefreshInstalledPackages();
            }

            Debug.Log(LogPrefix + " Waiting to enable " + integrationDefinition.DisplayName +
                      " until required packages are installed and detected.");
        }

        public void InstallAll()
        {
            InstallAll(null);
        }

        public void InstallAll(Func<PackageDefinition, PackageChannel> channelSelector)
        {
            PackageDefinition[] missingPackages = PackageRegistry.StandalonePackages
                .Where(package => !_packageDetectionService.IsInstalled(package.PackageId))
                .ToArray();

            if (missingPackages.Length == 0)
            {
                foreach (PackageDefinition integration in PackageRegistry.Integrations)
                {
                    if (ArePackageDependenciesInstalled(integration))
                    {
                        EnableIntegrationSymbols(integration);
                    }
                    else
                    {
                        QueueIntegrationUntilDependenciesAreDetected(integration);
                    }
                }

                if (_pendingIntegrations.Count > 0)
                {
                    RefreshInstalledPackages();
                }

                Debug.Log(LogPrefix + " Processed Install All.");
                return;
            }

            foreach (PackageDefinition integration in PackageRegistry.Integrations)
            {
                if (!IsIntegrationComplete(integration))
                {
                    QueueIntegrationUntilDependenciesAreDetected(integration);
                }
            }

            _packageInstallService.InstallMany(missingPackages, channelSelector);

            if (!_packageInstallService.IsBusy)
            {
                RefreshInstalledPackages();
            }

            Debug.Log(LogPrefix + " Waiting to enable integrations until required packages are installed and detected.");
        }

        public bool ArePackageDependenciesInstalled(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return integrationDefinition.Dependencies.All(_packageDetectionService.IsInstalled);
        }

        public bool AreIntegrationSymbolsEnabled(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return _scriptingDefineService.HasSymbols(
                _scriptingDefineService.SelectedBuildTargetGroup,
                integrationDefinition.ScriptingDefineSymbols);
        }

        public bool IsIntegrationComplete(PackageDefinition integrationDefinition)
        {
            return ArePackageDependenciesInstalled(integrationDefinition) && AreIntegrationSymbolsEnabled(integrationDefinition);
        }

        public bool HasPendingIntegration(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return _pendingIntegrations.Any(pendingIntegration =>
                string.Equals(pendingIntegration.PackageId, integrationDefinition.PackageId, StringComparison.OrdinalIgnoreCase));
        }

        private PackageDefinition[] GetMissingInstallableDependencies(PackageDefinition integrationDefinition)
        {
            return PackageRegistry
                .GetInstallableDependencies(integrationDefinition)
                .Where(dependency => !_packageDetectionService.IsInstalled(dependency.PackageId))
                .ToArray();
        }

        private void QueueIntegrationUntilDependenciesAreDetected(PackageDefinition integrationDefinition)
        {
            if (HasPendingIntegration(integrationDefinition))
            {
                return;
            }

            _pendingIntegrations.Add(integrationDefinition);
        }

        private void RemovePendingIntegration(PackageDefinition integrationDefinition)
        {
            _pendingIntegrations.RemoveAll(pendingIntegration =>
                string.Equals(pendingIntegration.PackageId, integrationDefinition.PackageId, StringComparison.OrdinalIgnoreCase));
        }

        private void CompletePendingIntegrations()
        {
            if (_pendingIntegrations.Count == 0)
            {
                return;
            }

            foreach (PackageDefinition pendingIntegration in _pendingIntegrations.ToArray())
            {
                if (!ArePackageDependenciesInstalled(pendingIntegration))
                {
                    Debug.LogWarning(LogPrefix + " Cannot enable " + pendingIntegration.DisplayName +
                                     " yet because one or more required packages are still not installed.");
                    continue;
                }

                EnableIntegrationSymbols(pendingIntegration);
                RemovePendingIntegration(pendingIntegration);
            }
        }

        private void EnableIntegrationSymbols(PackageDefinition integrationDefinition)
        {
            if (!ArePackageDependenciesInstalled(integrationDefinition))
            {
                Debug.LogWarning(LogPrefix + " Cannot enable " + integrationDefinition.DisplayName +
                                 " because one or more required packages are not installed.");
                return;
            }

            _scriptingDefineService.AddSymbolsToSelectedBuildTargetGroup(integrationDefinition.ScriptingDefineSymbols);
            Debug.Log(LogPrefix + " Processed integration " + integrationDefinition.DisplayName + ".");
        }

        private void RefreshInstalledPackages()
        {
            _packageDetectionService.Refresh();
        }
    }
}

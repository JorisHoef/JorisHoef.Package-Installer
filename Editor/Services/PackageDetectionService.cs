using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageDetectionService : IDisposable
    {
        private const string LogPrefix = "[Deucarian Package Installer]";

        private readonly Dictionary<string, PackageManagerPackageInfo> _installedPackages =
            new Dictionary<string, PackageManagerPackageInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _installedPackageReferences =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;

        private ListRequest _listRequest;
        private bool _refreshRetryScheduled;

        public PackageDetectionService()
        {
            _packageLockPaths = GetPackageLockPaths();
        }

        public event Action StateChanged;

        public event Action RefreshCompleted;

        public bool IsRefreshing => _listRequest != null && !_listRequest.IsCompleted;

        public void Refresh()
        {
            if (IsRefreshing)
            {
                return;
            }

            try
            {
                _listRequest = Client.List(true, true);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                NotifyStateChanged();
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Failed to start installed-package refresh: " + exception.Message);
                _listRequest = null;
                ScheduleRefreshRetry();
                NotifyStateChanged();
            }
        }

        public bool IsInstalled(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _installedPackages.ContainsKey(packageId);
        }

        internal void ReplaceInstalledPackageNamesForTests(IEnumerable<string> packageIds)
        {
            _installedPackages.Clear();

            if (packageIds == null)
            {
                return;
            }

            foreach (string packageId in packageIds)
            {
                if (!string.IsNullOrWhiteSpace(packageId))
                {
                    _installedPackages[packageId.Trim()] = null;
                }
            }
        }

        public bool TryGetInstalledPackage(string packageId, out PackageManagerPackageInfo packageInfo)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageInfo = null;
                return false;
            }

            return _installedPackages.TryGetValue(packageId, out packageInfo);
        }

        public bool TryGetInstalledPackageReference(string packageId, out string packageReference)
        {
            packageReference = string.Empty;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            return _installedPackageReferences.TryGetValue(packageId, out packageReference) &&
                   !string.IsNullOrWhiteSpace(packageReference);
        }

        public bool TryGetInstalledPackageChannel(
            PackageDefinition packageDefinition,
            out PackageChannel channel,
            out string packageReference)
        {
            channel = PackageChannel.Stable;
            packageReference = string.Empty;

            if (packageDefinition == null ||
                !TryGetInstalledPackageReference(packageDefinition.PackageId, out packageReference))
            {
                return false;
            }

            if (TryGetReferenceName(packageReference, out string installedReferenceName))
            {
                if (string.Equals(installedReferenceName, "develop", StringComparison.OrdinalIgnoreCase) &&
                    ReferenceMatchesChannel(packageReference, packageDefinition.DevelopmentUrl))
                {
                    channel = PackageChannel.Development;
                    return true;
                }

                if (string.Equals(installedReferenceName, "main", StringComparison.OrdinalIgnoreCase) &&
                    ReferenceMatchesChannel(packageReference, packageDefinition.StableUrl))
                {
                    channel = PackageChannel.Stable;
                    return true;
                }
            }

            if (ReferenceMatchesChannel(packageReference, packageDefinition.DevelopmentUrl))
            {
                channel = PackageChannel.Development;
                return true;
            }

            if (ReferenceMatchesChannel(packageReference, packageDefinition.StableUrl))
            {
                channel = PackageChannel.Stable;
                return true;
            }

            channel = PackageChannel.Custom;
            return true;
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
            EditorApplication.delayCall -= RetryRefresh;
        }

        private void Update()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
            {
                return;
            }

            if (_listRequest.Status == StatusCode.Success)
            {
                _installedPackages.Clear();
                _installedPackageReferences.Clear();

                foreach (PackageManagerPackageInfo packageInfo in _listRequest.Result)
                {
                    if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.name))
                    {
                        _installedPackages[packageInfo.name] = packageInfo;

                        if (TryReadPackageLockReference(packageInfo.name, out string packageReference) ||
                            TryExtractReferenceFromPackageManagerPackageId(packageInfo.packageId, packageInfo.name, out packageReference))
                        {
                            _installedPackageReferences[packageInfo.name] = packageReference;
                        }
                    }
                }
            }
            else
            {
                string errorMessage = _listRequest.Error != null
                    ? _listRequest.Error.message
                    : "Package Manager returned an unknown error.";

                Debug.LogError(LogPrefix + " Failed to refresh installed-package state: " + errorMessage);
            }

            _listRequest = null;
            EditorApplication.update -= Update;
            NotifyStateChanged();
            RefreshCompleted?.Invoke();
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private void ScheduleRefreshRetry()
        {
            if (_refreshRetryScheduled)
            {
                return;
            }

            _refreshRetryScheduled = true;
            EditorApplication.delayCall += RetryRefresh;
        }

        private void RetryRefresh()
        {
            EditorApplication.delayCall -= RetryRefresh;
            _refreshRetryScheduled = false;
            Refresh();
        }

        private bool TryReadPackageLockReference(string packageId, out string packageReference)
        {
            packageReference = string.Empty;

            foreach (string packageLockPath in _packageLockPaths)
            {
                if (TryReadPackageLockReference(packageLockPath, packageId, out packageReference))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadPackageLockReference(
            string packageLockPath,
            string packageId,
            out string packageReference)
        {
            packageReference = string.Empty;

            if (string.IsNullOrWhiteSpace(packageLockPath) || !File.Exists(packageLockPath))
            {
                return false;
            }

            string lockJson = File.ReadAllText(packageLockPath);
            Match packageMatch = Regex.Match(
                lockJson,
                "\"" + Regex.Escape(packageId) + "\"\\s*:\\s*\\{(?<body>.*?)\\n\\s*\\}",
                RegexOptions.Singleline);

            if (!packageMatch.Success)
            {
                return false;
            }

            return TryReadJsonStringField(packageMatch.Groups["body"].Value, "version", out packageReference);
        }

        private static bool TryReadJsonStringField(string jsonBody, string fieldName, out string value)
        {
            value = string.Empty;
            Match match = Regex.Match(
                jsonBody,
                "\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*\"(?<value>[^\"]+)\"",
                RegexOptions.Singleline);

            if (!match.Success)
            {
                return false;
            }

            value = match.Groups["value"].Value.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryExtractReferenceFromPackageManagerPackageId(
            string packageManagerPackageId,
            string packageId,
            out string packageReference)
        {
            packageReference = string.Empty;

            if (string.IsNullOrWhiteSpace(packageManagerPackageId) ||
                string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            string prefix = packageId + "@";

            if (!packageManagerPackageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            packageReference = packageManagerPackageId.Substring(prefix.Length).Trim();
            return !string.IsNullOrWhiteSpace(packageReference);
        }

        private static bool ReferenceMatchesChannel(string installedReference, string channelUrl)
        {
            if (string.IsNullOrWhiteSpace(installedReference) || string.IsNullOrWhiteSpace(channelUrl))
            {
                return false;
            }

            if (string.Equals(
                    NormalizePackageReference(installedReference),
                    NormalizePackageReference(channelUrl),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return TryGetReferenceName(installedReference, out string installedReferenceName) &&
                   TryGetReferenceName(channelUrl, out string channelReferenceName) &&
                   string.Equals(installedReferenceName, channelReferenceName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetReferenceName(string packageReference, out string referenceName)
        {
            referenceName = string.Empty;

            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            int hashIndex = packageReference.LastIndexOf('#');

            if (hashIndex < 0 || hashIndex == packageReference.Length - 1)
            {
                return false;
            }

            referenceName = packageReference.Substring(hashIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(referenceName);
        }

        private static string NormalizePackageReference(string packageReference)
        {
            return (packageReference ?? string.Empty).Trim();
        }

        private static IReadOnlyList<string> GetPackageLockPaths()
        {
            string projectRoot = Directory.GetParent(Application.dataPath) != null
                ? Directory.GetParent(Application.dataPath).FullName
                : Application.dataPath;

            string packagesDirectory = Path.Combine(projectRoot, "Packages");

            return new[]
            {
                Path.Combine(packagesDirectory, "packages-lock.json"),
                Path.Combine(packagesDirectory, "package-lock.json")
            };
        }
    }
}

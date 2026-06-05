using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class PackageUpdateCheckService : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";
        private const int GitTimeoutMilliseconds = 15000;

        private static readonly Regex ShaRegex =
            new Regex("(?<![0-9a-fA-F])([0-9a-fA-F]{40})(?![0-9a-fA-F])", RegexOptions.Compiled);

        private readonly PackageDetectionService _packageDetectionService;
        private readonly Dictionary<string, PackageUpdateStatus> _statuses =
            new Dictionary<string, PackageUpdateStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;

        private Task<PackageUpdateStatus[]> _checkTask;
        private IReadOnlyList<UpdateCheckItem> _activeCheckItems = Array.Empty<UpdateCheckItem>();

        public PackageUpdateCheckService(PackageDetectionService packageDetectionService)
        {
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _packageLockPaths = GetPackageLockPaths();
        }

        public event Action StateChanged;

        public bool IsChecking => _checkTask != null;

        public bool HasStatuses => _statuses.Count > 0;

        public void CheckForUpdates(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (IsChecking)
            {
                UnityEngine.Debug.Log(LogPrefix + " Update check is already running.");
                return;
            }

            List<UpdateCheckItem> checkItems = new List<UpdateCheckItem>();

            foreach (PackageDefinition packageDefinition in GetInstallablePackages(packageDefinitions))
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;
                string selectedUrl = packageDefinition.GetUrl(channel);

                if (!_packageDetectionService.TryGetInstalledPackage(
                        packageDefinition.PackageId,
                        out PackageManagerPackageInfo packageInfo))
                {
                    _statuses[packageDefinition.PackageId] =
                        PackageUpdateStatus.NotInstalled(packageDefinition, channel, selectedUrl);
                    continue;
                }

                _statuses[packageDefinition.PackageId] =
                    PackageUpdateStatus.Checking(packageDefinition, channel, selectedUrl);

                checkItems.Add(new UpdateCheckItem(
                    packageDefinition,
                    channel,
                    selectedUrl,
                    packageInfo.packageId,
                    packageInfo.resolvedPath,
                    _packageLockPaths));
            }

            NotifyStateChanged();

            if (checkItems.Count == 0)
            {
                UnityEngine.Debug.Log(LogPrefix + " No installed registry packages found for update checking.");
                return;
            }

            _activeCheckItems = checkItems;
            _checkTask = Task.Run(() => checkItems.Select(CheckItem).ToArray());

            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        public PackageUpdateStatus GetStatus(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return PackageUpdateStatus.Unknown(null, channel);
            }

            string selectedUrl = packageDefinition.GetUrl(channel);

            if (_statuses.TryGetValue(packageDefinition.PackageId, out PackageUpdateStatus status) &&
                status.Channel == channel &&
                string.Equals(status.SelectedUrl, selectedUrl, StringComparison.Ordinal))
            {
                return status;
            }

            if (!_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                return PackageUpdateStatus.NotInstalled(packageDefinition, channel, selectedUrl);
            }

            return PackageUpdateStatus.Unknown(packageDefinition, channel);
        }

        public IEnumerable<PackageDefinition> GetPackagesWithUpdates(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            foreach (PackageDefinition packageDefinition in GetInstallablePackages(packageDefinitions))
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;

                if (GetStatus(packageDefinition, channel).IsUpdateAvailable)
                {
                    yield return packageDefinition;
                }
            }
        }

        public void Invalidate(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            if (_statuses.Remove(packageId))
            {
                NotifyStateChanged();
            }
        }

        public void InvalidateAll()
        {
            if (_statuses.Count == 0)
            {
                return;
            }

            _statuses.Clear();
            NotifyStateChanged();
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
        }

        private static IEnumerable<PackageDefinition> GetInstallablePackages(IEnumerable<PackageDefinition> packageDefinitions)
        {
            if (packageDefinitions == null)
            {
                yield break;
            }

            foreach (PackageDefinition packageDefinition in packageDefinitions)
            {
                if (packageDefinition != null && packageDefinition.HasPackageReference)
                {
                    yield return packageDefinition;
                }
            }
        }

        private static PackageUpdateStatus CheckItem(UpdateCheckItem item)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.SelectedUrl))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        "Selected channel has no package URL.");
                }

                if (!TryParseGitPackageReference(
                        item.SelectedUrl,
                        out string remoteUrl,
                        out string reference,
                        out string parseMessage))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        parseMessage);
                }

                if (!TryGetInstalledRevision(item, out string installedRevision))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        "Installed Git revision is unknown. Refresh package detection or reinstall the package from a Git URL.");
                }

                if (!TryGetRemoteRevision(remoteUrl, reference, out string latestRevision, out string remoteMessage))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        installedRevision,
                        remoteMessage);
                }

                if (RevisionsMatch(installedRevision, latestRevision))
                {
                    return PackageUpdateStatus.UpToDate(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        installedRevision,
                        latestRevision);
                }

                return PackageUpdateStatus.UpdateAvailable(
                    item.PackageDefinition,
                    item.Channel,
                    item.SelectedUrl,
                    installedRevision,
                    latestRevision);
            }
            catch (Exception exception)
            {
                return PackageUpdateStatus.Failed(
                    item.PackageDefinition,
                    item.Channel,
                    item.SelectedUrl,
                    string.Empty,
                    "Update check failed: " + exception.Message);
            }
        }

        private void Update()
        {
            if (_checkTask == null || !_checkTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= Update;

            PackageUpdateStatus[] results;

            try
            {
                results = _checkTask.Result;
            }
            catch (Exception exception)
            {
                results = _activeCheckItems
                    .Select(item => PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        "Update check failed: " + exception.GetBaseException().Message))
                    .ToArray();
            }

            foreach (PackageUpdateStatus status in results)
            {
                _statuses[status.PackageId] = status;
                LogStatus(status);
            }

            _checkTask = null;
            _activeCheckItems = Array.Empty<UpdateCheckItem>();
            NotifyStateChanged();
        }

        private static bool TryParseGitPackageReference(
            string packageReference,
            out string remoteUrl,
            out string reference,
            out string message)
        {
            remoteUrl = string.Empty;
            reference = string.Empty;
            message = string.Empty;

            int hashIndex = packageReference.LastIndexOf('#');

            if (hashIndex < 0 || hashIndex == packageReference.Length - 1)
            {
                message = "Selected package reference is not a Git URL with a branch, tag, or revision.";
                return false;
            }

            string urlWithoutReference = packageReference.Substring(0, hashIndex).Trim();
            reference = packageReference.Substring(hashIndex + 1).Trim();

            int pathIndex = urlWithoutReference.IndexOf("?path=", StringComparison.OrdinalIgnoreCase);
            remoteUrl = pathIndex >= 0
                ? urlWithoutReference.Substring(0, pathIndex)
                : urlWithoutReference;

            if (remoteUrl.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                remoteUrl = remoteUrl.Substring(4);
            }

            if (string.IsNullOrWhiteSpace(remoteUrl) || !LooksLikeGitUrl(remoteUrl))
            {
                message = "Selected package reference is not a supported Git URL.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                message = "Selected Git URL has no branch, tag, or revision.";
                return false;
            }

            return true;
        }

        private static bool LooksLikeGitUrl(string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                return false;
            }

            return remoteUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                   remoteUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
                   remoteUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetRemoteRevision(
            string remoteUrl,
            string reference,
            out string revision,
            out string message)
        {
            revision = string.Empty;
            message = string.Empty;

            if (IsRevision(reference))
            {
                revision = reference;
                return true;
            }

            string arguments = "ls-remote " + QuoteArgument(remoteUrl) + " " + QuoteArgument(reference);

            if (!RunGit(arguments, out string output, out string error))
            {
                message = error;
                return false;
            }

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0 && IsRevision(parts[0]))
                {
                    revision = parts[0];
                    return true;
                }
            }

            message = "Selected Git reference could not be found on the remote.";
            return false;
        }

        private static bool RunGit(string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;

            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                try
                {
                    process.Start();
                }
                catch (Win32Exception)
                {
                    error = "Git executable was not found on PATH.";
                    return false;
                }

                if (!process.WaitForExit(GitTimeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    error = "git ls-remote timed out.";
                    return false;
                }

                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    return true;
                }

                error = string.IsNullOrWhiteSpace(error)
                    ? "git ls-remote failed with exit code " + process.ExitCode + "."
                    : "git ls-remote failed: " + error.Trim();

                return false;
            }
        }

        private static bool TryGetInstalledRevision(UpdateCheckItem item, out string revision)
        {
            if (TryExtractRevision(item.PackageManagerPackageId, out revision))
            {
                return true;
            }

            foreach (string packageLockPath in item.PackageLockPaths)
            {
                if (TryReadPackageLockRevision(packageLockPath, item.PackageDefinition.PackageId, out revision))
                {
                    return true;
                }
            }

            return TryReadGitHeadRevision(item.ResolvedPath, out revision);
        }

        private static bool TryReadPackageLockRevision(string packageLockPath, string packageId, out string revision)
        {
            revision = string.Empty;

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

            string body = packageMatch.Groups["body"].Value;

            return TryReadJsonField(body, "hash", out revision) ||
                   TryExtractRevision(body, out revision);
        }

        private static bool TryReadJsonField(string jsonBody, string fieldName, out string value)
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
            return IsRevision(value);
        }

        private static bool TryReadGitHeadRevision(string resolvedPath, out string revision)
        {
            revision = string.Empty;

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return false;
            }

            string gitPath = Path.Combine(resolvedPath, ".git");

            if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
            {
                return false;
            }

            string gitDirectory = gitPath;

            if (File.Exists(gitPath))
            {
                string gitFile = File.ReadAllText(gitPath).Trim();

                if (!gitFile.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string relativeGitDirectory = gitFile.Substring("gitdir:".Length).Trim();
                gitDirectory = Path.GetFullPath(Path.Combine(resolvedPath, relativeGitDirectory));
            }

            string headPath = Path.Combine(gitDirectory, "HEAD");

            if (!File.Exists(headPath))
            {
                return false;
            }

            string head = File.ReadAllText(headPath).Trim();

            if (TryExtractRevision(head, out revision))
            {
                return true;
            }

            const string RefPrefix = "ref:";

            if (!head.StartsWith(RefPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string refPath = Path.Combine(gitDirectory, head.Substring(RefPrefix.Length).Trim().Replace('/', Path.DirectorySeparatorChar));

            return File.Exists(refPath) && TryExtractRevision(File.ReadAllText(refPath), out revision);
        }

        private static bool TryExtractRevision(string value, out string revision)
        {
            revision = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = ShaRegex.Match(value);

            if (!match.Success)
            {
                return false;
            }

            revision = match.Groups[1].Value;
            return true;
        }

        private static bool IsRevision(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   Regex.IsMatch(value, "^[0-9a-fA-F]{7,40}$");
        }

        private static bool RevisionsMatch(string installedRevision, string latestRevision)
        {
            string installed = NormalizeRevision(installedRevision);
            string latest = NormalizeRevision(latestRevision);

            if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(latest))
            {
                return false;
            }

            if (string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return installed.Length >= 7 &&
                   latest.Length >= 7 &&
                   (installed.StartsWith(latest, StringComparison.OrdinalIgnoreCase) ||
                    latest.StartsWith(installed, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeRevision(string revision)
        {
            return (revision ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string QuoteArgument(string argument)
        {
            return "\"" + (argument ?? string.Empty).Replace("\"", "\\\"") + "\"";
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

        private static void LogStatus(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return;
            }

            if (status.IsUpdateAvailable)
            {
                UnityEngine.Debug.Log(LogPrefix + " Update available for " + status.DisplayName + ": " +
                                      status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ".");
                return;
            }

            if (status.Kind == PackageUpdateStatusKind.Failed)
            {
                UnityEngine.Debug.LogWarning(LogPrefix + " Update check failed for " + status.DisplayName + ": " + status.Message);
                return;
            }

            UnityEngine.Debug.Log(LogPrefix + " Update check for " + status.DisplayName + ": " + status.Label + ".");
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private sealed class UpdateCheckItem
        {
            public UpdateCheckItem(
                PackageDefinition packageDefinition,
                PackageChannel channel,
                string selectedUrl,
                string packageManagerPackageId,
                string resolvedPath,
                IReadOnlyList<string> packageLockPaths)
            {
                PackageDefinition = packageDefinition;
                Channel = channel;
                SelectedUrl = selectedUrl ?? string.Empty;
                PackageManagerPackageId = packageManagerPackageId ?? string.Empty;
                ResolvedPath = resolvedPath ?? string.Empty;
                PackageLockPaths = packageLockPaths ?? Array.Empty<string>();
            }

            public PackageDefinition PackageDefinition { get; }

            public PackageChannel Channel { get; }

            public string SelectedUrl { get; }

            public string PackageManagerPackageId { get; }

            public string ResolvedPath { get; }

            public IReadOnlyList<string> PackageLockPaths { get; }
        }
    }
}

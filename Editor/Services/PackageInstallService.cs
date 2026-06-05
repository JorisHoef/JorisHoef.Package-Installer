using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace JorisHoef.PackageInstaller.Editor
{
    internal enum PackageInstallRequestState
    {
        Idle,
        Installing
    }

    internal sealed class PackageInstallService : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        private readonly Queue<QueuedPackageInstall> _installQueue = new Queue<QueuedPackageInstall>();
        private readonly HashSet<string> _queuedOrInstallingPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private AddRequest _currentRequest;
        private QueuedPackageInstall _currentInstall;

        public event Action StateChanged;

        public event Action<PackageDefinition, bool, string> InstallCompleted;

        public event Action QueueCompleted;

        public PackageInstallRequestState State { get; private set; } = PackageInstallRequestState.Idle;

        public PackageDefinition CurrentPackage => _currentInstall != null ? _currentInstall.PackageDefinition : null;

        public PackageChannel CurrentChannel => _currentInstall != null ? _currentInstall.Channel : PackageChannel.Stable;

        public string CurrentUrl => _currentInstall != null ? _currentInstall.Url : string.Empty;

        public bool IsBusy => State == PackageInstallRequestState.Installing || _installQueue.Count > 0;

        public bool Install(PackageDefinition packageDefinition)
        {
            return Install(packageDefinition, PackageChannel.Stable);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                Debug.LogError(LogPrefix + " Cannot install a null package definition.");
                return false;
            }

            string packageUrl = packageDefinition.GetUrl(channel);

            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                Debug.LogWarning(LogPrefix + " " + packageDefinition.DisplayName + " has no package URL to install.");
                return false;
            }

            if (_queuedOrInstallingPackageIds.Contains(packageDefinition.PackageId))
            {
                Debug.Log(LogPrefix + " " + packageDefinition.DisplayName + " is already queued or installing.");
                return false;
            }

            _installQueue.Enqueue(new QueuedPackageInstall(packageDefinition, channel, packageUrl));
            _queuedOrInstallingPackageIds.Add(packageDefinition.PackageId);
            Debug.Log(LogPrefix + " Queued " + packageDefinition.DisplayName + " from " + packageUrl + " (" + channel + ").");

            StartNextRequestIfNeeded();
            NotifyStateChanged();

            return true;
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions)
        {
            InstallMany(packageDefinitions, PackageChannel.Stable);
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions, PackageChannel channel)
        {
            InstallMany(packageDefinitions, _ => channel);
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions, Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (packageDefinitions == null)
            {
                return;
            }

            foreach (PackageDefinition packageDefinition in packageDefinitions)
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;
                Install(packageDefinition, channel);
            }
        }

        public bool IsQueuedOrInstalling(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _queuedOrInstallingPackageIds.Contains(packageId);
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
        }

        private void StartNextRequestIfNeeded()
        {
            if (_currentRequest != null || _installQueue.Count == 0)
            {
                return;
            }

            _currentInstall = _installQueue.Dequeue();
            State = PackageInstallRequestState.Installing;

            try
            {
                _currentRequest = Client.Add(_currentInstall.Url);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;

                Debug.Log(LogPrefix + " Installing " + _currentInstall.PackageDefinition.DisplayName + " using " + _currentInstall.Url + " (" + _currentInstall.Channel + ").");
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Failed to start install for " + _currentInstall.PackageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRequest(false, exception.Message);
            }
        }

        private void Update()
        {
            if (_currentRequest == null || !_currentRequest.IsCompleted)
            {
                return;
            }

            if (_currentRequest.Status == StatusCode.Success)
            {
                PackageDefinition packageDefinition = _currentInstall.PackageDefinition;
                string packageName = _currentRequest.Result != null ? _currentRequest.Result.name : packageDefinition.PackageId;
                string version = _currentRequest.Result != null ? _currentRequest.Result.version : "unknown";
                string message = "Installed " + packageDefinition.DisplayName + " (" + packageName + "@" + version + ") from " + _currentInstall.Channel + ".";

                Debug.Log(LogPrefix + " " + message);
                CompleteCurrentRequest(true, message);
                return;
            }

            string errorMessage = _currentRequest.Error != null
                ? _currentRequest.Error.message
                : "Package Manager returned an unknown error.";

            Debug.LogError(LogPrefix + " Failed to install " + _currentInstall.PackageDefinition.DisplayName + ": " + errorMessage);
            CompleteCurrentRequest(false, errorMessage);
        }

        private void CompleteCurrentRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentInstall != null ? _currentInstall.PackageDefinition : null;

            if (completedPackage != null)
            {
                _queuedOrInstallingPackageIds.Remove(completedPackage.PackageId);
            }

            _currentRequest = null;
            _currentInstall = null;
            State = PackageInstallRequestState.Idle;

            InstallCompleted?.Invoke(completedPackage, success, message);
            StartNextRequestIfNeeded();

            if (_currentRequest == null && _installQueue.Count == 0)
            {
                EditorApplication.update -= Update;
                QueueCompleted?.Invoke();
            }

            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private sealed class QueuedPackageInstall
        {
            public QueuedPackageInstall(PackageDefinition packageDefinition, PackageChannel channel, string url)
            {
                PackageDefinition = packageDefinition;
                Channel = channel;
                Url = url;
            }

            public PackageDefinition PackageDefinition { get; }

            public PackageChannel Channel { get; }

            public string Url { get; }
        }
    }
}

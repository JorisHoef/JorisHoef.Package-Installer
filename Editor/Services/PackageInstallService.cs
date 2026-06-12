using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallRequestState
    {
        Idle,
        Installing,
        Removing
    }

    internal enum PackageInstallProgressItemState
    {
        Pending,
        Active,
        Completed,
        Failed,
        Skipped
    }

    internal sealed class PackageInstallProgressItem
    {
        public PackageInstallProgressItem(string packageId, string displayName)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            State = PackageInstallProgressItemState.Pending;
            Message = string.Empty;
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public PackageInstallProgressItemState State { get; internal set; }

        public string Message { get; internal set; }
    }

    internal sealed class PackageInstallService : IDisposable
    {
        private const string LogPrefix = "[Deucarian Package Installer]";
        private const string PendingOperationNameKey = "Deucarian.PackageInstaller.PendingOperationName";
        private const string PendingQueueKey = "Deucarian.PackageInstaller.PendingQueue";

        private readonly Queue<QueuedPackageInstall> _installQueue = new Queue<QueuedPackageInstall>();
        private readonly HashSet<string> _queuedOrInstallingPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageInstallProgressItem> _progressItems = new List<PackageInstallProgressItem>();
        private readonly Dictionary<string, PackageInstallProgressItem> _progressItemsByPackageId =
            new Dictionary<string, PackageInstallProgressItem>(StringComparer.OrdinalIgnoreCase);

        private AddRequest _currentRequest;
        private RemoveRequest _currentRemoveRequest;
        private QueuedPackageInstall _currentInstall;
        private PackageDefinition _currentRemovePackage;
        private string _currentOperationName = string.Empty;
        private string _lastStatusMessage = string.Empty;
        private string _lastErrorMessage = string.Empty;
        private int _completedSteps;
        private int _successfulSteps;
        private int _failedSteps;
        private int _skippedSteps;
        private int _totalSteps;

        public event Action StateChanged;

        public event Action<PackageDefinition, bool, string> InstallCompleted;

        public event Action QueueCompleted;

        public PackageInstallRequestState State { get; private set; } = PackageInstallRequestState.Idle;

        public PackageDefinition CurrentPackage => _currentInstall != null ? _currentInstall.PackageDefinition : _currentRemovePackage;

        public PackageChannel CurrentChannel => _currentInstall != null ? _currentInstall.Channel : PackageChannel.Stable;

        public string CurrentUrl => _currentInstall != null ? _currentInstall.Url : string.Empty;

        public bool IsBusy =>
            State == PackageInstallRequestState.Installing ||
            State == PackageInstallRequestState.Removing ||
            _installQueue.Count > 0;

        public bool HasProgress => _totalSteps > 0 || !string.IsNullOrWhiteSpace(_currentOperationName);

        public string CurrentOperationName => _currentOperationName;

        public string CurrentPackageName => CurrentPackage != null ? CurrentPackage.DisplayName : string.Empty;

        public int CompletedSteps => _completedSteps;

        public int TotalSteps => _totalSteps;

        public int SuccessfulSteps => _successfulSteps;

        public int FailedSteps => _failedSteps;

        public int SkippedSteps => _skippedSteps;

        public string LastStatusMessage => _lastStatusMessage;

        public string LastErrorMessage => _lastErrorMessage;

        public IReadOnlyList<PackageInstallProgressItem> ProgressItems => _progressItems;

        public bool ResumeSavedOperation()
        {
            if (IsBusy || !TryLoadSavedOperation(out string operationName, out QueuedPackageInstall[] pendingInstalls))
            {
                return false;
            }

            PackageDefinition[] packages = pendingInstalls
                .Select(install => install.PackageDefinition)
                .Where(packageDefinition => packageDefinition != null)
                .ToArray();

            if (packages.Length == 0)
            {
                ClearSavedOperationState();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Resume Package Operation" : operationName,
                packages);

            foreach (QueuedPackageInstall pendingInstall in pendingInstalls)
            {
                QueueInstall(pendingInstall.PackageDefinition, pendingInstall.Channel);
            }

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            NotifyStateChanged();

            return IsBusy;
        }

        public bool Install(PackageDefinition packageDefinition)
        {
            return Install(packageDefinition, PackageChannel.Stable);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel)
        {
            string operationName = packageDefinition != null
                ? "Install " + packageDefinition.DisplayName
                : "Install Package";

            return Install(packageDefinition, channel, operationName);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel, string operationName)
        {
            if (packageDefinition == null)
            {
                Debug.LogError(LogPrefix + " Cannot install a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " because another package operation is already running.";
                Debug.LogWarning(LogPrefix + " " + _lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Install " + packageDefinition.DisplayName : operationName,
                new[] { packageDefinition });

            bool queued = QueueInstall(packageDefinition, channel);
            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            SavePendingOperationState();
            NotifyStateChanged();

            return queued;
        }

        private bool QueueInstall(PackageDefinition packageDefinition, PackageChannel channel)
        {
            string packageUrl = packageDefinition.GetUrl(channel);

            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                string message = packageDefinition.DisplayName + " has no package URL to install.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Failed, message);
                Debug.LogWarning(LogPrefix + " " + message);
                return false;
            }

            if (_queuedOrInstallingPackageIds.Contains(packageDefinition.PackageId))
            {
                string message = packageDefinition.DisplayName + " is already queued or installing.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Skipped, message);
                Debug.Log(LogPrefix + " " + message);
                return false;
            }

            _installQueue.Enqueue(new QueuedPackageInstall(packageDefinition, channel, packageUrl));
            _queuedOrInstallingPackageIds.Add(packageDefinition.PackageId);
            _lastStatusMessage = "Queued " + packageDefinition.DisplayName + ".";
            Debug.Log(LogPrefix + " Queued " + packageDefinition.DisplayName + " from " + packageUrl + " (" + channel + ").");

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
            InstallMany(packageDefinitions, channelSelector, "Install Packages");
        }

        public void InstallMany(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName)
        {
            if (packageDefinitions == null)
            {
                return;
            }

            PackageDefinition[] packages = packageDefinitions
                .Where(packageDefinition => packageDefinition != null)
                .GroupBy(packageDefinition => packageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            if (packages.Length == 0)
            {
                return;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + operationName + " because another package operation is already running.";
                Debug.LogWarning(LogPrefix + " " + _lastErrorMessage);
                NotifyStateChanged();
                return;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName,
                packages);

            foreach (PackageDefinition packageDefinition in packages)
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;
                QueueInstall(packageDefinition, channel);
            }

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            SavePendingOperationState();
            NotifyStateChanged();
        }

        public bool Remove(PackageDefinition packageDefinition)
        {
            string operationName = packageDefinition != null
                ? "Remove " + packageDefinition.DisplayName
                : "Remove Package";

            return Remove(packageDefinition, operationName);
        }

        public bool Remove(PackageDefinition packageDefinition, string operationName)
        {
            if (packageDefinition == null)
            {
                Debug.LogError(LogPrefix + " Cannot remove a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " removal because another package operation is already running.";
                Debug.LogWarning(LogPrefix + " " + _lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Remove " + packageDefinition.DisplayName : operationName,
                new[] { packageDefinition });

            _currentRemovePackage = packageDefinition;
            State = PackageInstallRequestState.Removing;
            MarkProgressItem(
                packageDefinition,
                PackageInstallProgressItemState.Active,
                "Removing " + packageDefinition.DisplayName + "...");
            _lastStatusMessage = "Removing " + packageDefinition.DisplayName + "...";
            ClearSavedOperationState();

            try
            {
                _currentRemoveRequest = Client.Remove(packageDefinition.PackageId);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                Debug.Log(LogPrefix + " Removing " + packageDefinition.DisplayName + " (" + packageDefinition.PackageId + ").");
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Failed to start remove for " + packageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRemoveRequest(false, exception.Message);
            }

            NotifyStateChanged();
            return _currentRemoveRequest != null;
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
            if (_currentRequest != null || _currentRemoveRequest != null || _installQueue.Count == 0)
            {
                return;
            }

            _currentInstall = _installQueue.Dequeue();
            State = PackageInstallRequestState.Installing;
            MarkProgressItem(
                _currentInstall.PackageDefinition,
                PackageInstallProgressItemState.Active,
                "Installing " + _currentInstall.PackageDefinition.DisplayName + "...");
            _lastStatusMessage = "Installing " + _currentInstall.PackageDefinition.DisplayName + "...";

            try
            {
                _currentRequest = Client.Add(_currentInstall.Url);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                SavePendingOperationState();

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
            if (_currentRemoveRequest != null)
            {
                UpdateRemoveRequest();
                return;
            }

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

                CompleteCurrentRequest(true, message);
                Debug.Log(LogPrefix + " " + message);
                return;
            }

            string errorMessage = _currentRequest.Error != null
                ? _currentRequest.Error.message
                : "Package Manager returned an unknown error.";
            string failedPackageName = _currentInstall != null && _currentInstall.PackageDefinition != null
                ? _currentInstall.PackageDefinition.DisplayName
                : "package";

            CompleteCurrentRequest(false, errorMessage);
            Debug.LogError(LogPrefix + " Failed to install " + failedPackageName + ": " + errorMessage);
        }

        private void UpdateRemoveRequest()
        {
            if (_currentRemoveRequest == null || !_currentRemoveRequest.IsCompleted)
            {
                return;
            }

            PackageDefinition packageDefinition = _currentRemovePackage;
            string packageName = packageDefinition != null ? packageDefinition.DisplayName : "package";

            if (_currentRemoveRequest.Status == StatusCode.Success)
            {
                string message = "Removed " + packageName + ".";
                CompleteCurrentRemoveRequest(true, message);
                Debug.Log(LogPrefix + " " + message);
                return;
            }

            string errorMessage = _currentRemoveRequest.Error != null
                ? _currentRemoveRequest.Error.message
                : "Package Manager returned an unknown error.";

            CompleteCurrentRemoveRequest(false, errorMessage);
            Debug.LogError(LogPrefix + " Failed to remove " + packageName + ": " + errorMessage);
        }

        private void CompleteCurrentRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentInstall != null ? _currentInstall.PackageDefinition : null;

            if (completedPackage != null)
            {
                _queuedOrInstallingPackageIds.Remove(completedPackage.PackageId);
                MarkProgressItem(
                    completedPackage,
                    success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                    message);
            }

            _currentRequest = null;
            _currentInstall = null;
            State = PackageInstallRequestState.Idle;

            InstallCompleted?.Invoke(completedPackage, success, message);
            StartNextRequestIfNeeded();

            if (_currentRequest == null && _installQueue.Count == 0)
            {
                EditorApplication.update -= Update;
                SetOperationCompleteSummary();
                ClearSavedOperationState();
                QueueCompleted?.Invoke();
            }
            else
            {
                SavePendingOperationState();
            }

            NotifyStateChanged();
        }

        private void CompleteCurrentRemoveRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentRemovePackage;

            if (completedPackage != null)
            {
                MarkProgressItem(
                    completedPackage,
                    success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                    message);
            }

            _currentRemoveRequest = null;
            _currentRemovePackage = null;
            State = PackageInstallRequestState.Idle;
            EditorApplication.update -= Update;
            SetOperationCompleteSummary();
            ClearSavedOperationState();
            QueueCompleted?.Invoke();
            NotifyStateChanged();
        }

        private void BeginOperation(string operationName, IEnumerable<PackageDefinition> packages)
        {
            _currentOperationName = operationName ?? string.Empty;
            _lastStatusMessage = "Queued " + _currentOperationName + ".";
            _lastErrorMessage = string.Empty;
            _completedSteps = 0;
            _successfulSteps = 0;
            _failedSteps = 0;
            _skippedSteps = 0;
            _progressItems.Clear();
            _progressItemsByPackageId.Clear();

            foreach (PackageDefinition packageDefinition in packages)
            {
                if (packageDefinition == null)
                {
                    continue;
                }

                PackageInstallProgressItem item = new PackageInstallProgressItem(
                    packageDefinition.PackageId,
                    packageDefinition.DisplayName);

                _progressItems.Add(item);
                _progressItemsByPackageId[packageDefinition.PackageId] = item;
            }

            _totalSteps = _progressItems.Count;
        }

        private void MarkProgressItem(
            PackageDefinition packageDefinition,
            PackageInstallProgressItemState state,
            string message)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (!_progressItemsByPackageId.TryGetValue(packageDefinition.PackageId, out PackageInstallProgressItem item))
            {
                item = new PackageInstallProgressItem(packageDefinition.PackageId, packageDefinition.DisplayName);
                _progressItems.Add(item);
                _progressItemsByPackageId[packageDefinition.PackageId] = item;
                _totalSteps = _progressItems.Count;
            }

            PackageInstallProgressItemState previousState = item.State;
            item.State = state;
            item.Message = message ?? string.Empty;

            if ((state == PackageInstallProgressItemState.Completed ||
                 state == PackageInstallProgressItemState.Failed ||
                 state == PackageInstallProgressItemState.Skipped) &&
                previousState != PackageInstallProgressItemState.Completed &&
                previousState != PackageInstallProgressItemState.Failed &&
                previousState != PackageInstallProgressItemState.Skipped)
            {
                _completedSteps++;

                if (state == PackageInstallProgressItemState.Completed)
                {
                    _successfulSteps++;
                }
                else if (state == PackageInstallProgressItemState.Failed)
                {
                    _failedSteps++;
                    _lastErrorMessage = message ?? string.Empty;
                }
                else
                {
                    _skippedSteps++;
                }
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                _lastStatusMessage = message;
            }
        }

        private void CompleteOperationIfIdle()
        {
            if (_currentRequest != null || _currentRemoveRequest != null || _installQueue.Count > 0)
            {
                return;
            }

            SetOperationCompleteSummary();
        }

        private void SetOperationCompleteSummary()
        {
            if (!HasProgress)
            {
                return;
            }

            if (_failedSteps > 0)
            {
                _lastStatusMessage = _currentOperationName + " finished with " +
                                     _successfulSteps + " succeeded and " +
                                     _failedSteps + " failed" +
                                     FormatSkippedSummarySuffix() + ".";
                return;
            }

            _lastStatusMessage = _currentOperationName + " completed successfully" +
                                 FormatSkippedSummarySuffix() + ".";
        }

        private string FormatSkippedSummarySuffix()
        {
            return _skippedSteps > 0 ? " and " + _skippedSteps + " skipped" : string.Empty;
        }

        private void SavePendingOperationState()
        {
            if (!IsBusy || State == PackageInstallRequestState.Removing)
            {
                ClearSavedOperationState();
                return;
            }

            string queue = string.Join(
                "\n",
                GetCurrentAndQueuedInstalls().Select(SerializePendingInstall).ToArray());

            SessionState.SetString(PendingOperationNameKey, _currentOperationName ?? string.Empty);
            SessionState.SetString(PendingQueueKey, queue);
        }

        private void ClearSavedOperationState()
        {
            SessionState.SetString(PendingOperationNameKey, string.Empty);
            SessionState.SetString(PendingQueueKey, string.Empty);
        }

        private IEnumerable<QueuedPackageInstall> GetCurrentAndQueuedInstalls()
        {
            if (_currentInstall != null)
            {
                yield return _currentInstall;
            }

            foreach (QueuedPackageInstall queuedInstall in _installQueue)
            {
                yield return queuedInstall;
            }
        }

        private static string SerializePendingInstall(QueuedPackageInstall install)
        {
            if (install == null || install.PackageDefinition == null)
            {
                return string.Empty;
            }

            return install.PackageDefinition.PackageId + "|" + (int)install.Channel;
        }

        private static bool TryLoadSavedOperation(
            out string operationName,
            out QueuedPackageInstall[] pendingInstalls)
        {
            operationName = SessionState.GetString(PendingOperationNameKey, string.Empty);
            string queue = SessionState.GetString(PendingQueueKey, string.Empty);

            if (string.IsNullOrWhiteSpace(queue))
            {
                pendingInstalls = Array.Empty<QueuedPackageInstall>();
                return false;
            }

            List<QueuedPackageInstall> installs = new List<QueuedPackageInstall>();

            foreach (string line in queue.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|');

                if (parts.Length < 2 ||
                    !PackageRegistryProvider.TryGetPackage(parts[0], out PackageDefinition packageDefinition) ||
                    !int.TryParse(parts[1], out int channelValue))
                {
                    continue;
                }

                PackageChannel channel = Enum.IsDefined(typeof(PackageChannel), channelValue)
                    ? (PackageChannel)channelValue
                    : PackageChannel.Stable;
                string url = packageDefinition.GetUrl(channel);

                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                installs.Add(new QueuedPackageInstall(packageDefinition, channel, url));
            }

            pendingInstalls = installs.ToArray();
            return pendingInstalls.Length > 0;
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

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class PackageInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "Package Installer";
        private const float MinWindowWidth = 520f;
        private const float MinWindowHeight = 480f;

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private PackageUpdateCheckService _packageUpdateCheckService;
        private ScriptingDefineService _scriptingDefineService;
        private IntegrationInstaller _integrationInstaller;
        private readonly Dictionary<string, PackageChannel> _selectedChannels =
            new Dictionary<string, PackageChannel>();

        private Vector2 _scrollPosition;
        private bool _checkUpdatesAfterDetectionRefresh;

        [MenuItem("Tools/JorisHoef/Package Installer")]
        public static void Open()
        {
            PackageInstallerWindow window = GetWindow<PackageInstallerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(MinWindowWidth, MinWindowHeight);

            _packageInstallService = new PackageInstallService();
            _packageDetectionService = new PackageDetectionService();
            _packageUpdateCheckService = new PackageUpdateCheckService(_packageDetectionService);
            _scriptingDefineService = new ScriptingDefineService();
            _integrationInstaller = new IntegrationInstaller(
                _packageInstallService,
                _packageDetectionService,
                _scriptingDefineService);

            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.QueueCompleted += HandlePackageInstallQueueCompleted;
            _packageDetectionService.StateChanged += Repaint;
            _packageDetectionService.RefreshCompleted += HandlePackageDetectionRefreshCompleted;
            _packageUpdateCheckService.StateChanged += Repaint;

            _packageDetectionService.Refresh();
        }

        private void OnDisable()
        {
            if (_packageInstallService != null)
            {
                _packageInstallService.StateChanged -= Repaint;
                _packageInstallService.QueueCompleted -= HandlePackageInstallQueueCompleted;
                _packageInstallService.Dispose();
            }

            if (_integrationInstaller != null)
            {
                _integrationInstaller.Dispose();
            }

            if (_packageDetectionService != null)
            {
                _packageDetectionService.StateChanged -= Repaint;
                _packageDetectionService.RefreshCompleted -= HandlePackageDetectionRefreshCompleted;
                _packageDetectionService.Dispose();
            }

            if (_packageUpdateCheckService != null)
            {
                _packageUpdateCheckService.StateChanged -= Repaint;
                _packageUpdateCheckService.Dispose();
            }
        }

        private void OnGUI()
        {
            DrawHeader();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawStandalonePackages();
            DrawIntegrations();

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("JorisHoef Package Installer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Install standalone packages and enable optional integrations for the active build target.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Build Target Group", GUILayout.Width(130f));
                EditorGUILayout.SelectableLabel(
                    _scriptingDefineService.SelectedBuildTargetGroup.ToString(),
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                using (new EditorGUI.DisabledScope(_packageDetectionService.IsRefreshing))
                {
                    if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                    {
                        _packageDetectionService.Refresh();
                        _packageUpdateCheckService.InvalidateAll();
                    }
                }

                using (new EditorGUI.DisabledScope(
                           _packageInstallService.IsBusy ||
                           _packageDetectionService.IsRefreshing ||
                           _packageUpdateCheckService.IsChecking))
                {
                    if (GUILayout.Button("Check for Updates", GUILayout.Width(130f)))
                    {
                        _packageUpdateCheckService.CheckForUpdates(PackageRegistry.StandalonePackages, GetSelectedChannel);
                    }
                }
            }

            DrawRequestStatus();
            EditorGUILayout.Space(8f);
        }

        private void DrawRequestStatus()
        {
            if (_packageDetectionService.IsRefreshing)
            {
                EditorGUILayout.HelpBox("Refreshing installed packages...", MessageType.Info);
            }

            if (_packageInstallService.State == PackageInstallRequestState.Installing &&
                _packageInstallService.CurrentPackage != null)
            {
                EditorGUILayout.HelpBox("Installing " + _packageInstallService.CurrentPackage.DisplayName + "...", MessageType.Info);
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                EditorGUILayout.HelpBox("Checking installed packages for updates...", MessageType.Info);
            }
        }

        private void DrawStandalonePackages()
        {
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.StandalonePackages)
            {
                DrawPackageCard(packageDefinition);
            }
        }

        private void DrawIntegrations()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Integrations", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.Integrations)
            {
                DrawIntegrationCard(packageDefinition);
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(8f);
            DrawUpdateAllStatus();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();

                using (new EditorGUI.DisabledScope(
                           packagesWithUpdates.Length == 0 ||
                           _packageInstallService.IsBusy ||
                           _packageDetectionService.IsRefreshing ||
                           _packageUpdateCheckService.IsChecking))
                {
                    if (GUILayout.Button("Update All Installed Packages", EditorStyles.toolbarButton, GUILayout.Width(190f)))
                    {
                        _packageInstallService.InstallMany(packagesWithUpdates, GetSelectedChannel);
                        _packageUpdateCheckService.InvalidateAll();
                    }
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_packageInstallService.IsBusy || _packageDetectionService.IsRefreshing))
                {
                    if (GUILayout.Button("Install All", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    {
                        _integrationInstaller.InstallAll(GetSelectedChannel);
                    }
                }
            }
        }

        private void DrawPackageCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(packageDefinition.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DrawChannelPopup(packageDefinition);
                    GUILayout.Space(8f);
                    DrawPackageStatus(packageDefinition);
                }

                EditorGUILayout.LabelField(packageDefinition.Description, EditorStyles.wordWrappedLabel);
                DrawDisplayVersion(packageDefinition);
                DrawSelectableValue("Package ID", packageDefinition.PackageId);
                DrawSelectableValue("Reference", packageDefinition.GetUrl(GetSelectedChannel(packageDefinition)));
                DrawPackageUpdateStatus(packageDefinition);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
                    bool queuedOrInstalling = _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId);
                    PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                        packageDefinition,
                        GetSelectedChannel(packageDefinition));

                    using (new EditorGUI.DisabledScope(
                               !updateStatus.IsUpdateAvailable ||
                               queuedOrInstalling ||
                               _packageInstallService.IsBusy ||
                               _packageDetectionService.IsRefreshing ||
                               _packageUpdateCheckService.IsChecking))
                    {
                        if (GUILayout.Button("Update", GUILayout.Width(100f)))
                        {
                            _packageInstallService.Install(packageDefinition, GetSelectedChannel(packageDefinition));
                            _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
                        }
                    }

                    using (new EditorGUI.DisabledScope(installed || queuedOrInstalling || _packageDetectionService.IsRefreshing))
                    {
                        if (GUILayout.Button(installed ? "Installed" : "Install", GUILayout.Width(100f)))
                        {
                            _integrationInstaller.InstallPackage(packageDefinition, GetSelectedChannel(packageDefinition));
                        }
                    }
                }
            }
        }

        private void DrawIntegrationCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(packageDefinition.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DrawIntegrationStatus(packageDefinition);
                }

                EditorGUILayout.LabelField(packageDefinition.Description, EditorStyles.wordWrappedLabel);
                DrawDisplayVersion(packageDefinition);
                DrawSelectableValue("Dependencies", string.Join(", ", packageDefinition.Dependencies.ToArray()));
                DrawSelectableValue("Defines", string.Join(", ", packageDefinition.ScriptingDefineSymbols.ToArray()));

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    bool complete = _integrationInstaller.IsIntegrationComplete(packageDefinition);

                    bool pending = _integrationInstaller.HasPendingIntegration(packageDefinition);

                    using (new EditorGUI.DisabledScope(complete || pending || _packageDetectionService.IsRefreshing))
                    {
                        string buttonLabel = complete ? "Enabled" : pending ? "Pending" : "Install Integration";

                        if (GUILayout.Button(buttonLabel, GUILayout.Width(140f)))
                        {
                            _integrationInstaller.InstallIntegration(packageDefinition, GetSelectedChannel);
                        }
                    }
                }
            }
        }

        private void DrawPackageStatus(PackageDefinition packageDefinition)
        {
            if (_packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                DrawStatusLabel("Queued", MessageType.Info);
                return;
            }

            if (_packageDetectionService.TryGetInstalledPackage(packageDefinition.PackageId, out PackageManagerPackageInfo packageInfo))
            {
                DrawStatusLabel("Installed " + packageInfo.version, MessageType.None);
                return;
            }

            DrawStatusLabel("Not installed", MessageType.Warning);
        }

        private void DrawPackageUpdateStatus(PackageDefinition packageDefinition)
        {
            PackageUpdateStatus status = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            DrawSelectableValue("Update", GetUpdateStatusText(status));
        }

        private void DrawIntegrationStatus(PackageDefinition packageDefinition)
        {
            if (_integrationInstaller.HasPendingIntegration(packageDefinition))
            {
                DrawStatusLabel("Pending", MessageType.Info);
                return;
            }

            bool dependenciesInstalled = _integrationInstaller.ArePackageDependenciesInstalled(packageDefinition);
            bool symbolsEnabled = _integrationInstaller.AreIntegrationSymbolsEnabled(packageDefinition);

            if (dependenciesInstalled && symbolsEnabled)
            {
                DrawStatusLabel("Enabled", MessageType.None);
                return;
            }

            if (!dependenciesInstalled && symbolsEnabled)
            {
                DrawStatusLabel("Defines enabled", MessageType.Info);
                return;
            }

            if (dependenciesInstalled)
            {
                DrawStatusLabel("Packages installed", MessageType.Info);
                return;
            }

            DrawStatusLabel("Not enabled", MessageType.Warning);
        }

        private static void DrawDisplayVersion(PackageDefinition packageDefinition)
        {
            if (!packageDefinition.HasDisplayVersion)
            {
                return;
            }

            DrawSelectableValue("Version", packageDefinition.DisplayVersion);
        }

        private void DrawChannelPopup(PackageDefinition packageDefinition)
        {
            PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);

            using (new EditorGUI.DisabledScope(!packageDefinition.HasDevelopmentUrl))
            {
                PackageChannel nextChannel = (PackageChannel)EditorGUILayout.EnumPopup(
                    selectedChannel,
                    GUILayout.Width(115f));

                if (nextChannel != selectedChannel)
                {
                    _selectedChannels[packageDefinition.PackageId] = nextChannel;
                    _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
                }
            }
        }

        private PackageChannel GetSelectedChannel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            if (_selectedChannels.TryGetValue(packageDefinition.PackageId, out PackageChannel selectedChannel))
            {
                return selectedChannel;
            }

            return PackageChannel.Stable;
        }

        private static void DrawStatusLabel(string label, MessageType messageType)
        {
            GUIStyle style = EditorStyles.miniBoldLabel;
            Color previousColor = GUI.contentColor;

            if (messageType == MessageType.Warning)
            {
                GUI.contentColor = new Color(0.9f, 0.62f, 0.2f);
            }
            else if (messageType == MessageType.Info)
            {
                GUI.contentColor = new Color(0.35f, 0.62f, 0.95f);
            }
            else
            {
                GUI.contentColor = new Color(0.35f, 0.75f, 0.35f);
            }

            GUILayout.Label(label, style, GUILayout.Width(120f));
            GUI.contentColor = previousColor;
        }

        private static void DrawSelectableValue(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90f));
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrWhiteSpace(value) ? "-" : value,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawUpdateAllStatus()
        {
            if (!_packageUpdateCheckService.HasStatuses)
            {
                return;
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                EditorGUILayout.HelpBox("Checking installed packages for updates...", MessageType.Info);
                return;
            }

            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();

            if (packagesWithUpdates.Length == 0)
            {
                EditorGUILayout.HelpBox("No package updates available for installed packages.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                "Updates available: " + string.Join(", ", packagesWithUpdates.Select(package => package.DisplayName).ToArray()),
                MessageType.Info);
        }

        private PackageDefinition[] GetPackagesWithUpdates()
        {
            return _packageUpdateCheckService
                .GetPackagesWithUpdates(PackageRegistry.StandalonePackages, GetSelectedChannel)
                .ToArray();
        }

        private static string GetUpdateStatusText(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return "Check unknown";
            }

            if (status.Kind == PackageUpdateStatusKind.UpToDate && !string.IsNullOrWhiteSpace(status.ShortLatestRevision))
            {
                return status.Label + " (" + status.ShortLatestRevision + ")";
            }

            if (status.Kind == PackageUpdateStatusKind.UpdateAvailable)
            {
                return status.Label + " (" + status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ")";
            }

            if (status.Kind == PackageUpdateStatusKind.Failed && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            return status.Label;
        }

        private void HandlePackageInstallQueueCompleted()
        {
            if (_packageUpdateCheckService.HasStatuses)
            {
                _checkUpdatesAfterDetectionRefresh = true;
            }
        }

        private void HandlePackageDetectionRefreshCompleted()
        {
            if (!_checkUpdatesAfterDetectionRefresh)
            {
                return;
            }

            _checkUpdatesAfterDetectionRefresh = false;
            _packageUpdateCheckService.CheckForUpdates(PackageRegistry.StandalonePackages, GetSelectedChannel);
        }

    }
}

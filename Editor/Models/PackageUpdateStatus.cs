namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageUpdateStatusKind
    {
        Unknown,
        NotInstalled,
        Checking,
        UpToDate,
        UpdateAvailable,
        Failed
    }

    internal sealed class PackageUpdateStatus
    {
        private const int ShortRevisionLength = 7;

        private PackageUpdateStatus(
            PackageUpdateStatusKind kind,
            string packageId,
            string displayName,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string message)
        {
            Kind = kind;
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Channel = channel;
            SelectedUrl = selectedUrl ?? string.Empty;
            InstalledRevision = installedRevision ?? string.Empty;
            LatestRevision = latestRevision ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public PackageUpdateStatusKind Kind { get; }

        public string PackageId { get; }

        public string DisplayName { get; }

        public PackageChannel Channel { get; }

        public string SelectedUrl { get; }

        public string InstalledRevision { get; }

        public string LatestRevision { get; }

        public string Message { get; }

        public bool IsChecking => Kind == PackageUpdateStatusKind.Checking;

        public bool IsUpdateAvailable => Kind == PackageUpdateStatusKind.UpdateAvailable;

        public string ShortInstalledRevision => ShortenRevision(InstalledRevision);

        public string ShortLatestRevision => ShortenRevision(LatestRevision);

        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case PackageUpdateStatusKind.NotInstalled:
                        return "Not installed";
                    case PackageUpdateStatusKind.Checking:
                        return "Checking...";
                    case PackageUpdateStatusKind.UpToDate:
                        return "Up to date";
                    case PackageUpdateStatusKind.UpdateAvailable:
                        return "Update available";
                    case PackageUpdateStatusKind.Failed:
                        return "Check failed";
                    default:
                        return "Unknown";
                }
            }
        }

        public static PackageUpdateStatus Unknown(PackageDefinition packageDefinition, PackageChannel channel)
        {
            return Create(
                PackageUpdateStatusKind.Unknown,
                packageDefinition,
                channel,
                packageDefinition != null ? packageDefinition.GetUrl(channel) : string.Empty,
                string.Empty,
                string.Empty,
                "Updates have not been checked for this package/channel yet.");
        }

        public static PackageUpdateStatus NotInstalled(PackageDefinition packageDefinition, PackageChannel channel, string selectedUrl)
        {
            return Create(
                PackageUpdateStatusKind.NotInstalled,
                packageDefinition,
                channel,
                selectedUrl,
                string.Empty,
                string.Empty,
                "Install the package before checking for updates.");
        }

        public static PackageUpdateStatus Checking(PackageDefinition packageDefinition, PackageChannel channel, string selectedUrl)
        {
            return Create(
                PackageUpdateStatusKind.Checking,
                packageDefinition,
                channel,
                selectedUrl,
                string.Empty,
                string.Empty,
                "Checking the selected Git reference.");
        }

        public static PackageUpdateStatus UpToDate(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision)
        {
            return Create(
                PackageUpdateStatusKind.UpToDate,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                "Installed revision matches the selected channel.");
        }

        public static PackageUpdateStatus UpdateAvailable(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision)
        {
            return Create(
                PackageUpdateStatusKind.UpdateAvailable,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                "Installed revision differs from the selected channel.");
        }

        public static PackageUpdateStatus Failed(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string message)
        {
            return Create(
                PackageUpdateStatusKind.Failed,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                string.Empty,
                message);
        }

        private static PackageUpdateStatus Create(
            PackageUpdateStatusKind kind,
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string message)
        {
            string packageId = packageDefinition != null ? packageDefinition.PackageId : string.Empty;
            string displayName = packageDefinition != null ? packageDefinition.DisplayName : string.Empty;

            return new PackageUpdateStatus(
                kind,
                packageId,
                displayName,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                message);
        }

        private static string ShortenRevision(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision) || revision.Length <= ShortRevisionLength)
            {
                return revision ?? string.Empty;
            }

            return revision.Substring(0, ShortRevisionLength);
        }
    }
}

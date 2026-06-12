namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryLoadResult
    {
        private PackageRegistryLoadResult(
            PackageRegistry registry,
            PackageRegistrySource source,
            bool isValid,
            string errorMessage)
        {
            Registry = registry;
            Source = source;
            IsValid = isValid;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public PackageRegistry Registry { get; }

        public PackageRegistrySource Source { get; }

        public bool IsValid { get; }

        public string ErrorMessage { get; }

        public string StatusMessage
        {
            get
            {
                switch (Source)
                {
                    case PackageRegistrySource.Remote:
                        return "Using remote registry";
                    case PackageRegistrySource.RemoteFailedUsingBundled:
                        return "Remote registry failed, using bundled registry";
                    default:
                        return "Using bundled registry";
                }
            }
        }

        public static PackageRegistryLoadResult Success(PackageRegistry registry, PackageRegistrySource source)
        {
            return new PackageRegistryLoadResult(registry, source, true, string.Empty);
        }

        public static PackageRegistryLoadResult Failure(PackageRegistrySource source, string errorMessage)
        {
            return new PackageRegistryLoadResult(null, source, false, errorMessage);
        }

        public static PackageRegistryLoadResult RemoteFailureUsingBundled(
            PackageRegistry bundledRegistry,
            string errorMessage)
        {
            return new PackageRegistryLoadResult(
                bundledRegistry,
                PackageRegistrySource.RemoteFailedUsingBundled,
                bundledRegistry != null,
                errorMessage);
        }
    }
}

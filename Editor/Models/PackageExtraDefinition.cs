using System;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageExtraDefinition
    {
        public PackageExtraDefinition(
            string displayName,
            string description,
            string sampleName = null,
            string samplePath = null,
            string destinationPath = null,
            bool requiresPackageInstalled = true)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
            }

            DisplayName = displayName;
            Description = description ?? string.Empty;
            SampleName = string.IsNullOrWhiteSpace(sampleName) ? displayName : sampleName;
            SamplePath = samplePath ?? string.Empty;
            DestinationPath = destinationPath ?? string.Empty;
            RequiresPackageInstalled = requiresPackageInstalled;
        }

        public string DisplayName { get; }

        public string Description { get; }

        public string SampleName { get; }

        public string SamplePath { get; }

        public string DestinationPath { get; }

        public bool RequiresPackageInstalled { get; }
    }
}

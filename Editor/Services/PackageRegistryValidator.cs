using System;
using System.Collections.Generic;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryValidator
    {
        public const int SupportedSchemaVersion = 1;

        public static bool Validate(PackageRegistry registry, out string message)
        {
            if (registry == null)
            {
                message = "Registry is empty.";
                return false;
            }

            if (registry.schemaVersion != SupportedSchemaVersion)
            {
                message = "Unsupported registry schemaVersion: " + registry.schemaVersion + ".";
                return false;
            }

            if (registry.packages == null)
            {
                message = "Registry packages cannot be null.";
                return false;
            }

            HashSet<string> packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageRegistryEntry package in registry.packages)
            {
                if (package == null)
                {
                    message = "Registry contains a null package entry.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.id))
                {
                    message = "Package id cannot be empty.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.displayName))
                {
                    message = "Package displayName cannot be empty for " + package.id + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.category))
                {
                    message = "Package category cannot be empty for " + package.id + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.stableUrl))
                {
                    message = "Package stableUrl cannot be empty for " + package.id + ".";
                    return false;
                }

                if (!packageIds.Add(package.id.Trim()))
                {
                    message = "Duplicate package id in registry: " + package.id + ".";
                    return false;
                }
            }

            foreach (PackageRegistryEntry package in registry.packages)
            {
                if (package.dependencies == null)
                {
                    continue;
                }

                foreach (string dependencyId in package.dependencies)
                {
                    if (string.IsNullOrWhiteSpace(dependencyId))
                    {
                        message = "Package " + package.id + " contains an empty dependency id.";
                        return false;
                    }

                    if (!packageIds.Contains(dependencyId.Trim()))
                    {
                        message = "Package " + package.id + " depends on unknown package id " + dependencyId + ".";
                        return false;
                    }
                }
            }

            message = string.Empty;
            return true;
        }
    }
}

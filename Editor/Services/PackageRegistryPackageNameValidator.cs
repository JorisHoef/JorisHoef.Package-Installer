using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryPackageNameValidator
    {
        public static bool ValidatePackageNames(
            PackageRegistry registry,
            Func<PackageRegistryEntry, string> packageJsonProvider,
            out string message)
        {
            if (!PackageRegistryValidator.Validate(registry, out message))
            {
                return false;
            }

            if (packageJsonProvider == null)
            {
                message = "Package JSON provider is unavailable.";
                return false;
            }

            foreach (PackageRegistryEntry package in registry.packages)
            {
                string packageJson;

                try
                {
                    packageJson = packageJsonProvider(package);
                }
                catch (Exception exception)
                {
                    message = "Could not read target package.json for " + package.id + ": " +
                              exception.GetBaseException().Message;
                    return false;
                }

                if (!ValidatePackageName(package, packageJson, out message))
                {
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        public static async Task<string> ValidateRemotePackageNamesAsync(
            PackageRegistry registry,
            Func<string, Task<string>> packageJsonFetcher)
        {
            if (!PackageRegistryValidator.Validate(registry, out string message))
            {
                return message;
            }

            if (packageJsonFetcher == null)
            {
                return "Package JSON fetcher is unavailable.";
            }

            foreach (PackageRegistryEntry package in registry.packages)
            {
                if (!TryCreateGitHubPackageJsonUrl(package.stableUrl, out string packageJsonUrl))
                {
                    return "Could not resolve target package.json URL for " + package.id + ".";
                }

                string packageJson;

                try
                {
                    packageJson = await packageJsonFetcher(packageJsonUrl);
                }
                catch (Exception exception)
                {
                    return "Could not fetch target package.json for " + package.id + ": " +
                           exception.GetBaseException().Message;
                }

                if (!ValidatePackageName(package, packageJson, out message))
                {
                    return message;
                }
            }

            return string.Empty;
        }

        internal static bool TryCreateGitHubPackageJsonUrl(string packageUrl, out string packageJsonUrl)
        {
            packageJsonUrl = string.Empty;

            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                return false;
            }

            string trimmedUrl = packageUrl.Trim();
            int hashIndex = trimmedUrl.LastIndexOf('#');

            if (hashIndex < 0 || hashIndex == trimmedUrl.Length - 1)
            {
                return false;
            }

            string referenceName = trimmedUrl.Substring(hashIndex + 1).Trim();
            string urlWithoutReference = trimmedUrl.Substring(0, hashIndex);
            string packagePath = string.Empty;
            int queryIndex = urlWithoutReference.IndexOf('?');

            if (queryIndex >= 0)
            {
                packagePath = ExtractPackagePath(urlWithoutReference.Substring(queryIndex + 1));
                urlWithoutReference = urlWithoutReference.Substring(0, queryIndex);
            }

            if (urlWithoutReference.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                urlWithoutReference = urlWithoutReference.Substring(4);
            }

            if (!Uri.TryCreate(urlWithoutReference, UriKind.Absolute, out Uri repositoryUri) ||
                !string.Equals(repositoryUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] segments = repositoryUri.AbsolutePath.Trim('/').Split('/');

            if (segments.Length < 2)
            {
                return false;
            }

            string owner = Uri.UnescapeDataString(segments[0]);
            string repository = Uri.UnescapeDataString(segments[1]);

            if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repository = repository.Substring(0, repository.Length - 4);
            }

            string manifestPath = string.IsNullOrWhiteSpace(packagePath)
                ? "package.json"
                : packagePath.Trim('/').TrimEnd('/') + "/package.json";

            packageJsonUrl = "https://raw.githubusercontent.com/" +
                             owner + "/" +
                             repository + "/" +
                             referenceName + "/" +
                             manifestPath;
            return true;
        }

        internal static bool TryReadPackageName(string packageJson, out string packageName)
        {
            packageName = string.Empty;

            if (string.IsNullOrWhiteSpace(packageJson))
            {
                return false;
            }

            try
            {
                PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(packageJson);

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.name))
                {
                    return false;
                }

                packageName = manifest.name.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ValidatePackageName(
            PackageRegistryEntry package,
            string packageJson,
            out string message)
        {
            if (!TryReadPackageName(packageJson, out string packageName))
            {
                message = "Could not read package.json name for " + package.id + ".";
                return false;
            }

            string registryPackageId = package.id != null ? package.id.Trim() : string.Empty;

            if (!string.Equals(registryPackageId, packageName, StringComparison.Ordinal))
            {
                message = "Registry package id " + registryPackageId +
                          " does not match target package.json name " + packageName + ".";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string ExtractPackagePath(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return string.Empty;
            }

            string[] parts = query.Split('&');

            foreach (string part in parts)
            {
                int equalsIndex = part.IndexOf('=');
                string key = equalsIndex >= 0 ? part.Substring(0, equalsIndex) : part;

                if (!string.Equals(key, "path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = equalsIndex >= 0 ? part.Substring(equalsIndex + 1) : string.Empty;
                return Uri.UnescapeDataString(value).Trim();
            }

            return string.Empty;
        }

        [Serializable]
        private sealed class PackageManifest
        {
            public string name;
        }
    }
}

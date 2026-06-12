using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryLoader
    {
        public const string RemoteRegistryUrl =
            "https://raw.githubusercontent.com/Deucarian/Package-Registry/main/packages.json";
        public const string BundledRegistryFileName = "PackageRegistry.json";

        private readonly Func<string, Task<string>> _remoteFetcher;
        private readonly Func<string, Task<string>> _packageManifestFetcher;
        private readonly string _remoteRegistryUrl;

        public PackageRegistryLoader(
            Func<string, Task<string>> remoteFetcher = null,
            string remoteRegistryUrl = RemoteRegistryUrl,
            Func<string, Task<string>> packageManifestFetcher = null)
        {
            _remoteFetcher = remoteFetcher ?? FetchRemoteJsonAsync;
            _packageManifestFetcher = packageManifestFetcher ?? FetchRemoteJsonAsync;
            _remoteRegistryUrl = string.IsNullOrWhiteSpace(remoteRegistryUrl)
                ? RemoteRegistryUrl
                : remoteRegistryUrl;
        }

        public PackageRegistryLoadResult LoadBundled()
        {
            if (!TryReadBundledRegistryJson(out string json, out string errorMessage))
            {
                return PackageRegistryLoadResult.Failure(PackageRegistrySource.Bundled, errorMessage);
            }

            return LoadFromJson(json, PackageRegistrySource.Bundled);
        }

        public async Task<PackageRegistryLoadResult> LoadRemoteAsync(PackageRegistry bundledRegistry)
        {
            try
            {
                string json = await _remoteFetcher(_remoteRegistryUrl);
                PackageRegistryLoadResult result = LoadFromJson(json, PackageRegistrySource.Remote);

                if (!result.IsValid)
                {
                    return PackageRegistryLoadResult.RemoteFailureUsingBundled(
                        bundledRegistry,
                        result.ErrorMessage);
                }

                string packageNameValidationMessage =
                    await PackageRegistryPackageNameValidator.ValidateRemotePackageNamesAsync(
                        result.Registry,
                        _packageManifestFetcher);

                if (!string.IsNullOrWhiteSpace(packageNameValidationMessage))
                {
                    return PackageRegistryLoadResult.RemoteFailureUsingBundled(
                        bundledRegistry,
                        packageNameValidationMessage);
                }

                return result;
            }
            catch (Exception exception)
            {
                return PackageRegistryLoadResult.RemoteFailureUsingBundled(
                    bundledRegistry,
                    exception.GetBaseException().Message);
            }
        }

        internal PackageRegistryLoadResult LoadFromJson(string json, PackageRegistrySource source)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return PackageRegistryLoadResult.Failure(source, "Registry JSON is empty.");
            }

            try
            {
                PackageRegistry registry = JsonUtility.FromJson<PackageRegistry>(json);

                if (!PackageRegistryValidator.Validate(registry, out string validationMessage))
                {
                    return PackageRegistryLoadResult.Failure(source, validationMessage);
                }

                return PackageRegistryLoadResult.Success(registry, source);
            }
            catch (Exception exception)
            {
                return PackageRegistryLoadResult.Failure(
                    source,
                    "Registry JSON could not be parsed: " + exception.Message);
            }
        }

        private static Task<string> FetchRemoteJsonAsync(string url)
        {
            return Task.Run(() =>
            {
                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers[HttpRequestHeader.UserAgent] = "Deucarian-Package-Installer";
                    return webClient.DownloadString(url);
                }
            });
        }

        private static bool TryReadBundledRegistryJson(out string json, out string errorMessage)
        {
            json = string.Empty;
            errorMessage = string.Empty;

            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(PackageRegistryLoader).Assembly);

            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                errorMessage = "Could not resolve installer package path.";
                return false;
            }

            string registryPath = Path.Combine(packageInfo.resolvedPath, BundledRegistryFileName);

            if (!File.Exists(registryPath))
            {
                errorMessage = "Bundled registry file was not found: " + registryPath;
                return false;
            }

            try
            {
                json = File.ReadAllText(registryPath);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not read bundled registry: " + exception.Message;
                return false;
            }
        }
    }
}

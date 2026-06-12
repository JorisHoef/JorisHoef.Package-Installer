using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageRegistryTests
    {
        private const string ValidRegistryJson =
            "{ \"schemaVersion\": 1, \"updatedAt\": \"2026-06-05\", \"packages\": [" +
            "{ \"id\": \"com.deucarian.core-state\", \"displayName\": \"Deucarian Core State\", \"category\": \"Core\", \"description\": \"Core package.\", \"stableUrl\": \"https://github.com/Deucarian/Core-State.git#main\", \"developmentUrl\": \"https://github.com/Deucarian/Core-State.git#develop\", \"dependencies\": [] }," +
            "{ \"id\": \"com.deucarian.core-state.bridge\", \"displayName\": \"Core Bridge\", \"category\": \"Bridge\", \"description\": \"Bridge package.\", \"stableUrl\": \"https://github.com/Deucarian/Core-State-Bridge.git#main\", \"developmentUrl\": \"https://github.com/Deucarian/Core-State-Bridge.git#develop\", \"dependencies\": [\"com.deucarian.core-state\"] }" +
            "] }";

        [Test]
        public void ValidRegistryParses()
        {
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(ValidRegistryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.Bundled, result.Source);
            Assert.AreEqual(2, result.Registry.packages.Length);
        }

        [Test]
        public void UnsupportedSchemaVersionIsRejected()
        {
            string json = ValidRegistryJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("Unsupported registry schemaVersion", result.ErrorMessage);
        }

        [Test]
        public void DuplicateIdsAreRejected()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.duplicate\", \"displayName\": \"One\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/one.git#main\", \"dependencies\": [] }," +
                "{ \"id\": \"com.deucarian.duplicate\", \"displayName\": \"Two\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/two.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("Duplicate package id", result.ErrorMessage);
        }

        [Test]
        public void MissingDependencyIdIsRejected()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.bridge\", \"displayName\": \"Bridge\", \"category\": \"Bridge\", \"stableUrl\": \"https://example.com/bridge.git#main\", \"dependencies\": [\"com.deucarian.missing\"] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("depends on unknown package id", result.ErrorMessage);
        }

        [Test]
        public void MissingDevelopmentUrlDisablesDevelopmentChannel()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.stable-only\", \"displayName\": \"Stable Only\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/stable.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);
            PackageDefinition packageDefinition = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .Single();

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.IsFalse(packageDefinition.HasDevelopmentUrl);
            Assert.AreEqual("https://example.com/stable.git#main", packageDefinition.GetUrl(PackageChannel.Stable));
        }

        [Test]
        public void RemoteFailureKeepsBundledRegistry()
        {
            RunAsync(async () =>
            {
            PackageRegistry bundledRegistry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.core-state",
                        displayName = "Deucarian Core State",
                        category = "Core",
                        stableUrl = "https://example.com/core.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            PackageRegistryLoader loader = new PackageRegistryLoader(
                _ => Task.FromException<string>(new InvalidOperationException("offline")));

            PackageRegistryLoadResult result = await loader.LoadRemoteAsync(bundledRegistry);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.RemoteFailedUsingBundled, result.Source);
            Assert.AreSame(bundledRegistry, result.Registry);
            });
        }

        [Test]
        public void PackageNameValidationAcceptsMatchingPackageJsonNames()
        {
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(ValidRegistryJson, PackageRegistrySource.Bundled);

            bool isValid = PackageRegistryPackageNameValidator.ValidatePackageNames(
                result.Registry,
                package => "{ \"name\": \"" + package.id + "\" }",
                out string message);

            Assert.IsTrue(isValid, message);
        }

        [Test]
        public void PackageNameValidationRejectsPackageIdMismatch()
        {
            PackageRegistry registry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.core-state-invalid",
                        displayName = "Deucarian Core State",
                        category = "Core",
                        stableUrl = "https://github.com/Deucarian/Core-State.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            bool isValid = PackageRegistryPackageNameValidator.ValidatePackageNames(
                registry,
                _ => "{ \"name\": \"com.deucarian.core-state\" }",
                out string message);

            Assert.IsFalse(isValid);
            StringAssert.Contains("does not match target package.json name", message);
        }

        [Test]
        public void RemoteRegistryPackageNameMismatchKeepsBundledRegistry()
        {
            RunAsync(async () =>
            {
            PackageRegistry bundledRegistry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.core-state",
                        displayName = "Deucarian Core State",
                        category = "Core",
                        stableUrl = "https://github.com/Deucarian/Core-State.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            string remoteJson =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.core-state-invalid\", \"displayName\": \"Deucarian Core State\", \"category\": \"Core\", \"stableUrl\": \"https://github.com/Deucarian/Core-State.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoader loader = new PackageRegistryLoader(
                _ => Task.FromResult(remoteJson),
                packageManifestFetcher: _ => Task.FromResult("{ \"name\": \"com.deucarian.core-state\" }"));

            PackageRegistryLoadResult result = await loader.LoadRemoteAsync(bundledRegistry);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.RemoteFailedUsingBundled, result.Source);
            Assert.AreSame(bundledRegistry, result.Registry);
            StringAssert.Contains("does not match target package.json name", result.ErrorMessage);
            });
        }

        [Test]
        public void GitHubPackageJsonUrlUsesBranchAndPackagePath()
        {
            bool resolved = PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                "https://github.com/Deucarian/Example.git?path=/Packages/Example#develop",
                out string packageJsonUrl);

            Assert.IsTrue(resolved);
            Assert.AreEqual(
                "https://raw.githubusercontent.com/Deucarian/Example/develop/Packages/Example/package.json",
                packageJsonUrl);
        }

        [Test]
        public void BundledRegistryUsesRealCoreStatePackageId()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            PackageDefinition coreState = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .Single(package => package.DisplayName == "Deucarian Core State");

            string[] dependencyIds = result.Registry.packages
                .Where(package => package.dependencies != null)
                .SelectMany(package => package.dependencies)
                .ToArray();

            Assert.AreEqual("com.deucarian.core-state", coreState.PackageId);
            Assert.IsFalse(dependencyIds.Contains("com.deucarian.core-state-legacy"));
        }

        [Test]
        public void BundledRegistryIncludesObjectLoadingAndApiBridge()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);

            PackageRegistryEntry objectLoading = result.Registry.packages
                .Single(package => package.id == "com.deucarian.object-loading");
            Assert.AreEqual("Deucarian Object Loading", objectLoading.displayName);
            Assert.AreEqual("Core", objectLoading.category);
            Assert.IsEmpty(objectLoading.dependencies);

            PackageRegistryEntry bridge = result.Registry.packages
                .Single(package => package.id == "com.deucarian.object-loading.api-bridge");
            Assert.AreEqual("Deucarian Object Loading API Bridge", bridge.displayName);
            Assert.AreEqual("Bridge", bridge.category);
            CollectionAssert.AreEqual(
                new[]
                {
                    "com.deucarian.object-loading",
                    "com.deucarian.api"
                },
                bridge.dependencies);
        }

        [Test]
        public void CoreStateInstalledDetectionUsesRealPackageId()
        {
            PackageDefinition coreState = PackageRegistryProvider
                .CreatePackageDefinitions(new PackageRegistry
                {
                    schemaVersion = 1,
                    packages = new[]
                    {
                        new PackageRegistryEntry
                        {
                            id = "com.deucarian.core-state",
                            displayName = "Deucarian Core State",
                            category = "Core",
                            stableUrl = "https://github.com/Deucarian/Core-State.git#main",
                            dependencies = Array.Empty<string>()
                        }
                    }
                })
                .Single();

            using (PackageDetectionService detectionService = new PackageDetectionService())
            {
                detectionService.ReplaceInstalledPackageNamesForTests(new[] { "com.deucarian.core-state" });

                Assert.IsTrue(detectionService.IsInstalled(coreState.PackageId));
                Assert.IsFalse(detectionService.IsInstalled("com.deucarian.core-state-legacy"));
            }
        }

        [Test]
        public void PackageJsonSamplesParse()
        {
            string packageJson =
                "{ \"name\": \"com.example.samples\", \"version\": \"1.2.3\", \"samples\": [" +
                "{ \"displayName\": \"Scene Setup\", \"description\": \"A ready-to-open setup scene.\", \"path\": \"Samples~/Scene Setup\" }," +
                "{ \"displayName\": \"Runtime Demo\", \"description\": \"Runtime sample content.\", \"path\": \"Samples~/Runtime Demo\" }" +
                "] }";

            PackageExtraDefinition[] samples = PackageSampleManifestParser
                .ParseSamples(packageJson)
                .ToArray();

            Assert.AreEqual(2, samples.Length);
            Assert.AreEqual("Scene Setup", samples[0].DisplayName);
            Assert.AreEqual("A ready-to-open setup scene.", samples[0].Description);
            Assert.AreEqual("Samples~/Scene Setup", samples[0].SamplePath);
            Assert.AreEqual("Runtime Demo", samples[1].DisplayName);
        }

        [Test]
        public void PackageJsonSampleWithoutDisplayNameUsesPathFolder()
        {
            string packageJson =
                "{ \"samples\": [" +
                "{ \"description\": \"Missing a displayName.\", \"path\": \"Samples~/Path Named Sample\" }" +
                "] }";

            PackageExtraDefinition sample = PackageSampleManifestParser
                .ParseSamples(packageJson)
                .Single();

            Assert.AreEqual("Path Named Sample", sample.DisplayName);
            Assert.AreEqual("Missing a displayName.", sample.Description);
            Assert.AreEqual("Samples~/Path Named Sample", sample.SamplePath);
        }

        [Test]
        public void PackageJsonSamplesSkipDuplicates()
        {
            string packageJson =
                "{ \"samples\": [" +
                "{ \"displayName\": \"Duplicate\", \"path\": \"Samples~/Duplicate\" }," +
                "{ \"displayName\": \"Duplicate Alias\", \"path\": \"Samples~/Duplicate\" }" +
                "] }";

            PackageExtraDefinition[] samples = PackageSampleManifestParser
                .ParseSamples(packageJson)
                .ToArray();

            Assert.AreEqual(1, samples.Length);
        }

        private static string GetBundledRegistryPath()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(PackageRegistryLoader).Assembly);
            Assert.IsNotNull(packageInfo);
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageInfo.resolvedPath));
            return Path.Combine(packageInfo.resolvedPath, PackageRegistryLoader.BundledRegistryFileName);
        }

        private static void RunAsync(Func<Task> asyncTest)
        {
            asyncTest().GetAwaiter().GetResult();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace JorisHoef.PackageInstaller.Editor
{
    internal static class PackageRegistry
    {
        public const string CoreStatePackageId = "com.jorishoef.core.state";
        public const string GenericUIItemsPackageId = "com.jorishoef.generic-ui-items";
        public const string APIHelperPackageId = "com.jorishoef.api-helper";
        public const string SessionHelperPackageId = "com.jorishoef.session-helper";

        public const string GenericUIItemsCoreStateSymbol = "GENERIC_UI_ITEMS_CORE_STATE";
        public const string SessionHelperAPIHelperSymbol = "SESSION_HELPER_APIHELPER";

        private static readonly PackageDefinition[] StandalonePackageDefinitions =
        {
            new PackageDefinition(
                "CoreState",
                CoreStatePackageId,
                "https://github.com/JorisHoef/Core-State.git#main",
                "Small, standalone repository and selection services for Unity projects.",
                displayVersion: "0.1.0"),

            new PackageDefinition(
                "GenericUIItems",
                GenericUIItemsPackageId,
                "https://github.com/JorisHoef/GenericUIItems.git#develop",
                "Lightweight UGUI collection-to-item presentation helpers.",
                developmentUrl: "https://github.com/JorisHoef/GenericUIItems.git#develop",
                displayVersion: "1.0.0"),

            new PackageDefinition(
                "APIHelper",
                APIHelperPackageId,
                "https://github.com/JorisHoef/API-Helper.git#main",
                "Reusable API client package for JSON, text, bytes, textures, and endpoint workflows.",
                developmentUrl: "https://github.com/JorisHoef/API-Helper.git#develop",
                displayVersion: "1.0.0"),

            new PackageDefinition(
                "SessionHelper",
                SessionHelperPackageId,
                "https://github.com/JorisHoef/Session-Helper.git#master",
                "Standalone authenticated-session lifecycle helpers with storage, restore, refresh, and change notifications.",
                developmentUrl: "https://github.com/JorisHoef/Session-Helper.git#develop",
                displayVersion: "0.1.0")
        };

        private static readonly PackageDefinition[] IntegrationPackageDefinitions =
        {
            new PackageDefinition(
                "GenericUIItems + CoreState integration",
                "com.jorishoef.integration.generic-ui-items-core-state",
                string.Empty,
                "Installs GenericUIItems and CoreState, then enables their optional integration define symbol.",
                new[] { GenericUIItemsPackageId, CoreStatePackageId },
                new[] { GenericUIItemsCoreStateSymbol },
                true),

            new PackageDefinition(
                "SessionHelper + APIHelper integration",
                "com.jorishoef.integration.session-helper-api-helper",
                string.Empty,
                "Installs SessionHelper and APIHelper, then enables the SessionHelper APIHelper adapter symbol.",
                new[] { SessionHelperPackageId, APIHelperPackageId },
                new[] { SessionHelperAPIHelperSymbol },
                true)
        };

        private static readonly PackageDefinition[] AllPackageDefinitions =
            StandalonePackageDefinitions.Concat(IntegrationPackageDefinitions).ToArray();

        public static IReadOnlyList<PackageDefinition> StandalonePackages => StandalonePackageDefinitions;

        public static IReadOnlyList<PackageDefinition> Integrations => IntegrationPackageDefinitions;

        public static IReadOnlyList<PackageDefinition> All => AllPackageDefinitions;

        public static bool TryGetPackage(string packageId, out PackageDefinition packageDefinition)
        {
            packageDefinition = AllPackageDefinitions.FirstOrDefault(definition =>
                string.Equals(definition.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

            return packageDefinition != null;
        }

        public static IEnumerable<PackageDefinition> GetInstallableDependencies(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                yield break;
            }

            foreach (string dependencyId in packageDefinition.Dependencies)
            {
                if (TryGetPackage(dependencyId, out PackageDefinition dependency) && dependency.HasPackageReference)
                {
                    yield return dependency;
                }
            }
        }
    }
}

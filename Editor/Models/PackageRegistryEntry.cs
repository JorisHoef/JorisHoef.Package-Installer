using System;

namespace Deucarian.PackageInstaller.Editor
{
    [Serializable]
    internal sealed class PackageRegistryEntry
    {
        public string id;
        public string displayName;
        public string category;
        public string description;
        public string stableUrl;
        public string developmentUrl;
        public string[] dependencies;
    }
}

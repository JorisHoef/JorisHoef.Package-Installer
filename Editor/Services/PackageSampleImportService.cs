using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageSampleImportState
    {
        Unknown,
        Importing,
        Imported,
        AlreadyImported,
        Failed
    }

    internal sealed class PackageSampleImportStatus
    {
        public PackageSampleImportStatus(PackageSampleImportState state, string message)
        {
            State = state;
            Message = message ?? string.Empty;
        }

        public PackageSampleImportState State { get; }

        public string Message { get; }
    }

    internal sealed class PackageSampleImportService
    {
        private const string LogPrefix = "[Deucarian Package Installer]";

        private readonly Dictionary<string, PackageSampleImportStatus> _statuses =
            new Dictionary<string, PackageSampleImportStatus>(StringComparer.OrdinalIgnoreCase);

        public event Action StateChanged;

        public bool IsBusy { get; private set; }

        public string CurrentOperationName { get; private set; } = string.Empty;

        public string CurrentExtraName { get; private set; } = string.Empty;

        public string LastStatusMessage { get; private set; } = string.Empty;

        public string LastErrorMessage { get; private set; } = string.Empty;

        public PackageSampleImportStatus GetStatus(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            string key = GetStatusKey(packageDefinition, extraDefinition);

            if (_statuses.TryGetValue(key, out PackageSampleImportStatus status))
            {
                return status;
            }

            if (IsSampleImported(packageDefinition, extraDefinition, packageInfo))
            {
                return new PackageSampleImportStatus(
                    PackageSampleImportState.AlreadyImported,
                    "Sample already imported.");
            }

            return new PackageSampleImportStatus(PackageSampleImportState.Unknown, string.Empty);
        }

        public bool IsSampleImported(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageDefinition == null || extraDefinition == null || packageInfo == null)
            {
                return false;
            }

            if (TryFindUnitySample(packageDefinition, extraDefinition, packageInfo, out object sample) &&
                TryGetBoolMember(sample, "isImported", out bool isImported) &&
                isImported)
            {
                return true;
            }

            return DestinationExists(GetDestinationPath(packageDefinition, extraDefinition, packageInfo));
        }

        public void ImportSample(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageDefinition == null || extraDefinition == null)
            {
                return;
            }

            string key = GetStatusKey(packageDefinition, extraDefinition);
            IsBusy = true;
            CurrentOperationName = "Import Sample";
            CurrentExtraName = extraDefinition.DisplayName;
            LastStatusMessage = "Importing sample " + extraDefinition.DisplayName + "...";
            LastErrorMessage = string.Empty;
            _statuses[key] = new PackageSampleImportStatus(PackageSampleImportState.Importing, LastStatusMessage);
            NotifyStateChanged();

            try
            {
                if (extraDefinition.RequiresPackageInstalled && packageInfo == null)
                {
                    SetStatus(key, PackageSampleImportState.Failed, "Install the package before importing this sample.");
                    return;
                }

                if (IsSampleImported(packageDefinition, extraDefinition, packageInfo))
                {
                    SetStatus(key, PackageSampleImportState.AlreadyImported, "Sample already imported.");
                    return;
                }

                if (TryImportWithUnitySampleApi(packageDefinition, extraDefinition, packageInfo, out string unityMessage))
                {
                    SetStatus(key, PackageSampleImportState.Imported, unityMessage);
                    return;
                }

                if (TryImportByCopy(packageDefinition, extraDefinition, packageInfo, out string copyMessage))
                {
                    SetStatus(key, PackageSampleImportState.Imported, copyMessage);
                    return;
                }

                string message = string.IsNullOrWhiteSpace(unityMessage)
                    ? copyMessage
                    : unityMessage + " " + copyMessage;
                SetStatus(key, PackageSampleImportState.Failed, string.IsNullOrWhiteSpace(message)
                    ? "Import failed."
                    : message.Trim());
            }
            finally
            {
                IsBusy = false;
                CurrentOperationName = string.Empty;
                CurrentExtraName = string.Empty;
                NotifyStateChanged();
            }
        }

        public string GetDestinationPath(PackageDefinition packageDefinition, PackageExtraDefinition extraDefinition)
        {
            return GetDestinationPath(packageDefinition, extraDefinition, null);
        }

        public string GetDestinationPath(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (extraDefinition != null && !string.IsNullOrWhiteSpace(extraDefinition.DestinationPath))
            {
                return NormalizeAssetPath(extraDefinition.DestinationPath);
            }

            string packageFolder = SanitizeAssetPathSegment(GetPackageDisplayName(packageDefinition, packageInfo), "Package");
            string sampleFolder = SanitizeAssetPathSegment(extraDefinition != null
                ? extraDefinition.DisplayName
                : "Sample", "Sample");

            if (packageInfo != null)
            {
                string versionFolder = SanitizeAssetPathSegment(GetPackageVersion(packageDefinition, packageInfo), "Unknown Version");
                return "Assets/Samples/" + packageFolder + "/" + versionFolder + "/" + sampleFolder;
            }

            return "Assets/Samples/" + packageFolder + "/" + sampleFolder;
        }

        private bool TryImportWithUnitySampleApi(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo,
            out string message)
        {
            message = string.Empty;

            if (packageInfo == null ||
                !TryFindUnitySample(packageDefinition, extraDefinition, packageInfo, out object sample))
            {
                return false;
            }

            try
            {
                if (TryGetBoolMember(sample, "isImported", out bool isImported) && isImported)
                {
                    message = "Sample already imported.";
                    return true;
                }

                MethodInfo importMethod = sample
                    .GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method => method.Name == "Import");

                if (importMethod == null)
                {
                    message = "Unity sample import API is unavailable for this sample.";
                    return false;
                }

                ParameterInfo[] parameters = importMethod.GetParameters();

                if (parameters.Length == 0)
                {
                    importMethod.Invoke(sample, null);
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                {
                    importMethod.Invoke(sample, new[] { Enum.ToObject(parameters[0].ParameterType, 0) });
                }
                else
                {
                    message = "Unity sample import API has an unsupported import signature.";
                    return false;
                }

                AssetDatabase.Refresh();
                message = "Imported sample " + extraDefinition.DisplayName + ".";
                Debug.Log(LogPrefix + " " + message);
                return true;
            }
            catch (Exception exception)
            {
                message = "Unity sample import failed: " + exception.GetBaseException().Message;
                Debug.LogWarning(LogPrefix + " " + message);
                return false;
            }
        }

        private bool TryImportByCopy(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo,
            out string message)
        {
            message = string.Empty;

            if (packageInfo == null)
            {
                message = "Installed package information is unavailable.";
                return false;
            }

            if (!TryGetSourcePath(packageInfo, extraDefinition, out string sourcePath, out message))
            {
                return false;
            }

            string destinationAssetPath = GetDestinationPath(packageDefinition, extraDefinition, packageInfo);

            if (!IsSafeAssetPath(destinationAssetPath))
            {
                message = "Sample destination must be inside the project's Assets folder.";
                return false;
            }

            string destinationPath = GetAbsoluteProjectPath(destinationAssetPath);
            string assetsRootPath = Path.Combine(GetProjectRootPath(), "Assets");

            if (!IsPathInsideDirectory(destinationPath, assetsRootPath))
            {
                message = "Sample destination resolves outside the project's Assets folder.";
                return false;
            }

            if (!Directory.Exists(sourcePath))
            {
                message = "Sample folder was not found: " + sourcePath;
                return false;
            }

            if (DestinationExists(destinationAssetPath))
            {
                message = "Sample already imported.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                CopyDirectory(sourcePath, destinationPath);
                AssetDatabase.Refresh();
                message = "Imported sample " + extraDefinition.DisplayName + ".";
                Debug.Log(LogPrefix + " " + message);
                return true;
            }
            catch (Exception exception)
            {
                message = "Sample import failed: " + exception.Message;
                Debug.LogWarning(LogPrefix + " " + message);
                return false;
            }
        }

        private bool TryFindUnitySample(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo,
            out object sample)
        {
            sample = null;

            if (packageDefinition == null || extraDefinition == null || packageInfo == null)
            {
                return false;
            }

            Type sampleType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEditor.PackageManager.UI.Sample"))
                .FirstOrDefault(type => type != null);

            if (sampleType == null)
            {
                return false;
            }

            MethodInfo findByPackageMethod = sampleType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                    method.Name == "FindByPackage" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string) &&
                    method.GetParameters()[1].ParameterType == typeof(string));

            if (findByPackageMethod == null)
            {
                return false;
            }

            string packageName = !string.IsNullOrWhiteSpace(packageInfo.name)
                ? packageInfo.name
                : packageDefinition.PackageId;

            object samples = findByPackageMethod.Invoke(null, new object[] { packageName, packageInfo.version });

            if (!(samples is IEnumerable enumerableSamples))
            {
                return false;
            }

            foreach (object candidate in enumerableSamples)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (SampleMatches(candidate, extraDefinition))
                {
                    sample = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool SampleMatches(object sample, PackageExtraDefinition extraDefinition)
        {
            string displayName = GetStringMember(sample, "displayName");

            if (string.Equals(displayName, extraDefinition.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(displayName, extraDefinition.SampleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedSamplePath = NormalizeAssetPath(extraDefinition.SamplePath);
            string resolvedPath = NormalizeAssetPath(GetStringMember(sample, "resolvedPath"));
            string importPath = NormalizeAssetPath(GetStringMember(sample, "importPath"));

            return !string.IsNullOrWhiteSpace(normalizedSamplePath) &&
                   (resolvedPath.EndsWith(normalizedSamplePath, StringComparison.OrdinalIgnoreCase) ||
                    importPath.EndsWith(normalizedSamplePath, StringComparison.OrdinalIgnoreCase));
        }

        private void SetStatus(string key, PackageSampleImportState state, string message)
        {
            PackageSampleImportStatus status = new PackageSampleImportStatus(state, message);
            _statuses[key] = status;
            LastStatusMessage = message ?? string.Empty;
            LastErrorMessage = state == PackageSampleImportState.Failed ? LastStatusMessage : string.Empty;
            NotifyStateChanged();
        }

        private static bool TryGetBoolMember(object target, string memberName, out bool value)
        {
            value = false;

            if (target == null)
            {
                return false;
            }

            PropertyInfo property = target.GetType().GetProperty(memberName);

            if (property == null || property.PropertyType != typeof(bool))
            {
                FieldInfo field = target.GetType().GetField(memberName);

                if (field == null || field.FieldType != typeof(bool))
                {
                    return false;
                }

                value = (bool)field.GetValue(target);
                return true;
            }

            value = (bool)property.GetValue(target, null);
            return true;
        }

        private static string GetStringMember(object target, string memberName)
        {
            if (target == null)
            {
                return string.Empty;
            }

            PropertyInfo property = target.GetType().GetProperty(memberName);

            if (property == null || property.PropertyType != typeof(string))
            {
                FieldInfo field = target.GetType().GetField(memberName);

                if (field == null || field.FieldType != typeof(string))
                {
                    return string.Empty;
                }

                return field.GetValue(target) as string ?? string.Empty;
            }

            return property.GetValue(target, null) as string ?? string.Empty;
        }

        private static bool TryGetSourcePath(
            PackageManagerPackageInfo packageInfo,
            PackageExtraDefinition extraDefinition,
            out string sourcePath,
            out string message)
        {
            sourcePath = string.Empty;
            message = string.Empty;

            if (packageInfo == null || extraDefinition == null)
            {
                message = "Installed package information is unavailable.";
                return false;
            }

            string normalizedSamplePath = NormalizeAssetPath(extraDefinition.SamplePath);

            if (string.IsNullOrWhiteSpace(normalizedSamplePath))
            {
                message = "Sample path is missing from package.json.";
                return false;
            }

            if (!IsSamplesFolderPath(normalizedSamplePath))
            {
                message = "Sample path must be inside the package's Samples~ folder.";
                return false;
            }

            string packagePath = GetPackageRootPath(packageInfo.resolvedPath);

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                message = "Installed package path is unavailable.";
                return false;
            }

            sourcePath = Path.GetFullPath(Path.Combine(
                packagePath,
                normalizedSamplePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!IsPathInsideDirectory(sourcePath, packagePath))
            {
                message = "Sample path resolves outside the installed package.";
                sourcePath = string.Empty;
                return false;
            }

            return true;
        }

        private static bool DestinationExists(string destinationAssetPath)
        {
            string normalizedPath = NormalizeAssetPath(destinationAssetPath);

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            if (!IsSafeAssetPath(normalizedPath))
            {
                return false;
            }

            string absolutePath = GetAbsoluteProjectPath(normalizedPath);
            string assetsRootPath = Path.Combine(GetProjectRootPath(), "Assets");

            if (!IsPathInsideDirectory(absolutePath, assetsRootPath))
            {
                return false;
            }

            return AssetDatabase.IsValidFolder(normalizedPath) ||
                   Directory.Exists(absolutePath) ||
                   File.Exists(absolutePath);
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(GetProjectRootPath(), NormalizeAssetPath(assetPath)));
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, destinationFile, false);
            }

            foreach (string directory in Directory.GetDirectories(sourceDirectory))
            {
                string destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(directory));
                CopyDirectory(directory, destinationSubdirectory);
            }
        }

        private static string SanitizeAssetPathSegment(string segment)
        {
            return SanitizeAssetPathSegment(segment, "Sample");
        }

        private static string SanitizeAssetPathSegment(string segment, string fallback)
        {
            string sanitized = segment ?? string.Empty;

            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized.Trim();
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static string GetPackageDisplayName(
            PackageDefinition packageDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.displayName))
            {
                return packageInfo.displayName;
            }

            return packageDefinition != null ? packageDefinition.DisplayName : "Package";
        }

        private static string GetPackageVersion(
            PackageDefinition packageDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.version))
            {
                return packageInfo.version;
            }

            return packageDefinition != null ? packageDefinition.DisplayVersion : string.Empty;
        }

        private static string GetPackageRootPath(string resolvedPath)
        {
            string packagePath = resolvedPath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return string.Empty;
            }

            if (!Path.IsPathRooted(packagePath))
            {
                packagePath = Path.Combine(GetProjectRootPath(), packagePath);
            }

            return Path.GetFullPath(packagePath);
        }

        private static string GetProjectRootPath()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            return projectRoot != null ? projectRoot.FullName : Application.dataPath;
        }

        private static bool IsSafeAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            return string.Equals(normalizedPath, "Assets", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSamplesFolderPath(string samplePath)
        {
            string normalizedPath = NormalizeAssetPath(samplePath);

            return string.Equals(normalizedPath, "Samples~", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith("Samples~/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathInsideDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string fullPath = AppendDirectorySeparator(Path.GetFullPath(path));
            string fullDirectory = AppendDirectorySeparator(Path.GetFullPath(directory));

            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string GetStatusKey(PackageDefinition packageDefinition, PackageExtraDefinition extraDefinition)
        {
            string packageId = packageDefinition != null ? packageDefinition.PackageId : string.Empty;
            string samplePath = extraDefinition != null ? extraDefinition.SamplePath : string.Empty;
            string sampleName = extraDefinition != null ? extraDefinition.SampleName : string.Empty;
            return packageId + "|" + samplePath + "|" + sampleName;
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}

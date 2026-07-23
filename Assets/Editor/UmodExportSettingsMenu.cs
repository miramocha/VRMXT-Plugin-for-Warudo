using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu mirroring <c>umod/export-settings.ps1</c>: backup/restore
/// <c>Assets/ExportSettings.asset</c> from <c>umod/local/</c> when UMod wipes it.
/// </summary>
public static class UmodExportSettingsMenu
{
    private const string MenuRoot = "Tools/UMod Export Settings/";

    [MenuItem(MenuRoot + "Restore", false, 1)]
    public static void Restore()
    {
        CleanupLegacyAssetsBackups();

        var assetOld = AssetOldPath();
        if (!File.Exists(assetOld))
        {
            EditorUtility.DisplayDialog(
                "UMod Export Settings",
                "Missing umod/local/ExportSettings.asset.old.\n" +
                "Run Backup after Mod Settings look correct.",
                "OK");
            return;
        }

        var asset = LiveAssetPath();
        File.Copy(assetOld, asset, overwrite: true);

        var metaOld = MetaOldPath();
        if (File.Exists(metaOld))
        {
            File.Copy(metaOld, LiveMetaPath(), overwrite: true);
        }

        AssetDatabase.Refresh();
        Debug.Log(
            "UMod: restored Assets/ExportSettings.asset from umod/local " +
            "(refocus Warudo → Mod Settings if needed).\n" + BuildStatusText());
        EditorUtility.DisplayDialog(
            "UMod Export Settings",
            "Restored Assets/ExportSettings.asset from umod/local.\n" +
            "Refocus Warudo → Mod Settings if the UI looks empty.",
            "OK");
    }

    [MenuItem(MenuRoot + "Backup", false, 2)]
    public static void Backup()
    {
        CleanupLegacyAssetsBackups();

        var asset = LiveAssetPath();
        if (!File.Exists(asset))
        {
            EditorUtility.DisplayDialog(
                "UMod Export Settings",
                "Missing Assets/ExportSettings.asset.\n" +
                "Fill Warudo → Mod Settings first, or copy umod/ExportSettings.example.asset.",
                "OK");
            return;
        }

        var localDir = LocalDirPath();
        Directory.CreateDirectory(localDir);
        File.Copy(asset, AssetOldPath(), overwrite: true);

        var meta = LiveMetaPath();
        if (File.Exists(meta))
        {
            File.Copy(meta, MetaOldPath(), overwrite: true);
        }

        Debug.Log("UMod: backed up ExportSettings → umod/local\n" + BuildStatusText());
        EditorUtility.DisplayDialog(
            "UMod Export Settings",
            "Backed up Assets/ExportSettings.asset → umod/local/ExportSettings.asset.old",
            "OK");
    }

    [MenuItem(MenuRoot + "Status", false, 3)]
    public static void Status()
    {
        CleanupLegacyAssetsBackups();
        var text = BuildStatusText();
        Debug.Log("UMod Export Settings status:\n" + text);
        EditorUtility.DisplayDialog("UMod Export Settings — Status", text, "OK");
    }

    private static string RepoRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string LocalDirPath()
    {
        return Path.Combine(RepoRootPath(), "umod", "local");
    }

    private static string LiveAssetPath()
    {
        return Path.Combine(Application.dataPath, "ExportSettings.asset");
    }

    private static string LiveMetaPath()
    {
        return Path.Combine(Application.dataPath, "ExportSettings.asset.meta");
    }

    private static string AssetOldPath()
    {
        return Path.Combine(LocalDirPath(), "ExportSettings.asset.old");
    }

    private static string MetaOldPath()
    {
        return Path.Combine(LocalDirPath(), "ExportSettings.asset.meta.old");
    }

    private static void CleanupLegacyAssetsBackups()
    {
        var assets = Application.dataPath;
        var legacy = new[]
        {
            Path.Combine(assets, "ExportSettings.asset.old"),
            Path.Combine(assets, "ExportSettings.asset.meta.old"),
            Path.Combine(assets, "ExportSettings.asset.old.meta"),
            Path.Combine(assets, "ExportSettings.asset.meta.old.meta"),
        };

        for (var i = 0; i < legacy.Length; i++)
        {
            if (File.Exists(legacy[i]))
            {
                File.Delete(legacy[i]);
            }
        }
    }

    private static string BuildStatusText()
    {
        var sb = new StringBuilder();
        AppendStatusLine(sb, "Assets/ExportSettings.asset", LiveAssetPath());
        AppendStatusLine(sb, "Assets/ExportSettings.asset.meta", LiveMetaPath());
        AppendStatusLine(sb, "umod/local/ExportSettings.asset.old", AssetOldPath());
        AppendStatusLine(sb, "umod/local/ExportSettings.asset.meta.old", MetaOldPath());
        return sb.ToString();
    }

    private static void AppendStatusLine(StringBuilder sb, string label, string path)
    {
        if (File.Exists(path))
        {
            var bytes = new FileInfo(path).Length;
            sb.Append(label).Append("  exists  ").Append(bytes).Append(" bytes\n");
        }
        else
        {
            sb.Append(label).Append("  missing\n");
        }
    }
}

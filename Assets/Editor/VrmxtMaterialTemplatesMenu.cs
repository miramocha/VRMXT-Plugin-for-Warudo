using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Opens StreamingAssets material Transfer folder (Unity .mat YAML templates).
/// </summary>
public static class VrmxtMaterialTemplatesMenu
{
    private const string MenuPath = "Tools/VRMXT/Open material templates folder";

    public static string AbsoluteFolderPath =>
        Path.GetFullPath(
            Path.Combine(Application.dataPath, "StreamingAssets", "VRMXT", "MaterialTemplates")
        );

    [MenuItem(MenuPath, false, 20)]
    public static void OpenFolder()
    {
        var abs = AbsoluteFolderPath;
        if (!Directory.Exists(abs))
        {
            Directory.CreateDirectory(abs);
            AssetDatabase.Refresh();
        }

        EditorUtility.RevealInFinder(abs);
        Debug.Log("[VRMXT] Material templates folder: " + abs);
    }
}

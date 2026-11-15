// Temporary diagnostic: prints glTFast's hidden import report for each .glb.
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class GltfReport
{
    public static void Run()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Object", new[] { "Assets/Apollo/Models" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".glb")) continue;
            var imp = AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.Log("GLTFREPORT no importer " + path); continue; }
            var f = imp.GetType().GetField("reportItems", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var items = f?.GetValue(imp) as System.Array;
            Debug.Log("GLTFREPORT " + path + " importer=" + imp.GetType().Name + " items=" + (items?.Length ?? -1));
            if (items == null) continue;
            foreach (var it in items)
            {
                var t = it.GetType();
                string msg = "";
                var m = t.GetMethod("ToString", System.Type.EmptyTypes);
                msg = m != null ? (string)m.Invoke(it, null) : it.ToString();
                var type = t.GetField("Type")?.GetValue(it) ?? t.GetProperty("Type")?.GetValue(it);
                var code = t.GetField("Code")?.GetValue(it) ?? t.GetProperty("Code")?.GetValue(it);
                Debug.Log("GLTFREPORT   " + type + " " + code + " " + msg);
            }
        }
    }
}

// Eagle as it rode to the Moon: legs folded, in the adapter under Columbia, docking drogue up. Built from the
// Lunar Module's published dimensions (descent stage 4.2 m across and 1.65 m tall in gold Kapton foil, ascent stage
// cabin with its two triangular windows, the docking tunnel and drogue on top, four RCS thruster quads, the
// rendezvous radar and S-band dishes, and the docking target the commander lines up in the COAS). A stand-in until a
// NASA model is added.
using UnityEngine;

public static class LmModel
{
    public static Vector3 DrogueLocal = new Vector3(0f, 5.25f, 0f);             // top of the docking drogue (set by the model in use)
    public static readonly Vector3 TargetOffset = new Vector3(0.75f, 0f, 0f);    // the docking target, sideways from the drogue

    static Material Mat(string name, Color c, float metal, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
        return m;
    }

    public static Transform Build(Transform parent, Vector3 basePos, Quaternion rot)
    {
        var nasa = LmNasa.Build(parent, basePos, rot, Mat("LM_Target_White", new Color(0.92f, 0.92f, 0.9f), 0f, 0.2f), Mat("LM_Target_Black", new Color(0.05f, 0.05f, 0.055f), 0.2f, 0.4f));
        if (nasa) return nasa;
        DrogueLocal = new Vector3(0f, 5.25f, 0f);
        var root = new GameObject("Eagle_LM").transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(basePos, rot);
        var gold = Mat("LM_Kapton_Gold", new Color(0.80f, 0.56f, 0.16f), 0.85f, 0.62f);
        var silver = Mat("LM_Silver", new Color(0.70f, 0.70f, 0.69f), 0.75f, 0.45f);
        var grey = Mat("LM_Ascent_Grey", new Color(0.46f, 0.47f, 0.46f), 0.55f, 0.35f);
        var black = Mat("LM_Black", new Color(0.05f, 0.05f, 0.055f), 0.2f, 0.4f);
        var white = Mat("LM_White", new Color(0.92f, 0.92f, 0.9f), 0f, 0.2f);

        // descent stage: an octagon in gold foil, black underneath
        Prism(root, "DescentStage", 8, 2.11f, 0.25f, 1.65f, gold, 22.5f);
        Prism(root, "DescentStage_Base", 8, 2.05f, 0.05f, 0.25f, black, 22.5f);
        // folded landing legs along four sides
        for (int k = 0; k < 4; k++)
        {
            float a = (45f + 90f * k) * Mathf.Deg2Rad;
            Vector3 out1 = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Strut(root, out1 * 2.25f + Vector3.up * 0.25f, out1 * 2.05f + Vector3.up * 1.6f, 0.07f, silver);
            Strut(root, out1 * 2.25f + Vector3.up * 0.25f, out1 * 1.6f + Vector3.up * 0.6f, 0.04f, silver);
            var pad = Prism(root, "FootPad", 12, 0.42f, 0f, 0.08f, silver, 0f);
            pad.localPosition = out1 * 2.25f + Vector3.up * 0.18f;
        }

        // ascent stage: the crew cabin (faceted front, flat sides) and the aft equipment bay
        Box(root, "AscentCabin", new Vector3(0f, 2.7f, 0.25f), new Vector3(2.4f, 2.1f, 1.9f), grey);
        Box(root, "AscentFace", new Vector3(0f, 2.55f, 1.28f), new Vector3(2.0f, 1.5f, 0.22f), silver, Quaternion.Euler(-12f, 0f, 0f));
        Box(root, "AftBay", new Vector3(0f, 2.75f, -1.15f), new Vector3(2.0f, 1.3f, 0.9f), black);
        Box(root, "UpperDeck", new Vector3(0f, 3.82f, 0f), new Vector3(2.2f, 0.18f, 2.0f), silver);
        for (int s = -1; s <= 1; s += 2)
        {
            var w = Tri(root, "Window", new Vector3(s * 0.45f, 2.95f, 1.405f), 0.42f, s, black);
            w.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        }
        // docking tunnel, drogue and the target
        Prism(root, "DockingTunnel", 16, 0.42f, 3.9f, 5.05f, silver, 0f);
        Prism(root, "Drogue", 16, 0.55f, 5.05f, 5.25f, grey, 0f);
        var target = Prism(root, "DockingTarget", 24, 0.22f, 0f, 0.02f, white, 0f);
        target.localPosition = new Vector3(TargetOffset.x, 3.95f, TargetOffset.z);
        Box(target, "CrossH", new Vector3(0f, 0.025f, 0f), new Vector3(0.40f, 0.01f, 0.05f), black);
        Box(target, "CrossV", new Vector3(0f, 0.025f, 0f), new Vector3(0.05f, 0.01f, 0.40f), black);
        Strut(target, Vector3.zero, Vector3.up * 0.38f, 0.015f, black);
        Box(target, "StandoffH", new Vector3(0f, 0.38f, 0f), new Vector3(0.18f, 0.02f, 0.02f), black);
        Box(target, "StandoffV", new Vector3(0f, 0.38f, 0f), new Vector3(0.02f, 0.02f, 0.18f), black);
        // four RCS quads at the corners of the ascent stage
        for (int k = 0; k < 4; k++)
        {
            float a = (45f + 90f * k) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Sin(a) * 1.75f, 3.35f, Mathf.Cos(a) * 1.55f);
            Box(root, "RCS_Quad", p, new Vector3(0.28f, 0.28f, 0.28f), silver);
            foreach (var d in new[] { Vector3.up, Vector3.down, new Vector3(Mathf.Sin(a), 0, 0).normalized, new Vector3(0, 0, Mathf.Cos(a)).normalized })
                Strut(root, p + d * 0.14f, p + d * 0.32f, 0.045f, black);
        }
        // rendezvous radar (front, top) and the S-band steerable antenna
        Dish(root, new Vector3(0.6f, 4.05f, 0.95f), 0.3f, Quaternion.Euler(-60f, 0f, 0f), silver);
        Strut(root, new Vector3(-0.9f, 3.9f, -0.6f), new Vector3(-1.2f, 4.5f, -0.6f), 0.03f, silver);
        Dish(root, new Vector3(-1.2f, 4.6f, -0.6f), 0.33f, Quaternion.Euler(-30f, -40f, 0f), white);
        return root;
    }

    static Transform Box(Transform p, string n, Vector3 pos, Vector3 size, Material m, Quaternion? r = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = n; g.transform.SetParent(p, false);
        g.transform.localPosition = pos; g.transform.localScale = size; g.transform.localRotation = r ?? Quaternion.identity;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g.transform;
    }

    static void Strut(Transform p, Vector3 a, Vector3 b, float r, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "Strut"; g.transform.SetParent(p, false);
        g.transform.localPosition = (a + b) * 0.5f;
        g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        g.transform.localScale = new Vector3(r * 2f, (b - a).magnitude * 0.5f, r * 2f);
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    static void Dish(Transform p, Vector3 pos, float r, Quaternion rot, Material m)
    {
        var d = Prism(p, "Dish", 20, r, 0f, 0.05f, m, 0f);
        d.localPosition = pos; d.localRotation = rot;
    }

    // a regular prism around the local y axis
    static Transform Prism(Transform p, string n, int sides, float radius, float y0, float y1, Material m, float startDeg)
    {
        var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a0 = (startDeg + 360f * i / sides) * Mathf.Deg2Rad, a1 = (startDeg + 360f * (i + 1) / sides) * Mathf.Deg2Rad;
            Vector3 p0 = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * radius, p1 = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * radius;
            int b = v.Count;
            v.Add(p0 + Vector3.up * y0); v.Add(p1 + Vector3.up * y0); v.Add(p1 + Vector3.up * y1); v.Add(p0 + Vector3.up * y1);
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            int c = v.Count;                                                    // caps
            v.Add(Vector3.up * y1); v.Add(p0 + Vector3.up * y1); v.Add(p1 + Vector3.up * y1);
            t.AddRange(new[] { c, c + 1, c + 2 });
            int e = v.Count;
            v.Add(Vector3.up * y0); v.Add(p1 + Vector3.up * y0); v.Add(p0 + Vector3.up * y0);
            t.AddRange(new[] { e, e + 1, e + 2 });
        }
        var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var g = new GameObject(n); g.transform.SetParent(p, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        return g.transform;
    }

    // a triangular window (pointing down and in, like Eagle's)
    static Transform Tri(Transform p, string n, Vector3 pos, float s, int side, Material m)
    {
        var mesh = new Mesh();
        mesh.SetVertices(new[] { new Vector3(-s * 0.5f * side, s * 0.4f, 0), new Vector3(s * 0.5f * side, s * 0.4f, 0), new Vector3(s * 0.5f * side, -s * 0.5f, 0) });
        mesh.SetTriangles(side > 0 ? new[] { 0, 2, 1, 0, 1, 2 } : new[] { 0, 1, 2, 0, 2, 1 }, 0);
        mesh.RecalculateNormals();
        var g = new GameObject(n); g.transform.SetParent(p, false); g.transform.localPosition = pos;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        return g.transform;
    }
}

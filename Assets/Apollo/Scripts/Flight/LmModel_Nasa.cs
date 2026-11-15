// Eagle from NASA's own 3D model (NASA 3D Resources, "Apollo Lunar Module", Resources/LM), folded for the trip:
// its landing gear stood out to the sides only once deployed in lunar orbit, so here the legs and pads (every part
// that stands more than 2.5 m out from the centre line) are hidden, and the docking target is added on top.
using UnityEngine;

public static class LmNasa
{
    public static Transform Build(Transform parent, Vector3 basePos, Quaternion rot, Material targetWhite, Material targetBlack)
    {
        var prefab = Resources.Load<GameObject>("LM/Apollo_Lunar_Module");
        if (!prefab) return null;
        var root = new GameObject("Eagle_LM").transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(basePos, rot);
        var m = Object.Instantiate(prefab, root, false);
        m.name = "NASA_LM";
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        Bounds b = Local(root, m, false);
        if (b.size.y < 1f) { Object.Destroy(root.gameObject); return null; }
        float s = (b.size.y > 6.8f && b.size.y < 7.3f) ? 1f : 7.04f / b.size.y;  // the real LM: 7.04 m tall, gear down (NASA model: 5.01)
        m.transform.localScale *= s;
        b = Local(root, m, false);
        Vector2 axis = new Vector2(b.center.x, b.center.z);
        foreach (var r in m.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 c = root.InverseTransformPoint(r.bounds.center);
            if (Vector2.Distance(new Vector2(c.x, c.z), axis) > 2.5f) r.enabled = false;   // legs, struts, pads, ladder
        }
        b = Local(root, m, true);
        m.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        LmModel.DrogueLocal = new Vector3(0f, b.size.y, 0f);
        // the docking target on the ascent stage's top deck, beside the drogue
        var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
        Object.Destroy(t.GetComponent<Collider>());
        t.SetParent(root, false); t.name = "DockingTarget";
        t.localPosition = new Vector3(LmModel.TargetOffset.x, b.size.y - 0.75f, LmModel.TargetOffset.z);
        t.localScale = new Vector3(0.44f, 0.01f, 0.44f);
        t.GetComponent<Renderer>().sharedMaterial = targetWhite;
        foreach (var (sx, sz) in new[] { (0.40f, 0.05f), (0.05f, 0.40f) })
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            Object.Destroy(bar.GetComponent<Collider>());
            bar.SetParent(root, false); bar.localPosition = t.localPosition + Vector3.up * 0.012f; bar.localScale = new Vector3(sx, 0.01f, sz);
            bar.GetComponent<Renderer>().sharedMaterial = targetBlack;
        }
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
        Object.Destroy(post.GetComponent<Collider>());
        post.SetParent(root, false); post.localPosition = t.localPosition + Vector3.up * 0.19f; post.localScale = new Vector3(0.03f, 0.19f, 0.03f);
        post.GetComponent<Renderer>().sharedMaterial = targetBlack;
        foreach (var (sx, sz) in new[] { (0.18f, 0.025f), (0.025f, 0.18f) })
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            Object.Destroy(bar.GetComponent<Collider>());
            bar.SetParent(root, false); bar.localPosition = t.localPosition + Vector3.up * 0.38f; bar.localScale = new Vector3(sx, 0.025f, sz);
            bar.GetComponent<Renderer>().sharedMaterial = targetBlack;
        }
        Debug.Log($"VOYAGE Eagle from the NASA model: scale {s:0.###}, folded height {b.size.y:0.00} m, width {b.size.x:0.00} m");
        return root;
    }

    // bounds of the model's renderers in the root's frame
    static Bounds Local(Transform root, GameObject m, bool enabledOnly)
    {
        bool any = false; Bounds b = default;
        foreach (var r in m.GetComponentsInChildren<Renderer>(true))
        {
            if (enabledOnly && !r.enabled) continue;
            var wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 l = root.InverseTransformPoint(p);
                if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
            }
        }
        return b;
    }
}

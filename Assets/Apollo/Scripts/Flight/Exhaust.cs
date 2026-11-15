// What the outside camera sees of the climb: the engines' plumes (the first stage's orange kerosene flame,
// the upper stages' faint hydrogen flame), the white exhaust trail left hanging in the air, and the
// stages and the escape tower falling away at their real times.
using UnityEngine;

public class Exhaust : MonoBehaviour
{
    Ascent climb;
    Transform stack, world, sic, interstage, sii, sivb, les;
    Transform plume;
    Material plumeMat;
    ParticleSystem trail;
    float sicSep = -1, ringSep = -1, siiSep = -1, lesSep = -1;
    Vector3 sicLocal0, ringLocal0, siiLocal0, lesLocal0;

    public void Begin(Ascent a, Transform stackRoot, Transform worldRoot, Transform saturn, Material plumeMaterial, Material trailMaterial)
    {
        climb = a; stack = stackRoot; world = worldRoot;
        sic = Find(saturn, "S-IC"); interstage = Find(saturn, "S-II_AftInterstage"); sii = Find(saturn, "S-II"); sivb = Find(saturn, "S-IVB"); les = Find(saturn, "LES");
        plumeMat = new Material(plumeMaterial);
        plume = new GameObject("Plume").transform;
        plume.SetParent(stack, false);
        plume.gameObject.AddComponent<MeshFilter>().sharedMesh = PlumeMesh();
        var mr = plume.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = plumeMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail = Trail(trailMaterial);
    }

    static Transform Find(Transform root, string n)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    // a flared tube along -y, 1 m long and 1 m wide at the top (scaled per stage)
    public static Mesh PlumeMesh()
    {
        const int seg = 32, rings = 20;
        var v = new Vector3[(seg + 1) * (rings + 1)]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        var t = new int[seg * rings * 6];
        for (int j = 0; j <= rings; j++)
        {
            float s = (float)j / rings, r = 0.5f + 0.9f * Mathf.Sqrt(s) + 0.4f * s;
            for (int i = 0; i <= seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                int k = j * (seg + 1) + i;
                v[k] = new Vector3(Mathf.Cos(a) * r, -s, Mathf.Sin(a) * r);
                n[k] = new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)).normalized;
                uv[k] = new Vector2((float)i / seg, s);
            }
        }
        int q = 0;
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < seg; i++)
            {
                int a = j * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                t[q++] = a; t[q++] = c; t[q++] = b; t[q++] = b; t[q++] = c; t[q++] = d;
            }
        var m = new Mesh { name = "Plume" };
        m.vertices = v; m.normals = n; m.uv = uv; m.triangles = t; m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 50f);
        return m;
    }

    ParticleSystem Trail(Material trailMaterial)
    {
        var go = new GameObject("ExhaustTrail");
        go.transform.SetParent(stack, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1000f; main.loop = true;
        main.startLifetime = 40f; main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(18f, 30f);
        main.startColor = new Color(0.86f, 0.86f, 0.84f, 0.55f);
        main.maxParticles = 4000;
        main.simulationSpace = ParticleSystemSimulationSpace.Custom;
        main.customSimulationSpace = world;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 6f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 4.5f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.95f, 0.85f, 0.7f), 0f), new GradientColorKey(new Color(0.88f, 0.88f, 0.86f), 0.1f), new GradientColorKey(new Color(0.82f, 0.82f, 0.82f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.03f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(trailMaterial);
        m.SetTexture("_BaseMap", Puff());
        r.sharedMaterial = m;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
        return ps;
    }

    static Texture2D Puff()
    {
        const int N = 128;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                float n = Mathf.PerlinNoise(x * 0.07f, y * 0.07f) * 0.5f + Mathf.PerlinNoise(x * 0.19f + 5, y * 0.19f) * 0.3f;
                float a = Mathf.Clamp01((1f - r) * 1.6f) * (0.55f + n);
                float shade = 0.85f + 0.15f * (1 - (dy + 1) * 0.5f);
                px[y * N + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
            }
        t.SetPixels(px); t.Apply(true);
        return t;
    }

    // Voyage: the third stage burns again (TLI), or the service module's engine (SPS) burns; heat 0..1, -1 = off
    public float VoyageBurn = -1f;
    public bool Cutoff;                                                             // an abort has shut the Saturn's engines down
    public Transform VoyageBurner;
    public Transform Sivb => sivb;
    public Vector3 VoyageBurnerAxis = Vector3.up;                                   // world direction the vehicle's nose points

    void Update()
    {
        if (climb == null) return;
        if (!climb.enabled)
        {
            trail.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            bool on = VoyageBurn > 0f && VoyageBurner != null && VoyageBurner.gameObject.activeInHierarchy;
            plume.gameObject.SetActive(on);
            if (on)
            {
                plume.position = Bottom(VoyageBurner);
                plume.rotation = Quaternion.FromToRotation(Vector3.up, VoyageBurnerAxis);
                float w = VoyageBurner == sivb ? 4f : 1.6f;
                plume.localScale = new Vector3(w * 2.8f, (VoyageBurner == sivb ? 45f : 14f) * 1.6f, w * 2.8f);
                plumeMat.SetColor("_Tint", new Color(0.55f, 0.65f, 1f));
                plumeMat.SetFloat("_Heat", 0.022f * VoyageBurn);
            }
            return;
        }
        float m = climb.Met, alt = climb.AltitudeM;
        // which engines are burning, and how big the plume looks (it balloons as the air thins)
        float spread = 1f + (m < 161.63f ? 3.5f : 1.8f) * Mathf.InverseLerp(10000f, 70000f, alt);
        bool sicOn = m < 161.63f, siiOn = m > 164f && m < 548.22f, sivbOn = m > 552.2f && m < 699.33f;
        Transform burner = Cutoff ? null : sicOn ? sic : siiOn ? sii : sivbOn ? sivb : null;
        plume.gameObject.SetActive(burner != null);
        if (burner != null)
        {
            var b = Bottom(burner);
            plume.position = b;
            plume.rotation = stack.rotation;
            float len = sicOn ? 120f : siiOn ? 70f : 45f, wid = sicOn ? 12f : siiOn ? 9f : 4f;
            plume.localScale = new Vector3(wid * spread, len * Mathf.Lerp(1f, 1.6f, spread / 4.5f), wid * spread);
            plumeMat.SetColor("_Tint", sicOn ? new Color(1f, 0.52f, 0.16f) : new Color(0.55f, 0.65f, 1f));
            plumeMat.SetFloat("_Heat", sicOn ? 1.2f : 0.022f);                                   // hydrogen and oxygen burn almost invisibly in sunlight
        }
        // the white trail hangs in the air below 20 km
        var em = trail.emission; em.rateOverTime = (sicOn && !Cutoff && alt < 20000f && m > 0.4f) ? 35f : 0f;
        trail.transform.position = Bottom(sic) - stack.up * 40f;

        // stages and the tower falling away
        Separate(ref sicSep, sic, 162.3f, m, ref sicLocal0, 6f, 1.2f);
        Separate(ref ringSep, interstage, 192.3f, m, ref ringLocal0, 7f, 0.4f);
        Separate(ref lesSep, les, 197.9f, m, ref lesLocal0, -35f, 2.2f);
        Separate(ref siiSep, sii, 549.0f, m, ref siiLocal0, 6f, 0.9f);
    }

    // lowest point of a stage along the vehicle axis, in world space
    Vector3 Bottom(Transform t)
    {
        if (t == null) return stack.position;
        float lo = float.MaxValue; Vector3 best = t.position;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds;
            Vector3 c = b.center - stack.up * Vector3.Dot(b.extents, new Vector3(Mathf.Abs(stack.up.x), Mathf.Abs(stack.up.y), Mathf.Abs(stack.up.z)));
            float d = Vector3.Dot(c - stack.position, stack.up);
            if (d < lo) { lo = d; best = c; }
        }
        return best;
    }

    // after its time, a part moves away along the vehicle axis (backward, or forward for the tower) and tumbles
    void Separate(ref float start, Transform part, float at, float m, ref Vector3 local0, float accel, float tumble)
    {
        if (part == null || m < at) return;
        if (start < 0) { start = Time.time; local0 = part.localPosition; }
        float s = Time.time - start;
        if (s > 25f) { part.gameObject.SetActive(false); return; }
        Vector3 axisLocal = part.parent.InverseTransformDirection(stack.up);
        part.localPosition = local0 - axisLocal * (0.5f * accel * s * s + (accel < 0 ? 0f : 1.5f * s));
        part.localRotation = part.localRotation * Quaternion.Euler(tumble * Time.deltaTime, 0.3f * tumble * Time.deltaTime, 0f);
    }
}

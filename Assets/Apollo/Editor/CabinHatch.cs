// The closed side hatch, seen from inside Columbia. The museum scan has only the opening (the hatch is not
// in it), so the door is modelled here and placed just behind the scan's own rim: the rim hides the door's
// edge, the way a real door sits behind its frame. Built on the door's own curved frame:
//   s  along the wall (horizontal), t  up the wall toward the apex, d  outward (negative = into the cabin).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CabinHatch
{
    public struct Frame { public Vector3 C, S, T, N; public float Rc; }

    // a point on (or off, by d) the door's skin, which follows the Command Module's conical wall
    public static Vector3 P(Frame f, float s, float t, float d) => f.C + f.S * s + f.T * t + f.N * (d - s * s / (2f * f.Rc));
    public static Vector3 Out(Frame f, float s) => (f.N - f.S * (s / f.Rc)).normalized;          // outward normal of the skin
    static Quaternion Orient(Frame f, float s) => Quaternion.LookRotation(-Out(f, s), f.T);   // z into the cabin, y up the wall

    public class Spec
    {
        public float A, B;                    // half width / half height of the door (m)
        public Vector2 Window;                // window centre (s, t)
        public float WindowR;                 // clear radius of the window (m)
        public Color Paint;                   // the cabin's own paint, sampled from the scan around the opening
    }

    static float Superellipse(float s, float t, float a, float b) => Mathf.Pow(Mathf.Abs(s / a), 4f) + Mathf.Pow(Mathf.Abs(t / b), 4f);

    // distance from (s0, t0) along direction th to the door's outline
    static float ToEdge(float s0, float t0, float th, float a, float b)
    {
        float lo = 0f, hi = 2f * Mathf.Max(a, b);
        for (int k = 0; k < 40; k++)
        {
            float m = (lo + hi) * 0.5f;
            if (Superellipse(s0 + Mathf.Cos(th) * m, t0 + Mathf.Sin(th) * m, a, b) < 1f) lo = m; else hi = m;
        }
        return lo;
    }

    public static GameObject Build(Frame f, Spec sp, Transform parent, out Vector3 windowCentre)
    {
        var root = new GameObject("Hatch_Closed");
        root.transform.SetParent(parent, true);
        float a = sp.A, b = sp.B, rw = sp.WindowR;
        Vector2 w = sp.Window;

        // ---------- materials ----------
        var paintTex = PaintTexture(sp, out float gearT);
        var paint = FlightBuilder.Lit("Hatch_InnerSkin", Color.white, 0.22f, 0f, paintTex);
        var metal = FlightBuilder.Lit("Hatch_Mechanism", new Color(0.56f, 0.57f, 0.57f), 0.45f, 0.55f);
        var dark = FlightBuilder.Lit("Hatch_Dark", new Color(0.13f, 0.13f, 0.135f), 0.30f, 0.2f);
        var black = FlightBuilder.Lit("Hatch_Grip", new Color(0.045f, 0.045f, 0.05f), 0.35f);
        var red = FlightBuilder.Lit("Hatch_ValveLever", new Color(0.62f, 0.08f, 0.06f), 0.35f);
        var tunnel = FlightBuilder.Lit("Hatch_WindowTunnel", new Color(0.09f, 0.09f, 0.095f), 0.25f, 0.3f);
        var glass = Glass();

        // ---------- the inner skin, with a round hole for the window ----------
        {
            var V = new List<Vector3>(); var Nn = new List<Vector3>(); var UV = new List<Vector2>(); var T = new List<int>();
            const int M = 160, K = 26;
            float r0 = rw + 0.004f;
            for (int j = 0; j <= M; j++)
            {
                float th = 2f * Mathf.PI * j / M, re = ToEdge(w.x, w.y, th, a, b);
                for (int i = 0; i <= K; i++)
                {
                    float q = (float)i / K, rr = Mathf.Lerp(r0, re, q * q * 0.35f + q * 0.65f);
                    float s = w.x + Mathf.Cos(th) * rr, t = w.y + Mathf.Sin(th) * rr;
                    V.Add(P(f, s, t, 0f)); Nn.Add(-Out(f, s));
                    UV.Add(new Vector2((s + a) / (2f * a), (t + b) / (2f * b)));
                }
            }
            for (int j = 0; j < M; j++)
                for (int i = 0; i < K; i++)
                {
                    int p00 = j * (K + 1) + i, p01 = p00 + 1, p10 = p00 + K + 1, p11 = p10 + 1;
                    // wind so the face looks into the cabin
                    Vector3 nrm = Vector3.Cross(V[p10] - V[p00], V[p01] - V[p00]);
                    if (Vector3.Dot(nrm, Nn[p00]) >= 0) T.AddRange(new[] { p00, p10, p01, p01, p10, p11 });
                    else T.AddRange(new[] { p00, p01, p10, p01, p11, p10 });
                }
            var mesh = new Mesh { name = "Hatch_Skin", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetNormals(Nn); mesh.SetUVs(0, UV); mesh.SetTriangles(T, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, FlightBuilder.Dir + "/Hatch_Skin.asset");
            var go = new GameObject("Hatch_Skin");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = paint;
        }

        // ---------- the window: a deep tunnel through the hatch, a bolted bezel, two panes ----------
        {
            var tun = new FlightBuilder.Geo(); var bez = new FlightBuilder.Geo(); var bolts = new FlightBuilder.Geo(); var gl = new FlightBuilder.Geo();
            const int seg = 64;
            float bezR = rw + 0.042f, dIn = -0.026f;
            for (int k = 0; k < seg; k++)
            {
                float t0 = 2f * Mathf.PI * k / seg, t1 = 2f * Mathf.PI * (k + 1) / seg;
                Vector2 c0 = new Vector2(Mathf.Cos(t0), Mathf.Sin(t0)), c1 = new Vector2(Mathf.Cos(t1), Mathf.Sin(t1));
                Vector3 Q(Vector2 c, float r, float d) => P(f, w.x + c.x * r, w.y + c.y * r, d);
                Vector3 axIn = -Out(f, w.x);
                // tunnel wall, seen from inside the tube
                Vector3 n0 = (Q(Vector2.zero, 0, 0) - Q(c0, rw, 0)).normalized, n1 = (Q(Vector2.zero, 0, 0) - Q(c1, rw, 0)).normalized;
                tun.QuadN(Q(c0, rw, dIn), Q(c1, rw, dIn), Q(c1, rw, 0.075f), Q(c0, rw, 0.075f), n0, n1, n1, n0);
                // bezel face (annulus) and its outer edge
                bez.Quad(Q(c0, rw, dIn), Q(c1, rw, dIn), Q(c1, bezR, dIn), Q(c0, bezR, dIn), axIn);
                Vector3 e0 = (Q(c0, bezR, 0) - Q(Vector2.zero, 0, 0)).normalized, e1 = (Q(c1, bezR, 0) - Q(Vector2.zero, 0, 0)).normalized;
                bez.QuadN(Q(c0, bezR, dIn), Q(c1, bezR, dIn), Q(c1, bezR, 0.002f), Q(c0, bezR, 0.002f), e0, e1, e1, e0);
                // the panes: inner pressure pane and the outer heat-shield pane further out
                gl.Tri(Q(Vector2.zero, 0, 0.010f), Q(c0, rw, 0.010f), Q(c1, rw, 0.010f), axIn);
                gl.Tri(Q(Vector2.zero, 0, 0.070f), Q(c0, rw, 0.070f), Q(c1, rw, 0.070f), axIn);
            }
            for (int k = 0; k < 12; k++)
            {
                float th = 2f * Mathf.PI * (k + 0.5f) / 12f;
                Vector3 bc = P(f, w.x + Mathf.Cos(th) * (rw + 0.022f), w.y + Mathf.Sin(th) * (rw + 0.022f), dIn);
                bolts.Cyl(bc, bc - Out(f, w.x) * 0.006f, 0.0055f, 0.0045f, 10);
            }
            Collide(FlightBuilder.Obj("Hatch_WindowTunnel", tun, tunnel, root.transform));
            Collide(FlightBuilder.Obj("Hatch_WindowBezel", bez, dark, root.transform));
            Collide(FlightBuilder.Obj("Hatch_WindowBolts", bolts, metal, root.transform));
            FlightBuilder.Obj("Hatch_WindowPanes", gl, glass, root.transform);
        }

        // ---------- the latching mechanism ----------
        {
            var mech = new FlightBuilder.Geo(); var box = new FlightBuilder.Geo(); var grip = new FlightBuilder.Geo(); var lever = new FlightBuilder.Geo();
            // gearbox under the window, with the drive hub and the ratchet handle
            Vector2 g = new Vector2(0f, gearT);
            box.Box(P(f, g.x, g.y, -0.032f), new Vector3(0.20f, 0.13f, 0.064f), Orient(f, g.x));
            box.Box(P(f, g.x, g.y - 0.085f, -0.012f), new Vector3(0.24f, 0.035f, 0.024f), Orient(f, g.x));
            mech.Cyl(P(f, g.x, g.y, -0.064f), P(f, g.x, g.y, -0.094f), 0.030f, 0.030f, 24);
            Vector2 hd = new Vector2(Mathf.Cos(-2.25f), Mathf.Sin(-2.25f));
            Vector3 h0 = P(f, g.x, g.y, -0.088f), h1 = P(f, g.x + hd.x * 0.30f, g.y + hd.y * 0.30f, -0.118f), h2 = P(f, g.x + hd.x * 0.42f, g.y + hd.y * 0.42f, -0.125f);
            mech.Beam(h0, h1, 0.032f, 0.020f);
            grip.Cyl(h1, h2, 0.019f, 0.017f, 18);
            // perimeter linkage: a rod loop just inside the edge, the latches on it, two drive rods from the gearbox
            float ia = a - 0.075f, ib = b - 0.075f;
            Vector2 L(float th)
            {
                float c = Mathf.Cos(th), s = Mathf.Sin(th);
                return new Vector2(ia * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.5f), ib * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.5f));
            }
            const int LS = 72;
            for (int k = 0; k < LS; k++)
            {
                Vector2 p0 = L(2f * Mathf.PI * k / LS), p1 = L(2f * Mathf.PI * (k + 1) / LS);
                mech.Cyl(P(f, p0.x, p0.y, -0.014f), P(f, p1.x, p1.y, -0.014f), 0.0065f, 0.0065f, 8, false);
            }
            for (int k = 0; k < 15; k++)
            {
                float th = 2f * Mathf.PI * (k + 0.25f) / 15f;
                Vector2 p = L(th), p2 = L(th + 0.02f), o = new Vector2(p.x / ia, p.y / ib).normalized;
                Vector2 dir = (p2 - p).normalized;
                var rotq = Quaternion.LookRotation(-Out(f, p.x), P(f, p2.x, p2.y, 0) - P(f, p.x, p.y, 0));
                box.Box(P(f, p.x + o.x * 0.028f, p.y + o.y * 0.028f, -0.016f), new Vector3(0.034f, 0.05f, 0.03f), rotq);
                mech.Cyl(P(f, p.x, p.y, -0.006f), P(f, p.x, p.y, -0.026f), 0.012f, 0.012f, 12);
            }
            foreach (float sd in new[] { -1f, 1f })
            {
                Vector2 lp = new Vector2(sd * ia, g.y);
                mech.Cyl(P(f, g.x + sd * 0.10f, g.y, -0.03f), P(f, lp.x, lp.y, -0.016f), 0.008f, 0.008f, 10);
            }
            // counterbalance (gas) cylinder along the hinge side
            Vector3 c0 = P(f, a - 0.17f, -b * 0.42f, -0.034f), c1 = P(f, a - 0.17f, b * 0.30f, -0.034f);
            mech.Cyl(c0, c1, 0.024f, 0.024f, 20);
            mech.Cyl(c1, P(f, a - 0.17f, b * 0.42f, -0.022f), 0.009f, 0.009f, 10);
            box.Box(P(f, a - 0.17f, -b * 0.46f, -0.024f), new Vector3(0.06f, 0.05f, 0.045f), Orient(f, a - 0.17f));
            // cabin pressure equalization valve with its red lever
            Vector3 v0 = P(f, -a * 0.55f, b * 0.52f, -0.004f), v1 = P(f, -a * 0.55f, b * 0.52f, -0.048f);
            mech.Cyl(v0, v1, 0.026f, 0.022f, 22);
            lever.Beam(v1, P(f, -a * 0.55f + 0.11f, b * 0.52f + 0.03f, -0.052f), 0.018f, 0.010f);
            Collide(FlightBuilder.Obj("Hatch_Linkage", mech, metal, root.transform));
            Collide(FlightBuilder.Obj("Hatch_Gearbox", box, dark, root.transform));
            Collide(FlightBuilder.Obj("Hatch_HandleGrip", grip, black, root.transform));
            Collide(FlightBuilder.Obj("Hatch_ValveLever", lever, red, root.transform));
        }
        Collide(root.transform.Find("Hatch_Skin").gameObject);
        windowCentre = P(f, w.x, w.y, 0.05f);
        return root;
    }

    static void Collide(GameObject go) => go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;

    static Material Glass()
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Hatch_WindowGlass" };
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_AlphaClip", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.One);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", new Color(0.80f, 0.86f, 0.84f, 0.05f));
        m.SetFloat("_Smoothness", 0.96f);
        m.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(m, FlightBuilder.Dir + "/Hatch_WindowGlass.mat");
        return m;
    }

    // the inner skin's paint: the cabin's own colour, worn, darker toward the frame and under the mechanism,
    // with a row of rivets just inside the edge
    static Texture2D PaintTexture(Spec sp, out float gearT)
    {
        float a = sp.A, b = sp.B, rw = sp.WindowR;
        gearT = sp.Window.y - rw - 0.17f;
        int W = 512, H = Mathf.RoundToInt(512 * b / a);
        var px = new Color[W * H];
        var rnd = new System.Random(11);
        var scuffs = new List<Vector4>();
        for (int k = 0; k < 26; k++) scuffs.Add(new Vector4((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble(), (float)rnd.NextDouble()));
        Color pc = sp.Paint;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float s = ((x + 0.5f) / W * 2f - 1f) * a, t = ((y + 0.5f) / H * 2f - 1f) * b;
                float e = Superellipse(s, t, a, b);
                float edge = Mathf.Clamp01((1f - e) / 0.45f);
                float k = 0.93f + 0.07f * Mathf.PerlinNoise(s * 6f + 3f, t * 6f + 7f) + 0.03f * (Mathf.PerlinNoise(s * 40f, t * 40f) - 0.5f);
                k *= Mathf.Lerp(0.70f, 1f, Mathf.SmoothStep(0f, 1f, edge));                          // in the frame's shadow
                float dw = Vector2.Distance(new Vector2(s, t), sp.Window);
                k *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01((dw - rw - 0.04f) / 0.05f));                // around the bezel
                float gx = Mathf.Abs(s) - 0.10f, gy = Mathf.Abs(t - gearT) - 0.065f;                 // under the gearbox
                float gd = Mathf.Max(gx, gy);
                k *= Mathf.Lerp(0.66f, 1f, Mathf.Clamp01(gd / 0.05f));
                // rivets on a line 3.5 cm inside the edge
                float ri = Mathf.Abs(Mathf.Pow(e, 0.25f) - (1f - 0.035f / Mathf.Min(a, b)));
                if (ri < 0.012f)
                {
                    float along = Mathf.Atan2(t / b, s / a) * Mathf.Min(a, b) / 0.04f;
                    float fr = Mathf.Abs(Mathf.Repeat(along, 1f) - 0.5f);
                    if (fr < 0.14f && ri < 0.006f) k *= 1.12f; else if (fr < 0.2f) k *= 0.85f;
                }
                foreach (var sc in scuffs)
                {
                    Vector2 c = new Vector2(sc.x * a, sc.y * b);
                    float ang = sc.z * Mathf.PI, len = 0.02f + sc.w * 0.07f;
                    Vector2 dd = new Vector2(s, t) - c, ax = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float al = Vector2.Dot(dd, ax), pe = Mathf.Abs(Vector2.Dot(dd, new Vector2(-ax.y, ax.x)));
                    if (Mathf.Abs(al) < len && pe < 0.0025f) k *= 0.9f;
                }
                px[y * W + x] = new Color(pc.r * k, pc.g * k, pc.b * k, 1f);
            }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Hatch_Paint", wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(px); tex.Apply(true);
        AssetDatabase.CreateAsset(tex, FlightBuilder.Dir + "/Hatch_Paint.asset");
        return tex;
    }
}

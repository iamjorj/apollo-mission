// Diagnostic: from the commander's eye, which directions see out of the Smithsonian cabin scan?
// Casts a ray panorama through the scan, groups the escaping directions into regions (real windows, the
// hatch opening, cracks between panels), and writes an annotated panorama of the real view.
//   Unity -batchmode -quit -projectPath . -executeMethod CabinProbe.Run
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CabinProbe
{
    public const int W = 1440, H = 720;                 // 0.25 degree per pixel

    public static Vector3 Dir(Quaternion frame, int x, int y)
    {
        float lon = ((x + 0.5f) / W * 360f - 180f) * Mathf.Deg2Rad, lat = ((y + 0.5f) / H * 180f - 90f) * Mathf.Deg2Rad;
        return frame * new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
    }

    // true where a ray from the eye leaves the cabin without touching it
    public static bool[] Escapes(Vector3 eye, Quaternion frame, int layerMask, float maxDist)
    {
        Physics.SyncTransforms();
        var esc = new bool[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                esc[y * W + x] = !Physics.Raycast(eye, Dir(frame, x, y), maxDist, layerMask, QueryTriggerInteraction.Ignore);
        return esc;
    }

    public class Region { public int Id, Pixels; public float SolidAngle, LonMin = 999, LonMax = -999, LatMin = 999, LatMax = -999; public Vector3 DirSum; public List<int> Px = new List<int>(); }

    public static List<Region> Regions(bool[] esc, out int[] label)
    {
        var lab = new int[W * H];
        for (int i = 0; i < lab.Length; i++) lab[i] = -1;
        label = lab;
        var regions = new List<Region>();
        var stack = new Stack<int>();
        for (int s = 0; s < esc.Length; s++)
        {
            if (!esc[s] || lab[s] >= 0) continue;
            var r = new Region { Id = regions.Count };
            regions.Add(r);
            lab[s] = r.Id; stack.Push(s);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), px = p % W, py = p / W;
                r.Px.Add(p);
                float lon = (px + 0.5f) / W * 360f - 180f, lat = (py + 0.5f) / H * 180f - 90f;
                r.Pixels++;
                float sa = Mathf.Cos(lat * Mathf.Deg2Rad) * Mathf.Pow(Mathf.PI / H, 2) * 2f;   // pixel solid angle
                r.SolidAngle += sa;
                r.DirSum += Dir(Quaternion.identity, px, py) * sa;
                r.LonMin = Mathf.Min(r.LonMin, lon); r.LonMax = Mathf.Max(r.LonMax, lon);
                r.LatMin = Mathf.Min(r.LatMin, lat); r.LatMax = Mathf.Max(r.LatMax, lat);
                void Try(int qx, int qy)
                {
                    if (qy < 0 || qy >= H) return;
                    qx = (qx + W) % W;
                    int q = qy * W + qx;
                    if (esc[q] && lab[q] < 0) { lab[q] = r.Id; stack.Push(q); }
                }
                Try(px + 1, py); Try(px - 1, py); Try(px, py + 1); Try(px, py - 1);
            }
        }
        return regions;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Apollo/Scenes/Flight.unity", OpenSceneMode.Single);
        var eye = GameObject.Find("CommanderEye").transform;
        var cabin = eye.parent;
        foreach (var l in cabin.GetComponentsInChildren<Light>(true)) l.enabled = true;
        const int cabinMask = 1 << 8;
        var esc = Escapes(eye.position, eye.rotation, cabinMask, 6f);
        var regions = Regions(esc, out var label);
        regions.Sort((a, b) => b.SolidAngle.CompareTo(a.SolidAngle));
        var palette = new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.magenta, Color.cyan, new Color(1, 0.5f, 0), new Color(0.5f, 0, 1), new Color(0, 0.6f, 0.3f), new Color(1, 0.6f, 0.8f), Color.white, new Color(0.6f, 0.4f, 0.2f) };
        var sb = new System.Text.StringBuilder();
        float total = 0f; foreach (var r in regions) total += r.SolidAngle;
        sb.AppendLine($"CABINPROBE eye {eye.position} regions {regions.Count} total {total:0.000} sr");
        var colourOf = new Dictionary<int, Color>();
        for (int k = 0; k < regions.Count; k++)
        {
            var r = regions[k];
            if (k < palette.Length) colourOf[r.Id] = palette[k];
            Vector3 dl = r.DirSum.normalized;
            float clon = Mathf.Atan2(dl.x, dl.z) * Mathf.Rad2Deg, clat = Mathf.Asin(dl.y) * Mathf.Rad2Deg;
            float fill = r.Pixels / Mathf.Max(1f, ((r.LonMax - r.LonMin) / 0.25f + 1) * ((r.LatMax - r.LatMin) / 0.25f + 1));
            // where this region's rays leave the Command Module wall (world), at 2.2 m
            Vector3 exitW = eye.position + eye.rotation * dl * 1.2f;
            if (k < 40) sb.AppendLine($"  #{k} {(k < palette.Length ? ColorUtility.ToHtmlStringRGB(palette[k]) : "------")} {r.SolidAngle:0.0000} sr px {r.Pixels} centre lon {clon:0.0} lat {clat:0.0} box lon {r.LonMin:0.0}..{r.LonMax:0.0} lat {r.LatMin:0.0}..{r.LatMax:0.0} fill {fill:0.00} worldDir {(eye.rotation * dl):F3}");
        }
        Debug.Log(sb.ToString());

        // the real view from the eye: six 90 degree renders assembled into the same panorama
        var camGo = new GameObject("ProbeCam");
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        cam.fieldOfView = 90f; cam.aspect = 1f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 50000f;
        cam.cullingMask = ~0; cam.clearFlags = CameraClearFlags.Skybox;
        const int F = 768;
        var rt = new RenderTexture(F, F, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var faces = new Texture2D[6];
        var fwd = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left, Vector3.up, Vector3.down };
        var ups = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.back, Vector3.forward };
        for (int f = 0; f < 6; f++)
        {
            cam.transform.SetPositionAndRotation(eye.position, eye.rotation * Quaternion.LookRotation(fwd[f], ups[f]));
            for (int warm = 0; warm < 3; warm++) cam.Render();
            RenderTexture.active = rt;
            faces[f] = new Texture2D(F, F, TextureFormat.RGB24, false);
            faces[f].ReadPixels(new Rect(0, 0, F, F), 0, 0); faces[f].Apply();
        }
        RenderTexture.active = null;
        var pano = new Texture2D(W, H, TextureFormat.RGB24, false);
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Vector3 d = Dir(Quaternion.identity, x, y);
                int best = 0; float bd = -2f;
                for (int f = 0; f < 6; f++) { float dd = Vector3.Dot(d, fwd[f]); if (dd > bd) { bd = dd; best = f; } }
                Quaternion q = Quaternion.LookRotation(fwd[best], ups[best]);
                Vector3 c = Quaternion.Inverse(q) * d;
                float u = c.x / c.z * 0.5f + 0.5f, v = c.y / c.z * 0.5f + 0.5f;
                Color col = faces[best].GetPixelBilinear(u, v);
                int l = label[y * W + x];
                if (l >= 0)
                {
                    bool edge = false;
                    foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int qx = (x + ox + W) % W, qy = Mathf.Clamp(y + oy, 0, H - 1);
                        if (label[qy * W + qx] != l) edge = true;
                    }
                    Color tint = colourOf.TryGetValue(l, out var pc) ? pc : new Color(0.3f, 0.3f, 0.3f);
                    col = edge ? tint : Color.Lerp(col, tint, 0.35f);
                }
                px[y * W + x] = col;
            }
        // grid every 30 degrees
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x += 120) px[y * W + x] = Color.Lerp(px[y * W + x], Color.black, 0.6f);
        for (int y = 0; y < H; y += 120) for (int x = 0; x < W; x++) px[y * W + x] = Color.Lerp(px[y * W + x], Color.black, 0.6f);
        pano.SetPixels(px); pano.Apply();
        Directory.CreateDirectory("Logs/previews");
        File.WriteAllBytes("Logs/previews/cabin_pano.png", pano.EncodeToPNG());
        Debug.Log("CABINPROBE wrote Logs/previews/cabin_pano.png");
    }

    // close-up renders from the eye toward each big opening (the added hatch door and structure hidden)
    public static void Views()
    {
        EditorSceneManager.OpenScene("Assets/Apollo/Scenes/Flight.unity", OpenSceneMode.Single);
        var eye = GameObject.Find("CommanderEye").transform;
        var cabin = eye.parent;
        foreach (var n in new[] { "Hatch_Closed" }) { var t = cabin.Find(n); if (t) t.gameObject.SetActive(false); }
        foreach (var r0 in cabin.GetComponentsInChildren<MeshRenderer>(true)) if (r0.name == "Cabin_Structure") r0.enabled = false;
        foreach (var l in cabin.GetComponentsInChildren<Light>(true)) l.enabled = true;
        var esc = Escapes(eye.position, eye.rotation, 1 << 8, 6f);
        var regions = Regions(esc, out var label);
        regions.Sort((a, b) => b.SolidAngle.CompareTo(a.SolidAngle));
        var camGo = new GameObject("ProbeCam");
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        cam.fieldOfView = 75f; cam.aspect = 1f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 50000f; cam.cullingMask = ~0;
        var rt = new RenderTexture(900, 900, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        Directory.CreateDirectory("Logs/previews");
        for (int k = 0; k < Mathf.Min(6, regions.Count); k++)
        {
            var r = regions[k];
            Vector3 d = eye.rotation * r.DirSum.normalized;
            cam.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(d, eye.rotation * Vector3.up));
            for (int warm = 0; warm < 3; warm++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply();
            File.WriteAllBytes($"Logs/previews/cabin_view_{k}.png", tex.EncodeToPNG());
            Debug.Log($"CABINVIEW {k}: {r.SolidAngle:0.000} sr centre dir {d:F3}");
        }
        RenderTexture.active = null;
    }

    // pad renders from the intro's first frame, the aerial test view and the lawn, with the game's sky light
    public static void PadViews()
    {
        EditorSceneManager.OpenScene("Assets/Apollo/Scenes/Flight.unity", OpenSceneMode.Single);
        var sun = GameObject.Find("Sun").GetComponent<Light>();
        sun.transform.rotation = Quaternion.LookRotation(-new Vector3(Mathf.Sin(135f * Mathf.Deg2Rad) * Mathf.Cos(30f * Mathf.Deg2Rad), Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(135f * Mathf.Deg2Rad) * Mathf.Cos(30f * Mathf.Deg2Rad)));
        sun.intensity = 2.0f;
        FlightDirector.ApplyAmbient(FlightDirector.GradientSH(new Color(0.29f, 0.39f, 0.56f) * 0.9f, new Color(0.40f, 0.44f, 0.50f) * 0.9f, new Color(0.17f, 0.16f, 0.12f) * 0.9f), "preview");
        var cam = GameObject.Find("Main Camera").GetComponent<Camera>();
        cam.cullingMask = ~(1 << 8);
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var shots = new (string n, Vector3 p, Vector3 t, float fov)[]
        {
            ("pad_intro0", new Vector3(-240f, 38f, -300f), new Vector3(0f, 74f, 0f), 48f),
            ("pad_aerial", new Vector3(-700f, 380f, -900f), new Vector3(0f, 0f, 60f), 42f),
            ("pad_lawn", new Vector3(-150f, 2f, -190f), new Vector3(0f, 30f, 0f), 55f),
            ("pad_trench", new Vector3(-60f, 30f, 120f), new Vector3(0f, -6f, 30f), 50f),
        };
        foreach (var s0 in shots)
        {
            cam.fieldOfView = s0.fov;
            cam.transform.SetPositionAndRotation(s0.p, Quaternion.LookRotation(s0.t - s0.p));
            for (int warm = 0; warm < 3; warm++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
            File.WriteAllBytes($"Logs/previews/{s0.n}.png", tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        Debug.Log("PADVIEWS done");
    }

    // the cabin as the player sees it: sealed, hatch closed, the game's cabin light
    public static void CabinShots()
    {
        EditorSceneManager.OpenScene("Assets/Apollo/Scenes/Flight.unity", OpenSceneMode.Single);
        var fd = Object.FindObjectOfType<FlightDirector>();
        var eye = fd.CommanderEye;
        foreach (var l in eye.parent.GetComponentsInChildren<Light>(true)) l.enabled = true;
        foreach (var r in fd.HideWhileEntering) r.enabled = false;
        FlightDirector.ApplyAmbient(FlightDirector.GradientSH(new Color(0.212f, 0.227f, 0.259f).linear, new Color(0.114f, 0.125f, 0.133f).linear, new Color(0.047f, 0.043f, 0.035f).linear), "cabin preview");
        var cam = fd.Cam;
        cam.cullingMask = ~0; cam.nearClipPlane = 0.03f; cam.fieldOfView = 72f;
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        Vector3 hw = Quaternion.Inverse(eye.rotation) * (fd.HatchWindow - eye.position);
        var shots = new (string n, float yaw, float pitch)[]
        {
            ("cab_forward", 0f, 0f), ("cab_left_window", -55f, 20f),
            ("cab_hatch", Mathf.Atan2(hw.x, hw.z) * Mathf.Rad2Deg, Mathf.Asin(hw.normalized.y) * Mathf.Rad2Deg),
            ("cab_up_right", 30f, 35f), ("cab_up_left", -25f, 40f),
        };
        foreach (var s0 in shots)
        {
            cam.transform.SetPositionAndRotation(eye.position, eye.rotation * Quaternion.Euler(-s0.pitch, s0.yaw, 0f));
            for (int warm = 0; warm < 3; warm++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
            File.WriteAllBytes($"Logs/previews/{s0.n}.png", tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        Debug.Log("CABINSHOTS done");
    }

    // what does the commander see through each window? (renderers whose bounds the window's rays cross)
    public static void WhatsOutside()
    {
        EditorSceneManager.OpenScene("Assets/Apollo/Scenes/Flight.unity", OpenSceneMode.Single);
        var fd = Object.FindObjectOfType<FlightDirector>();
        var eye = fd.CommanderEye;
        var hidden = new HashSet<Renderer>(fd.HideWhileEntering);
        var sb = new System.Text.StringBuilder("OUTSIDE" + System.Environment.NewLine);
        foreach (var (n, lon, lat) in new[] { ("left side", -79f, 17f), ("left rendezvous", -17f, 37f), ("right", 52f, 5f), ("hatch window", 30f, 38f) })
        {
            Vector3 d = eye.rotation * Dir(Quaternion.identity, Mathf.RoundToInt((lon + 180f) / 360f * W), Mathf.RoundToInt((lat + 90f) / 180f * H));
            sb.Append($"  {n} dir {d:F2}:");
            var hits = new List<(float, string)>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r.gameObject.layer == 8 || hidden.Contains(r) || !r.enabled) continue;
                if (r.bounds.IntersectRay(new Ray(eye.position, d), out float dist) && dist < 60f) hits.Add((dist, r.name));
            }
            hits.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            foreach (var h in hits) sb.Append($" {h.Item2}@{h.Item1:0.0}");
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    // the 3D clouds at several heights of the climb, with the game's light (editor renders)
    public static void CloudViews()
    {
        EditorSceneManager.OpenScene("Assets/Apollo/Scenes/Flight.unity", OpenSceneMode.Single);
        var fd = Object.FindObjectOfType<FlightDirector>();
        var sun = fd.Sun;
        float el = 25f, az = 130f;
        var sd = new Vector3(Mathf.Sin(az * Mathf.Deg2Rad) * Mathf.Cos(el * Mathf.Deg2Rad), Mathf.Sin(el * Mathf.Deg2Rad), Mathf.Cos(az * Mathf.Deg2Rad) * Mathf.Cos(el * Mathf.Deg2Rad));
        sun.transform.rotation = Quaternion.LookRotation(-sd);
        sun.intensity = Mathf.Lerp(1.4f, 2.3f, el / 45f);
        sun.color = Color.Lerp(new Color(1f, 0.80f, 0.62f), new Color(1f, 0.96f, 0.91f), Mathf.Clamp01(el / 30f));
        FlightDirector.ApplyAmbient(FlightDirector.GradientSH(new Color(0.29f, 0.39f, 0.56f) * 0.92f, new Color(0.40f, 0.44f, 0.50f) * 0.92f, new Color(0.17f, 0.16f, 0.12f) * 0.92f), "preview");
        RenderSettings.skybox.SetFloat("_Rotation", Mathf.Repeat(0.5953f - az / 360f, 1f));
        // the same placement rule as the game: a cumulus over the climb, none shading the pad
        var w = fd.CloudWeather;
        Vector2 drift = FlightDirector.FindCloudDrift(fd.CloudShape, w, sd, out float best);
        FlightDirector.CloudGlobals(w, drift, 14.1f, 1f);
        Debug.Log($"CLOUDVIEWS drift {drift} score {best:0.00}");
        var cam = fd.Cam;
        cam.cullingMask = ~(1 << 8);
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var shots = new (string n, Vector3 p, Vector3 look, float fov, float photo)[]
        {
            ("cl_2_pad_atmo", new Vector3(-240f, 38f, -300f), new Vector3(0f, 74f, 0f), 48f, 0f),
            ("cl_3_climb_600m", new Vector3(-30f, 600f, -20f), new Vector3(60f, 1700f, 60f), 60f, 0f),
            ("cl_4_under_1050m", new Vector3(10f, 1050f, 0f), new Vector3(400f, 1500f, 300f), 60f, 0f),
            ("cl_5_inside_1700m", new Vector3(80f, 1700f, 25f), new Vector3(300f, 1650f, 600f), 65f, 0f),
            ("cl_6_above_3200m", new Vector3(300f, 3200f, 90f), new Vector3(3000f, 900f, 2500f), 60f, 0f),
            ("cl_7_high_12km", new Vector3(3000f, 12000f, 900f), new Vector3(20000f, 0f, 9000f), 60f, 0f),
        };
        foreach (var s0 in shots)
        {
            fd.SkyMat.SetFloat("_PhotoMix", s0.photo);
            Shader.SetGlobalFloat("_CamAltitude", s0.p.y + 14.1f);
            cam.fieldOfView = s0.fov;
            cam.transform.SetPositionAndRotation(s0.p, Quaternion.LookRotation(s0.look - s0.p));
            for (int warm = 0; warm < 3; warm++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
            File.WriteAllBytes($"Logs/previews/{s0.n}.png", tex.EncodeToPNG());
        }
        // from the commander's couch during the climb: lift the vehicle and the cabin together
        {
            var eye = fd.CommanderEye;
            var sv = GameObject.Find("SaturnV_AS506");
            foreach (var r in fd.HideWhileEntering) r.enabled = false;
            foreach (var l in eye.parent.GetComponentsInChildren<Light>(true)) l.enabled = true;
            cam.cullingMask = ~0; cam.nearClipPlane = 0.03f; cam.fieldOfView = 75f;
            fd.SkyMat.SetFloat("_PhotoMix", 0f);
            float lifted = 0f;
            foreach (var (n, alt, yaw, pitch) in new[] { ("cab_a_0700m_up", 700f, -17f, 37f), ("cab_b_1500m_up", 1500f, -17f, 37f), ("cab_c_1500m_side", 1500f, -70f, 15f),
                                                         ("cab_d_2900m_side", 2900f, -75f, 5f), ("cab_e_2900m_up", 2900f, -17f, 37f), ("cab_f_9km_side", 9000f, -75f, 0f) })
            {
                var lift = new Vector3(0f, alt - 110f - lifted, 0f);
                eye.parent.position += lift; if (sv) sv.transform.position += lift;
                lifted += lift.y;
                Shader.SetGlobalFloat("_CamAltitude", eye.position.y + 14.1f);
                cam.transform.SetPositionAndRotation(eye.position, eye.rotation * Quaternion.Euler(-pitch, yaw, 0f));
                for (int warm = 0; warm < 3; warm++) cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
                File.WriteAllBytes($"Logs/previews/{n}.png", tex.EncodeToPNG());
            }
        }
        fd.SkyMat.SetFloat("_PhotoMix", 1f);
        RenderTexture.active = null;
        Debug.Log("CLOUDVIEWS done");
    }
}

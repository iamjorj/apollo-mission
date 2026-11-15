// What a wrong action does, played out for a few seconds before the MISSION LOST card and the rewind:
//  - Abort, the two ways Apollo could abort a climb:
//      * with the escape tower (until it was thrown off at 3:17): its solid motor fires through four nozzles canted
//        35 deg outward, about 650 kN for 3.2 s, pulling the command module off the rocket at about 7 g, the pitch motor
//        tipping it over; the Saturn's engines are cut. Then the tower is thrown off and the parachutes open
//        (two drogues, then three 25 m orange-and-white mains)
//      * later, with Columbia's own engine: the command and service modules separate from the rocket (thruster puffs)
//        and the service module's engine fires to bring the crew down safely
//  - Blackout: the main bus shorts and the cabin goes dark
// Everything is put back exactly as it was when the student rewinds.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Consequences : MonoBehaviour
{
    FlightDirector fd;
    Transform cm, les, sm;
    Vector3 cmPos, lesPos, smPos;
    Quaternion cmRot, lesRot, smRot;
    bool lesActive;
    readonly List<GameObject> made = new List<GameObject>();
    Image dark;
    float[] lightLevels;
    Func<(Vector3 focus, float dist)> oldFocus;
    bool aborting, blackedOut;
    public bool Aborting => aborting;

    public static Consequences On(FlightDirector fd)
    {
        var c = fd.GetComponent<Consequences>();
        if (!c) { c = fd.gameObject.AddComponent<Consequences>(); c.fd = fd; }
        return c;
    }

    Transform Saturn(string n)
    {
        foreach (var t in fd.SaturnRoot.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    Image Dark()
    {
        if (dark) return dark;
        var canvas = fd.GetComponentInChildren<Canvas>();
        var rt = UiKit.Full(canvas.transform, "ConsequenceDark");
        rt.SetSiblingIndex(Mathf.Max(0, canvas.transform.childCount - 3));     // under the cards, over the scene
        dark = UiKit.Box(rt, new Color(0f, 0f, 0f, 0f), 0);
        return dark;
    }

    // ---------------- the abort ----------------

    public IEnumerator Abort(Ascent climb, Action then, string caption = null)
    {
        aborting = true;
        cm = Saturn("CM"); les = Saturn("LES"); sm = Saturn("SM");
        if (cm) { cmPos = cm.localPosition; cmRot = cm.localRotation; }
        if (les) { lesPos = les.localPosition; lesRot = les.localRotation; lesActive = les.gameObject.activeSelf; }
        if (sm) { smPos = sm.localPosition; smRot = sm.localRotation; }
        bool tower = les && les.gameObject.activeInHierarchy && climb.Met < 197f;
        climb.Paused = true;
        var ex = fd.GetComponent<Exhaust>(); if (ex) ex.Cutoff = true;      // the abort shuts the Saturn's engines down
        fd.SetOutside(true);
        if (fd.HideWhileEntering != null) foreach (var r in fd.HideWhileEntering) if (r) r.enabled = true;
        oldFocus = fd.OutsideFocus;
        if (tower) yield return TowerAbort(climb, caption);
        else yield return EngineAbort(climb, caption);
        var img = Dark();
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f) { img.color = new Color(0f, 0f, 0f, t); yield return null; }
        img.color = Color.black;
        then?.Invoke();
    }

    IEnumerator TowerAbort(Ascent climb, string caption)
    {
        Transform root = fd.SaturnRoot;
        Vector3 up = root.up;
        fd.Explain(caption ?? "ABORT · the escape tower's motor fires: 650 kN for 3 seconds, pulling Columbia off the rocket at 7 g", 5f);
        // the four nozzles, canted 35 deg outward, at the bottom of the motor case (Saturn frame: y in metres)
        var flames = new List<Transform>();
        for (int i = 0; i < 4; i++)
        {
            float az = (45f + 90f * i) * Mathf.Deg2Rad;
            Vector3 radialL = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
            Vector3 nozzleL = radialL * 0.30f + Vector3.up * 103.7f;
            Vector3 dirL = (Vector3.down * Mathf.Cos(35f * Mathf.Deg2Rad) + radialL * Mathf.Sin(35f * Mathf.Deg2Rad)).normalized;
            var holder = new GameObject("EscapeNozzle" + i).transform;
            holder.SetParent(les, false);
            holder.position = root.TransformPoint(nozzleL);
            holder.rotation = Quaternion.FromToRotation(Vector3.down, root.TransformDirection(dirL));
            made.Add(holder.gameObject);
            flames.Add(Flame(holder, new Color(1f, 0.78f, 0.5f), 1.6f, 0.55f, 9f));
            Smoke(holder, 38f, new Color(0.86f, 0.85f, 0.83f, 0.55f), 2.2f, 7f, 220f);
        }
        oldFocus = fd.OutsideFocus;
        Vector3 cm0 = cm.position, les0 = les.position;
        Vector3 side = Vector3.Cross(up, Vector3.up).sqrMagnitude > 1e-3f ? Vector3.Cross(up, Vector3.up).normalized : Vector3.right;
        float travel = 0f, speed = 0f, pitch = 0f;
        Vector3 pivot = cm0;
        fd.OutsideFocus = () => (Vector3.Lerp(cm0, cm.position, 0.85f) + up * 4f, 55f);
        for (float t = 0f; t < 4.2f; t += Time.deltaTime)
        {
            bool burning = t < 3.2f;
            float a = burning ? 70f * Mathf.Clamp01(t / 0.15f) : -9.8f;            // about 7 g, then coasting
            speed += a * Time.deltaTime; travel += speed * Time.deltaTime;
            // the pitch motor at the top fires for half a second: the whole thing tips over, away from the rocket
            if (t > 0.1f && t < 2.5f) pitch = Mathf.Lerp(pitch, 28f, Time.deltaTime * 1.4f);
            var q = Quaternion.AngleAxis(pitch, side);
            Vector3 path = (q * up) * travel;
            cm.SetPositionAndRotation(cm0 + path, q * root.rotation * cmRot);
            les.SetPositionAndRotation(cm.position + q * (les0 - cm0), q * root.rotation * lesRot);
            foreach (var f in flames) if (f) f.gameObject.SetActive(burning);
            if (!burning) foreach (var ps in les.GetComponentsInChildren<ParticleSystem>()) { var em = ps.emission; em.rateOverTime = 0f; }
            yield return null;
        }
        // 14 s later: the tower thrown off, then the parachutes: the crew comes down safely
        fd.Explain("14 seconds later the tower is thrown off and the parachutes open. The crew splashes down safely in the Atlantic. There will be no Moon landing.", 6f);
        les.gameObject.SetActive(false);
        var canopy = Parachutes(cm);
        Vector3 hang = cm.position;
        fd.OutsideFocus = () => (cm.position + Vector3.up * 16f, 70f);
        for (float t = 0f; t < 3.2f; t += Time.deltaTime)
        {
            hang += Vector3.down * 9f * Time.deltaTime;                              // about 9 m/s under three mains
            float sway = Mathf.Sin(t * 1.3f) * 4f;
            cm.SetPositionAndRotation(hang, Quaternion.AngleAxis(sway, Vector3.forward) * Quaternion.FromToRotation(Vector3.up, Vector3.up) * root.rotation * cmRot);
            canopy.position = hang; canopy.rotation = Quaternion.AngleAxis(sway * 0.6f, Vector3.forward);
            yield return null;
        }
    }

    IEnumerator EngineAbort(Ascent climb, string caption)
    {
        Transform root = fd.SaturnRoot;
        Vector3 up = root.up;
        fd.Explain(caption ?? "ABORT · Columbia separates from the rocket and fires its own engine", 5f);
        Vector3 cm0 = cm.position, sm0 = sm ? sm.position : cm.position;
        // the service module's thrusters push it clear, then its big engine fires (hypergolic: an almost clear flame)
        var bottom = new GameObject("SPSNozzle").transform;
        bottom.SetParent(sm ? sm : cm, false);
        bottom.position = root.TransformPoint(Vector3.up * 93.9f);                // the bottom of the service module
        bottom.rotation = Quaternion.FromToRotation(Vector3.down, -up);
        made.Add(bottom.gameObject);
        var sps = Flame(bottom, new Color(0.6f, 0.7f, 1f), 0.08f, 0.9f, 7f);
        sps.gameObject.SetActive(false);
        var puffs = new List<ParticleSystem>();
        for (int i = 0; i < 4; i++)
        {
            float az = (7.25f + 90f * i) * Mathf.Deg2Rad;
            var q = new GameObject("RcsQuad" + i).transform;
            q.SetParent(sm ? sm : cm, false);
            q.position = root.TransformPoint(new Vector3(Mathf.Sin(az) * 2.0f, 97.05f, Mathf.Cos(az) * 2.0f));
            q.rotation = Quaternion.FromToRotation(Vector3.down, -up);
            made.Add(q.gameObject);
            puffs.Add(Smoke(q, 12f, new Color(1f, 1f, 1f, 0.7f), 0.3f, 0.8f, 120f));
        }
        fd.OutsideFocus = () => (Vector3.Lerp(sm0, cm.position, 0.7f), 48f);
        float speed = 0f, travel = 0f;
        for (float t = 0f; t < 4.6f; t += Time.deltaTime)
        {
            float a = t < 1.2f ? 0.9f : 3.0f;                                       // thrusters, then the engine (91 kN on 30 t)
            speed += a * Time.deltaTime; travel += speed * Time.deltaTime;
            if (t >= 1.2f && !sps.gameObject.activeSelf) { sps.gameObject.SetActive(true); foreach (var p in puffs) { var em = p.emission; em.rateOverTime = 0f; } }
            Vector3 d = up * travel;
            cm.position = cm0 + d; if (sm) sm.position = sm0 + d;
            yield return null;
        }
        fd.Explain("About 20 minutes later Columbia comes down under its parachutes in the Atlantic. The crew is safe, but there will be no Moon landing.", 6f);
        yield return new WaitForSeconds(1.2f);
    }

    // a flame: the engines' plume mesh along -y of the holder
    Transform Flame(Transform holder, Color tint, float heat, float width, float length)
    {
        var go = new GameObject("Flame");
        go.transform.SetParent(holder, false);
        go.AddComponent<MeshFilter>().sharedMesh = Exhaust.PlumeMesh();
        var r = go.AddComponent<MeshRenderer>();
        var m = new Material(fd.PlumeMat);
        m.SetColor("_Tint", tint); m.SetFloat("_Heat", heat);
        r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = new Vector3(width, length, width);
        return go.transform;
    }

    // smoke (or thruster puffs) blown along -y of the holder, left behind in the air
    ParticleSystem Smoke(Transform holder, float speed, Color c, float size, float life, float rate)
    {
        var go = new GameObject("Smoke");
        go.transform.SetParent(holder, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 100f; main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
        main.startColor = c; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 3000;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.15f; sh.rotation = new Vector3(90f, 0f, 0f);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 4f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.2f;   // the air slows the smoke
        var em = ps.emission; em.rateOverTime = rate;
        var rr = go.GetComponent<ParticleSystemRenderer>();
        rr.sharedMaterial = new Material(fd.TrailMat) { color = Color.white };
        ps.Play();
        made.Add(go);
        return ps;
    }

    // three ringsail main parachutes, 25 m across, orange and white gores, on risers to the capsule's top
    Transform Parachutes(Transform capsule)
    {
        var root = new GameObject("Parachutes").transform;
        root.position = capsule.position;
        made.Add(root.gameObject);
        var tex = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        for (int i = 0; i < 16; i++) tex.SetPixel(i, 0, i % 2 == 0 ? new Color(0.95f, 0.45f, 0.12f) : new Color(0.95f, 0.94f, 0.92f));
        tex.Apply();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { mainTexture = tex };
        mat.SetFloat("_Smoothness", 0.1f); mat.SetFloat("_Cull", 0f);
        var dome = Dome();
        var riserMat = new Material(fd.TrailMat) { color = new Color(0.9f, 0.9f, 0.9f, 0.8f) };
        for (int k = 0; k < 3; k++)
        {
            float az = k * 120f * Mathf.Deg2Rad;
            Vector3 off = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az)) * 13f + Vector3.up * 42f;
            var c = new GameObject("Main" + k).transform;
            c.SetParent(root, false);
            c.localPosition = off;
            c.localRotation = Quaternion.FromToRotation(Vector3.up, (off + Vector3.up * 20f).normalized);
            c.localScale = new Vector3(12.7f, 7f, 12.7f);
            c.gameObject.AddComponent<MeshFilter>().sharedMesh = dome;
            var mr = c.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            for (int j = 0; j < 6; j++)
            {
                float b = j * 60f * Mathf.Deg2Rad;
                var lr = new GameObject("Riser").AddComponent<LineRenderer>();
                lr.transform.SetParent(root, false);
                lr.useWorldSpace = false; lr.positionCount = 2; lr.widthMultiplier = 0.06f; lr.sharedMaterial = riserMat;
                lr.SetPosition(0, Vector3.up * 3.2f);
                lr.SetPosition(1, off + c.localRotation * (new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * 12.7f));
            }
        }
        return root;
    }

    // a shallow dome, open at the bottom (unit radius, height 1), u around for the gores
    static Mesh Dome()
    {
        const int seg = 32, rings = 8;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int j = 0; j <= rings; j++)
        {
            float a = j / (float)rings * Mathf.PI * 0.5f;
            for (int i = 0; i <= seg; i++)
            {
                float b = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Sin(a) * Mathf.Sin(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Cos(b)));
                uv.Add(new Vector2(i / (float)seg * 8f, j / (float)rings));
            }
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < seg; i++)
            {
                int a = j * (seg + 1) + i, b = a + seg + 1;
                t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ---------------- the main bus shorts: the floodlights die, a few flickers, then dark ----------------

    public IEnumerator Blackout(Action then)
    {
        blackedOut = true;
        var lights = fd.CabinLights ?? new Light[0];
        lightLevels = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) lightLevels[i] = lights[i] ? lights[i].intensity : 0f;
        fd.Explain("MAIN BUS SHORT · the cabin loses power", 4f);
        var img = Dark();
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            bool on = Mathf.PerlinNoise(t * 18f, 0.3f) > 0.62f;
            for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].intensity = on ? lightLevels[i] * 0.4f : 0f;
            img.color = new Color(0f, 0f, 0f, on ? 0.35f : 0.8f);
            yield return null;
        }
        foreach (var l in lights) if (l) l.intensity = 0f;
        img.color = new Color(0f, 0f, 0f, 0.86f);
        yield return new WaitForSeconds(0.8f);
        then?.Invoke();
    }

    // ---------------- everything back as it was ----------------

    public void Restore(Ascent climb)
    {
        if (aborting)
        {
            foreach (var g in made) if (g) Destroy(g);
            made.Clear();
            if (cm) { cm.localPosition = cmPos; cm.localRotation = cmRot; }
            if (sm) { sm.localPosition = smPos; sm.localRotation = smRot; }
            if (les) { les.localPosition = lesPos; les.localRotation = lesRot; les.gameObject.SetActive(lesActive); }
            var ex = fd.GetComponent<Exhaust>(); if (ex) ex.Cutoff = false;
            fd.OutsideFocus = oldFocus;
            fd.SetOutside(false);
            aborting = false;
        }
        if (blackedOut)
        {
            var lights = fd.CabinLights ?? new Light[0];
            for (int i = 0; i < lights.Length && lightLevels != null && i < lightLevels.Length; i++) if (lights[i]) lights[i].intensity = lightLevels[i];
            blackedOut = false;
        }
        if (dark) dark.color = new Color(0f, 0f, 0f, 0f);
        if (climb) climb.Paused = false;
    }
}

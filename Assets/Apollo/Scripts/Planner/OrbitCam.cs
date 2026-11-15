// Camera for the planner: drag to look around a body, scroll to zoom, and three framed views:
// Earth (Florida at liftoff), the whole trip at real scale, and the Moon (landing site).
using UnityEngine;
using UnityEngine.EventSystems;

public class OrbitCam : MonoBehaviour
{
    public enum View { Earth, Trip, Moon }
    public View Current { get; private set; } = View.Earth;

    Vector3 focus, focusTarget;
    float distance = 30f, distanceTarget = 30f;
    Quaternion orbit = Quaternion.identity, orbitTarget = Quaternion.identity;
    float minDist = 8f, maxDist = 2000f;

    void Start() { SpaceScene.Instance.Refresh(); Go(View.Earth, true); }

    public void Go(View v, bool instant = false)
    {
        var sc = SpaceScene.Instance;
        Current = v;
        Vector3 sun = Shader.GetGlobalVector("_SunDirWorld");
        switch (v)
        {
            case View.Earth:
            {
                // look at Kennedy Space Center from above the Atlantic, a little to the sunny side
                Vector3 k = sc.KennedyWorld().normalized;
                Vector3 dir = (k + 0.35f * Vector3.ProjectOnPlane(sun, k).normalized + 0.15f * Vector3.up).normalized;
                Set(Vector3.zero, dir, 21f, SpaceScene.EarthRadius * 1.25f, 400f);
                break;
            }
            case View.Trip:
            {
                Vector3 m = sc.MoonPos;
                Vector3 n = Vector3.Cross(m, sc.MoonLaunchPos).normalized;
                if (Vector3.Dot(n, Vector3.up) < 0) n = -n;
                Vector3 dir = (n * 0.8f + Vector3.Cross(n, m.normalized) * 0.35f + m.normalized * -0.2f).normalized;
                Set(m * 0.45f, dir, m.magnitude * 1.25f, 20f, 2000f);
                break;
            }
            case View.Moon:
            {
                // from the Earth side, so the near side and the landing sites face the camera
                Vector3 toEarth = (-sc.MoonPos).normalized;
                Vector3 dir = (toEarth + 0.25f * Vector3.up).normalized;
                Set(sc.MoonPos, dir, 6.2f, SpaceScene.MoonRadius * 1.3f, 60f);
                break;
            }
        }
        if (instant) { focus = focusTarget; distance = distanceTarget; orbit = orbitTarget; Apply(); }
    }

    void Set(Vector3 f, Vector3 dirFromFocus, float dist, float min, float max)
    {
        focusTarget = f;
        orbitTarget = Quaternion.LookRotation(-dirFromFocus, Vector3.up);
        distanceTarget = dist; minDist = min; maxDist = max;
    }

    void Update()
    {
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!overUi && (Input.GetMouseButton(0) || Input.GetMouseButton(1)))
        {
            float dx = Input.GetAxis("Mouse X"), dy = Input.GetAxis("Mouse Y");
            orbitTarget = Quaternion.AngleAxis(dx * 3f, Vector3.up) * orbitTarget;
            orbitTarget = orbitTarget * Quaternion.AngleAxis(-dy * 3f, Vector3.right);
        }
        if (!overUi)
        {
            float s = Input.mouseScrollDelta.y;
            if (s != 0) distanceTarget = Mathf.Clamp(distanceTarget * Mathf.Pow(0.88f, s), minDist, maxDist);
        }
        if (Current == View.Moon && SpaceScene.Instance) focusTarget = SpaceScene.Instance.MoonPos;

        float k = 1f - Mathf.Exp(-Time.deltaTime * 3.2f);
        focus = Vector3.Lerp(focus, focusTarget, k);
        distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(distance), Mathf.Log(distanceTarget), k));
        orbit = Quaternion.Slerp(orbit, orbitTarget, k);
        Apply();
    }

    void Apply() => transform.SetPositionAndRotation(focus - orbit * Vector3.forward * distance, orbit);
}

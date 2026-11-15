// Minimal UI building blocks in JetBrains Mono: rounded boxes, text, flat buttons, a chevron.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class UiKit
{
    public static readonly Color Panel = new Color(0.045f, 0.05f, 0.064f, 0.94f);
    public static readonly Color Card = new Color(0.04f, 0.045f, 0.058f, 0.985f);   // blending is linear: 0.94 still shows the Earth through
    public static readonly Color Text = new Color(0.94f, 0.95f, 0.96f);
    public static readonly Color Text2 = new Color(0.60f, 0.64f, 0.68f);
    public static readonly Color Text3 = new Color(0.36f, 0.39f, 0.43f);
    public static readonly Color Selected = new Color(0.94f, 0.95f, 0.96f, 1f);
    public static readonly Color Ink = new Color(0.05f, 0.055f, 0.065f);
    public static readonly Color Good = new Color(0.47f, 0.88f, 0.60f);
    public static readonly Color Bad = new Color(1f, 0.47f, 0.42f);
    public static readonly Color Line = new Color(1f, 1f, 1f, 0.07f);
    public static readonly Color Off = new Color(1f, 1f, 1f, 0.09f);

    static Sprite round;
    static TMP_FontAsset regular, bold;

    public static TMP_FontAsset Regular => regular ? regular : (regular = Resources.Load<TMP_FontAsset>("Fonts/JetBrainsMono-Regular SDF") ?? TMP_Settings.defaultFontAsset);
    public static TMP_FontAsset Bold => bold ? bold : (bold = Resources.Load<TMP_FontAsset>("Fonts/JetBrainsMono-Bold SDF") ?? Regular);

    // 64 px circle with a 32 px 9-slice border: any rounded rectangle from one sprite.
    public static Sprite Round
    {
        get
        {
            if (round != null) return round;
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(S / 2f - d + 0.5f)));
                }
            t.Apply();
            round = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(31, 31, 31, 31));
            return round;
        }
    }

    // A rounded rectangle drawn as a thin 1.5 px line (9-sliced): outlined buttons and panels.
    static Sprite outline;
    public static Sprite Outline
    {
        get
        {
            if (outline != null) return outline;
            const int S = 64; const float rad = 14f, stroke = 1.5f;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Vector2 c = new Vector2(S / 2f, S / 2f), h = new Vector2(S / 2f - 1f, S / 2f - 1f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    Vector2 q = new Vector2(Mathf.Abs(x + 0.5f - c.x), Mathf.Abs(y + 0.5f - c.y)) - (h - new Vector2(rad, rad));
                    float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - rad;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(stroke * 0.5f + 0.5f - Mathf.Abs(d + stroke * 0.5f))));
                }
            t.Apply();
            outline = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
            return outline;
        }
    }

    // a child that fills its parent
    public static RectTransform Full(Transform parent, string name)
    {
        var rt = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    public static Image OutlineBox(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Outline; img.type = Image.Type.Sliced; img.color = c; img.raycastTarget = false;
        return img;
    }

    // A crisp thin circle at a given size on screen (drawn at that size, so it never looks blurred)
    static readonly System.Collections.Generic.Dictionary<int, Sprite> rings = new System.Collections.Generic.Dictionary<int, Sprite>();
    public static Sprite Ring(int px)
    {
        if (rings.TryGetValue(px, out var sp) && sp != null) return sp;
        int S = Mathf.Clamp(px, 16, 1024);
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float r = S / 2f - 2f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1.25f - Mathf.Abs(d - r))));
            }
        t.Apply();
        sp = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        rings[px] = sp;
        return sp;
    }

    // A thin circle outline (for the suggested site on the Moon).
    static Sprite ringSprite;
    public static Sprite RingSprite
    {
        get
        {
            if (ringSprite != null) return ringSprite;
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(2.4f - Mathf.Abs(d - 28f))));
                }
            t.Apply();
            ringSprite = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return ringSprite;
        }
    }

    public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    public static Image Box(RectTransform rt, Color c, float radius)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        if (radius > 0f)
        {
            img.sprite = Round;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 32f / radius;
        }
        return img;
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color c, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box,
                                        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false, float spacing = 0f)
    {
        var rt = Rect(parent, "Text", anchor, pivot, pos, box);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = bold ? Bold : Regular;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align;
        t.raycastTarget = false; t.enableWordWrapping = true; t.richText = true;
        t.characterSpacing = spacing;
        if (c.r + c.g + c.b < 0.6f) t.fontSharedMaterial = DarkInk(t.font);
        return t;
    }

    // A soft dark shadow under light text, for text over bright 3D scenes.
    static Material shadowRegular, shadowBold;
    public static void Shadow(TMP_Text t)
    {
        bool b = t.font == Bold;
        var m = b ? shadowBold : shadowRegular;
        if (!m)
        {
            m = new Material(t.font.material) { name = t.font.name + " Shadow" };
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.8f));
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.35f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.7f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.25f);
            ShaderUtilities.UpdateShaderRatios(m);
            if (b) shadowBold = m; else shadowRegular = m;
        }
        t.fontSharedMaterial = m;
    }

    // Linear-space blending thins dark strokes on light fills; a slightly dilated face restores the weight.
    static Material darkRegular, darkBold;
    public static Material DarkInk(TMP_FontAsset f)
    {
        bool b = f == Bold;
        var m = b ? darkBold : darkRegular;
        if (m) return m;
        m = new Material(f.material) { name = f.name + " Ink" };
        m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.32f);
        ShaderUtilities.UpdateShaderRatios(m);
        if (b) darkBold = m; else darkRegular = m;
        return m;
    }

    // A flat button: invisible until hovered, or filled when 'fill' has alpha.
    public static Button Button(RectTransform rt, Color fill, float radius, Action onClick)
    {
        var img = Box(rt, fill.a > 0 ? fill : new Color(1, 1, 1, 0.001f), radius);
        img.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = fill.a > 0.5f ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(1f, 1f, 1f, 60f);
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        b.onClick.AddListener(() => onClick());
        return b;
    }

    public static Image Dot(Transform parent, Vector2 anchor, Vector2 pos, float d, Color c)
    {
        var rt = Rect(parent, "Dot", anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(d, d));
        return Box(rt, c, d / 2f);
    }

    // A thin "v" chevron made of two bars; rotate the returned holder 180 degrees to point up.
    public static RectTransform Chevron(Transform parent, Vector2 anchor, Vector2 pos, float size, Color c)
    {
        var holder = Rect(parent, "Chevron", anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
        float len = size * 0.62f, th = Mathf.Max(2f, size * 0.12f);
        for (int s = -1; s <= 1; s += 2)
        {
            var bar = Rect(holder, "Bar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(s * len * 0.33f, size * 0.02f), new Vector2(len, th));
            bar.localRotation = Quaternion.Euler(0, 0, s * 42f);
            Box(bar, c, th / 2f);
        }
        return holder;
    }
}

// For a button that turns white when hovered: its text and icons turn dark so they stay readable.
public class HoverInk : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    Graphic[] parts;
    Color[] normal;
    Material[] mats;

    void Collect()
    {
        var self = GetComponent<Graphic>();
        var list = new System.Collections.Generic.List<Graphic>();
        foreach (var g in GetComponentsInChildren<Graphic>(true)) if (g != self) list.Add(g);
        parts = list.ToArray();
        normal = new Color[parts.Length];
        mats = new Material[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            normal[i] = parts[i].color;
            if (parts[i] is TMP_Text t) mats[i] = t.fontSharedMaterial;
        }
    }

    public void OnPointerEnter(PointerEventData e) => Set(true);
    public void OnPointerExit(PointerEventData e) => Set(false);
    void OnDisable() => Set(false);

    void Set(bool dark)
    {
        if (parts == null) Collect();
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].color = dark ? UiKit.Ink : normal[i];
            if (parts[i] is TMP_Text t) t.fontSharedMaterial = dark ? UiKit.DarkInk(t.font) : mats[i];
        }
    }
}

// Click or drag along a track to set a 0..1 value.
public class DragTrack : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public Action<float> OnValue;
    public void OnPointerDown(PointerEventData e) => Set(e);
    public void OnDrag(PointerEventData e) => Set(e);
    void Set(PointerEventData e)
    {
        var rt = (RectTransform)transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var p))
            OnValue?.Invoke(Mathf.Clamp01((p.x - rt.rect.xMin) / rt.rect.width));
    }
}

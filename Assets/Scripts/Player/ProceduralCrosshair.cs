using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates a simple crosshair from two UI rectangles — no external
/// image files needed. Attach to the same GameObject as PlayerHUD,
/// or use standalone.
///
/// Setup:
///   1. Right-click Canvas → Create Empty. Name it "Crosshair".
///   2. Anchor it to center (Alt+Shift → center).
///   3. Set Width/Height to 32×32.
///   4. Add this script.
///   5. Set Color and thickness in the Inspector.
/// </summary>
public class ProceduralCrosshair : MonoBehaviour
{
    [SerializeField, Tooltip("Crosshair color.")]
    private Color color = Color.green;

    [SerializeField, Tooltip("Line thickness in pixels.")]
    private float thickness = 2f;

    [SerializeField, Tooltip("Total width/height of the crosshair.")]
    private float size = 32f;

    [SerializeField, Tooltip("Gap in the center (0 = no gap).")]
    private float gap = 6f;

    private void Start()
    {
        RectTransform parent = GetComponent<RectTransform>();
        parent.sizeDelta = new Vector2(size, size);

        // --- Horizontal line ---
        GameObject hLine = CreateLine("HLine");
        RectTransform hRt = hLine.GetComponent<RectTransform>();
        hRt.SetParent(parent, false);
        hRt.anchorMin = hRt.anchorMax = new Vector2(0.5f, 0.5f);
        hRt.sizeDelta = new Vector2(size, thickness);

        // --- Vertical line ---
        GameObject vLine = CreateLine("VLine");
        RectTransform vRt = vLine.GetComponent<RectTransform>();
        vRt.SetParent(parent, false);
        vRt.anchorMin = vRt.anchorMax = new Vector2(0.5f, 0.5f);
        vRt.sizeDelta = new Vector2(thickness, size);

        // --- Gap: overlay center with background-colored squares ---
        if (gap > 0f)
        {
            Color bg = Color.clear; // transparent gap
            if (GetComponentInParent<Canvas>() != null)
            {
                Image parentImg = GetComponentInParent<Canvas>().GetComponent<Image>();
                // For a transparent gap, we just overlay a small clear square.
                // Actually, simplest: make 4 separate line pieces instead.
            }
        }

        // If gap is requested, rebuild with 4 separate bars
        if (gap > 0f)
        {
            DestroyImmediate(hLine);
            DestroyImmediate(vLine);
            BuildWithGap(parent);
        }
    }

    private void BuildWithGap(RectTransform parent)
    {
        float halfGap = gap / 2f;
        float halfSize = size / 2f;
        float barLength = (size - gap) / 2f;

        // Top bar
        CreateBar(parent, "Top",    new Vector2(0, halfGap + barLength / 2f),  new Vector2(thickness, barLength));
        // Bottom bar
        CreateBar(parent, "Bottom", new Vector2(0, -(halfGap + barLength / 2f)), new Vector2(thickness, barLength));
        // Left bar
        CreateBar(parent, "Left",   new Vector2(-(halfGap + barLength / 2f), 0), new Vector2(barLength, thickness));
        // Right bar
        CreateBar(parent, "Right",  new Vector2(halfGap + barLength / 2f, 0),  new Vector2(barLength, thickness));
    }

    private void CreateBar(RectTransform parent, string name, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject bar = CreateLine(name);
        RectTransform rt = bar.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        bar.GetComponent<Image>().color = color;
    }

    private GameObject CreateLine(string name)
    {
        GameObject go = new GameObject(name);
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return go;
    }
}

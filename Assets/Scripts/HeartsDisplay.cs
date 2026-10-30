using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HeartsDisplay : MonoBehaviour
{
    [SerializeField] float pixelScale = 4f;
    [SerializeField] float spacing = 6f;
    [SerializeField] float popTime = 0.3f;
    [SerializeField] float popScale = 1.5f;

    // O = outline, R = fill, H = highlight.
    static readonly string[] HeartMap =
    {
        "..OOO...OOO..",
        ".ORRROOORRRO.",
        "ORRHRRRRRRRRO",
        "ORHRRRRRRRRRO",
        "ORRRRRRRRRRRO",
        ".ORRRRRRRRRO.",
        "..ORRRRRRRO..",
        "...ORRRRRO...",
        "....ORRRO....",
        ".....ORO.....",
        "......O......"
    };

    static readonly Color Outline = new Color(0.16f, 0.02f, 0.05f);
    static readonly Color Fill = new Color(0.88f, 0.1f, 0.16f);
    static readonly Color Highlight = new Color(1f, 0.72f, 0.78f);
    static readonly Color EmptyOutline = new Color(0.5f, 0.42f, 0.48f);
    static readonly Color EmptyFill = new Color(0.14f, 0.08f, 0.11f, 0.85f);

    Sprite fullSprite;
    Sprite emptySprite;
    Image[] images;
    int shown = -1;

    public void Set(int current, int max)
    {
        EnsureSprites();
        if (images == null || images.Length != max) Build(max);

        for (int i = 0; i < max; i++)
        {
            images[i].sprite = i < current ? fullSprite : emptySprite;
        }

        if (shown >= 0 && current < shown)
        {
            AudioManager.Play(Sfx.HeartLost);
            for (int i = Mathf.Max(current, 0); i < Mathf.Min(shown, max); i++)
            {
                StartCoroutine(Pop(images[i].rectTransform));
            }
        }
        shown = current;
    }

    void Build(int max)
    {
        if (images != null)
        {
            foreach (var image in images)
            {
                if (image != null) Destroy(image.gameObject);
            }
        }

        float width = HeartMap[0].Length * pixelScale;
        float height = HeartMap.Length * pixelScale;
        images = new Image[max];
        for (int i = 0; i < max; i++)
        {
            var go = new GameObject("Heart" + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-width * 0.5f - (max - 1 - i) * (width + spacing), -height * 0.5f);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            images[i] = image;
        }
        shown = -1;
    }

    IEnumerator Pop(RectTransform rt)
    {
        for (float t = 0f; t < popTime; t += Time.deltaTime)
        {
            float k = t / popTime;
            rt.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, k * k);
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    void EnsureSprites()
    {
        if (fullSprite != null) return;
        fullSprite = CreateSprite(Outline, Fill, Highlight);
        emptySprite = CreateSprite(EmptyOutline, EmptyFill, EmptyFill);
    }

    static Sprite CreateSprite(Color outline, Color fill, Color highlight)
    {
        int width = HeartMap[0].Length;
        int height = HeartMap.Length;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int row = 0; row < height; row++)
        {
            for (int x = 0; x < width; x++)
            {
                Color c;
                switch (HeartMap[row][x])
                {
                    case 'O': c = outline; break;
                    case 'R': c = fill; break;
                    case 'H': c = highlight; break;
                    default: c = Color.clear; break;
                }
                tex.SetPixel(x, height - 1 - row, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 1f);
    }
}

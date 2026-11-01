using UnityEngine;

public class CandySparkle : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField] int poolSize = 64;
    [SerializeField] float size = 0.06f;
    [SerializeField] int sortingOrder = 3;
    [SerializeField] float drag = 2.5f; // how fast a spark loses its speed (1/s); low values let it drift
    [SerializeField] Color[] palette =
    {
        new Color(1f, 1f, 1f),
        new Color(1f, 0.92f, 0.45f),
        new Color(1f, 0.6f, 0.85f),
        new Color(0.6f, 0.9f, 1f)
    };

    struct Spark
    {
        public SpriteRenderer renderer;
        public Vector3 velocity;
        public float age;
        public float life;
        public float size;
        public float angle;
        public float spin;
        public float seed;
        public Color color;
        public bool active;
    }

    Spark[] sparks;
    int next;

    public Material Material => material;

    // Makes a pool of its own with a different look (the ambient motes), sharing the material of another one.
    public static CandySparkle Create(Transform parent, Material material, Color[] palette, int poolSize, float size, int sortingOrder, float drag)
    {
        var go = new GameObject("Motes");
        go.SetActive(false); // Awake runs when it is switched on, after the settings below
        go.transform.SetParent(parent, false);
        var made = go.AddComponent<CandySparkle>();
        made.material = material;
        made.palette = palette;
        made.poolSize = poolSize;
        made.size = size;
        made.sortingOrder = sortingOrder;
        made.drag = drag;
        go.SetActive(true);
        return made;
    }

    void Awake()
    {
        var star = CreateStarSprite();
        sparks = new Spark[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("Spark");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = star;
            sr.sharedMaterial = material;
            sr.sortingOrder = sortingOrder;
            sr.enabled = false;
            go.transform.localScale = Vector3.zero;
            sparks[i].renderer = sr;
        }
    }

    public void Burst(Vector3 position, int count, float speed, float sizeScale = 1f)
    {
        Quaternion camRot = CameraRotation();
        for (int i = 0; i < count; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized;
            Vector3 velocity = camRot * (new Vector3(c.x, c.y, 0f) * speed * Random.Range(0.4f, 1f));
            Emit(position, velocity, Random.Range(0.35f, 0.7f), sizeScale * Random.Range(0.7f, 1.3f));
        }
    }

    public void Emit(Vector3 position, Vector3 velocity, float life, float sizeScale)
    {
        if (sparks == null) return;
        int i = next;
        next = (next + 1) % sparks.Length;

        ref Spark s = ref sparks[i];
        s.age = 0f;
        s.life = life;
        s.size = size * sizeScale;
        s.velocity = velocity;
        s.angle = Random.Range(0f, 90f);
        s.spin = Random.Range(-180f, 180f);
        s.seed = Random.value * 10f;
        s.color = palette[Random.Range(0, palette.Length)];
        s.active = true;
        s.renderer.transform.position = position;
        s.renderer.transform.localScale = Vector3.zero;
        s.renderer.color = new Color(s.color.r, s.color.g, s.color.b, 0f);
        s.renderer.enabled = true;
    }

    void Update()
    {
        Quaternion camRot = CameraRotation();
        float dt = Time.deltaTime;
        for (int i = 0; i < sparks.Length; i++)
        {
            ref Spark s = ref sparks[i];
            if (!s.active) continue;

            s.age += dt;
            float t = s.age / s.life;
            if (t >= 1f)
            {
                s.active = false;
                s.renderer.enabled = false;
                continue;
            }

            s.velocity *= Mathf.Max(0f, 1f - drag * dt);
            s.angle += s.spin * dt;

            var tr = s.renderer.transform;
            tr.position += s.velocity * dt;
            tr.rotation = camRot * Quaternion.Euler(0f, 0f, s.angle);

            float twinkle = 0.75f + 0.25f * Mathf.Sin(s.age * 28f + s.seed);
            float scale = s.size * Mathf.Sin(t * Mathf.PI) * twinkle;
            tr.localScale = new Vector3(scale, scale, 1f);
            s.renderer.color = new Color(s.color.r, s.color.g, s.color.b, 1f - t * t);
        }
    }

    static Quaternion CameraRotation()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.rotation : Quaternion.identity;
    }

    static Sprite CreateStarSprite()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float astroid = 1f - (Mathf.Sqrt(Mathf.Abs(u)) + Mathf.Sqrt(Mathf.Abs(v)));
                float glow = 0.4f * Mathf.Exp(-(u * u + v * v) * 12f);
                float a = Mathf.Clamp01(astroid * 1.4f + glow);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }
}

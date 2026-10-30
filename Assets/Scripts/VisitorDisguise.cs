using UnityEngine;

// Costume options for one visitor: changing the fur (dye or paint) and wearing prop parts.
// Swaps between shared material assets (no material instances) and toggles prop children,
// so any combination of the visitor's disguise elements can be put together at runtime.
// Never changes who the visitor is (Visitor.kind).
public class VisitorDisguise : MonoBehaviour
{
    [SerializeField] Material naturalFur;
    [SerializeField] Material disguiseFur;
    [SerializeField] GameObject[] props;

    Renderer[] renderers;

    // Disguise elements: the fur change plus each prop.
    public int ElementCount => 1 + props.Length;

    // Wears `count` randomly chosen elements (clamped to 1..ElementCount).
    public void Randomize(int count)
    {
        int n = ElementCount;
        count = Mathf.Clamp(count, 1, n);
        int mask = 0;
        for (int picked = 0; picked < count;)
        {
            int bit = 1 << Random.Range(0, n);
            if ((mask & bit) != 0) continue;
            mask |= bit;
            picked++;
        }
        Apply(mask);
    }

    // Bit 0 = fur change, bit i (i >= 1) = props[i - 1]. 0 removes the disguise.
    public void Apply(int mask)
    {
        SetFur((mask & 1) != 0 ? disguiseFur : naturalFur);
        for (int i = 0; i < props.Length; i++)
        {
            props[i].SetActive((mask & (1 << (i + 1))) != 0);
        }
    }

    [ContextMenu("Random Disguise")]
    void RandomizePreview()
    {
        Randomize(Random.Range(1, ElementCount + 1));
    }

    void SetFur(Material fur)
    {
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == naturalFur || mats[i] == disguiseFur)
                {
                    mats[i] = fur;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}

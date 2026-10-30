using System.Collections.Generic;
using UnityEngine;

// Costume options for one visitor: changing the fur (dye or paint) and wearing prop parts.
// Swaps between shared material assets (no material instances) and toggles prop children,
// so any combination of the visitor's disguise elements can be put together at runtime.
// A palm blast can knock worn props off one at a time (DropOneFakeProp); a dropped prop is gone
// for good. Never changes who the visitor is (Visitor.kind).
public class VisitorDisguise : MonoBehaviour
{
    [SerializeField] Material naturalFur;
    // Fur change levels from sloppiest (patchy, partial dye) to a full dye job.
    [SerializeField] Material[] dyeLevels;
    // All of these are fake (look-alike) props; real parts are never listed here.
    [SerializeField] GameObject[] props;

    [Header("Knocked-off props")]
    [SerializeField] float dropDuration = 0.7f;
    [SerializeField] float dropSpeed = 1.6f;
    [SerializeField] float dropLift = 2.6f;
    [SerializeField] float dropSpin = 720f;
    [SerializeField] float gravity = 9.8f;

    class Flight
    {
        public Transform prop;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public Vector3 center;      // world point it tumbles around
        public Vector3 centerInProp;
        public Quaternion startRotation;
        public Vector3 velocity;
        public Vector3 spinAxis;
        public float groundY;
        public bool bounced;
        public float time;
    }

    Renderer[] renderers;
    int droppedMask;
    readonly List<Flight> flights = new List<Flight>();

    // Disguise elements: the fur change plus each prop.
    public int ElementCount => 1 + props.Length;

    // Fake props currently worn (dropped ones no longer count).
    public int FakePropCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < props.Length; i++)
            {
                if (IsWorn(i)) count++;
            }
            return count;
        }
    }

    // Wears `count` randomly chosen elements (clamped to 1..ElementCount). skill (0..1) picks how
    // thorough the fur change is when it is one of them: low skill dyes a few patches, high skill all of it.
    public void Randomize(int count, float skill)
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
        float expected = Mathf.Clamp01(skill) * (dyeLevels.Length - 1);
        int level = Mathf.Clamp(Mathf.RoundToInt(expected + Random.Range(-0.6f, 0.6f)), 0, dyeLevels.Length - 1);
        Apply(mask, level);
    }

    // Bit 0 = fur change at dyeLevels[dyeLevel], bit i (i >= 1) = props[i - 1]. 0 removes the disguise.
    // Props already knocked off stay off.
    public void Apply(int mask, int dyeLevel)
    {
        SetFur((mask & 1) != 0 ? dyeLevels[dyeLevel] : naturalFur);
        for (int i = 0; i < props.Length; i++)
        {
            props[i].SetActive((mask & (1 << (i + 1))) != 0 && (droppedMask & (1 << i)) == 0);
        }
    }

    // Knocks one randomly chosen worn fake prop off, flying along `direction` (camera -> visitor) and
    // up, tumbling, bouncing once and vanishing. Returns false (and does nothing) when no fake prop is
    // worn, or while a ghost sheet covers the visitor.
    public bool DropOneFakeProp(Vector3 direction)
    {
        if (TryGetComponent(out GhostSheet sheet) && sheet.Covered) return false;
        int worn = FakePropCount;
        if (worn == 0) return false;

        int pick = Random.Range(0, worn);
        int index = -1;
        for (int i = 0; i < props.Length; i++)
        {
            if (IsWorn(i) && pick-- == 0)
            {
                index = i;
                break;
            }
        }
        droppedMask |= 1 << index;

        var prop = props[index].transform;
        var r = prop.GetComponentInChildren<Renderer>();
        Vector3 center = r != null ? r.bounds.center : prop.position;
        Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : -transform.forward;
        var flight = new Flight
        {
            prop = prop,
            localPosition = prop.localPosition,
            localRotation = prop.localRotation,
            localScale = prop.localScale,
            center = center,
            centerInProp = prop.InverseTransformPoint(center),
            velocity = forward * dropSpeed + Vector3.up * dropLift,
            spinAxis = Random.onUnitSphere,
            groundY = transform.position.y,
        };
        prop.SetParent(null, true); // fly free of the visitor (it is being knocked down)
        flight.startRotation = prop.rotation;
        flights.Add(flight);
        AudioManager.Play(Sfx.PropPop);
        return true;
    }

    bool IsWorn(int i) => props[i].activeSelf && (droppedMask & (1 << i)) == 0;

    void Update()
    {
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var f = flights[i];
            f.time += Time.deltaTime;
            f.velocity.y -= gravity * Time.deltaTime;
            f.center += f.velocity * Time.deltaTime;
            if (!f.bounced && f.velocity.y < 0f && f.center.y < f.groundY + 0.05f)
            {
                f.bounced = true;
                f.velocity = new Vector3(f.velocity.x * 0.5f, -f.velocity.y * 0.4f, f.velocity.z * 0.5f);
            }
            float t = Mathf.Clamp01(f.time / dropDuration);
            float scale = t < 0.7f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
            f.prop.rotation = Quaternion.AngleAxis(dropSpin * f.time, f.spinAxis) * f.startRotation;
            f.prop.localScale = f.localScale * scale;
            f.prop.position = f.center - f.prop.rotation * Vector3.Scale(f.centerInProp, f.prop.lossyScale);
            if (t < 1f) continue;

            // Done: park it back under the visitor, switched off, in its original pose.
            f.prop.gameObject.SetActive(false);
            f.prop.SetParent(transform, false);
            f.prop.localPosition = f.localPosition;
            f.prop.localRotation = f.localRotation;
            f.prop.localScale = f.localScale;
            flights.RemoveAt(i);
        }
    }

    void OnDestroy()
    {
        // Props in flight are not our children, so they would outlive a destroyed visitor.
        foreach (var f in flights)
        {
            if (f.prop != null) Destroy(f.prop.gameObject);
        }
    }

    [ContextMenu("Random Disguise")]
    void RandomizePreview()
    {
        Randomize(Random.Range(1, ElementCount + 1), Random.value);
    }

    // Play-mode testing from the component's context menu.
    [ContextMenu("Drop One Fake Prop")]
    void DropPreview()
    {
        DropOneFakeProp(Camera.main != null ? transform.position - Camera.main.transform.position : -transform.forward);
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
                if (mats[i] != null && (mats[i] == naturalFur || System.Array.IndexOf(dyeLevels, mats[i]) >= 0))
                {
                    mats[i] = fur;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}

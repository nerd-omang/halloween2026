using System;
using System.Collections.Generic;
using UnityEngine;

// Ghost-sheet disguise: a white sheet over the whole visitor that hides the look clues. The same component can
// wear a pumpkin helmet instead (a lobed jack-o'-lantern shell, a second cover): everything below applies to
// whichever cover is on, except that the pumpkin does not trail behind while walking.
// Wear() shows the sheet and switches off the renderers under it (fur, dye, markings, glasses...),
// except the parts listed in `protrusions` (horns, ears, snout, tails, sticking-out props): those stay
// visible in the plain sheet-cloth material, slightly inflated around the body center, so they read as
// cloth pushed up by whatever is underneath (a wagging tail keeps wagging). BlowOff() restores everything,
// sends the sheet tumbling up and away as if hit by a palm blast and raises Removed when it is gone.
// The hem flutter is in the VisitorSheet shader; this component only adds a trailing lean while
// the visitor walks. A visitor wears a sheet at most once.
public class GhostSheet : MonoBehaviour
{
    [SerializeField] Transform sheet;
    // Optional second cover. Wear() picks it with pumpkinChance, Wear(true) forces it.
    [SerializeField] Transform pumpkin;
    [SerializeField, Range(0f, 1f)] float pumpkinChance = 0.35f;
    [SerializeField] float blowOffDuration = 0.5f;
    [SerializeField] float flyDistance = 2.5f;
    [SerializeField] float flyRise = 1.2f;
    [SerializeField] float spinDegrees = 420f;
    [SerializeField] float walkLean = 8f;
    [SerializeField] float leanSpeed = 4f;
    // Sheet point it tumbles around while flying, in the visitor's local space.
    [SerializeField] Vector3 flyPivot = new Vector3(0f, 0.45f, 0f);

    [Header("Bumps under the sheet")]
    // Parts that push the cloth up. Only those currently active (worn / present) show.
    [SerializeField] Transform[] protrusions;
    // Shared sheet-cloth material asset (same shader and color as the sheet, without hem flutter).
    [SerializeField] Material bumpMaterial;
    // Same for the parts pushing through the pumpkin helmet.
    [SerializeField] Material pumpkinBumpMaterial;
    [SerializeField] float bumpScale = 1.12f;
    // Bumps inflate away from this point (the body ball's center), in the visitor's local space.
    [SerializeField] Vector3 bumpCenter = new Vector3(0f, 0.42f, 0f);

    public bool Covered { get; private set; }
    public bool IsPumpkin => pumpkin != null && cover == pumpkin;
    public float BlowOffDuration => blowOffDuration;
    public event Action Removed;

    readonly List<Renderer> hidden = new List<Renderer>();
    readonly List<(Renderer renderer, Material[] materials)> bumpRenderers = new List<(Renderer, Material[])>();
    readonly List<(Transform part, Vector3 position, Vector3 scale)> bumpParts = new List<(Transform, Vector3, Vector3)>();
    Visitor visitor;
    Transform cover; // the cover being worn (sheet or pumpkin)
    bool used;
    Vector3 lastPosition;
    Vector3 leanAxis = Vector3.right;
    float lean;

    bool blowing;
    float blowTime;
    Vector3 blowDirection;
    Vector3 spinAxis;
    Vector3 startPivot;
    Vector3 pivotInSheet;
    Quaternion startRotation;

    void Awake()
    {
        visitor = GetComponent<Visitor>();
    }

    public void Wear() => Wear(pumpkin != null && UnityEngine.Random.value < pumpkinChance);

    public void Wear(bool usePumpkin)
    {
        if (Covered || used) return;
        Covered = true;
        used = true;
        cover = usePumpkin && pumpkin != null ? pumpkin : sheet;
        hidden.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || IsCover(r.transform) || IsProtrusion(r.transform)) continue;
            r.enabled = false;
            hidden.Add(r);
        }
        cover.localRotation = Quaternion.identity;
        ShowBumps();
        cover.gameObject.SetActive(true);
        lastPosition = transform.position;
        lean = 0f;
    }

    public void BlowOff(Vector3 direction)
    {
        if (!Covered || blowing) return;
        Covered = false;
        foreach (var r in hidden)
        {
            if (r != null) r.enabled = true;
        }
        hidden.Clear();
        RestoreBumps();

        AudioManager.Play(Sfx.SheetBlowOff);
        blowing = true;
        blowTime = 0f;
        blowDirection = direction.sqrMagnitude > 1e-6f ? direction.normalized : -transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(blowDirection, Vector3.up);
        spinAxis = flat.sqrMagnitude > 1e-6f ? Vector3.Cross(flat, Vector3.up).normalized : transform.right;
        startPivot = transform.TransformPoint(flyPivot);
        pivotInSheet = cover.InverseTransformPoint(startPivot);
        cover.SetParent(null, true); // fly free of the visitor's bobbing
        startRotation = cover.rotation;
    }

    bool IsCover(Transform t) => t.IsChildOf(sheet) || (pumpkin != null && t.IsChildOf(pumpkin));

    bool IsProtrusion(Transform t)
    {
        if (protrusions == null) return false;
        foreach (var p in protrusions)
        {
            if (p != null && t.IsChildOf(p)) return true;
        }
        return false;
    }

    // Swaps the protruding parts to the shared cloth material (no instances) and inflates them.
    void ShowBumps()
    {
        bumpRenderers.Clear();
        bumpParts.Clear();
        var clothMaterial = IsPumpkin && pumpkinBumpMaterial != null ? pumpkinBumpMaterial : bumpMaterial;
        if (protrusions == null || clothMaterial == null) return;
        foreach (var part in protrusions)
        {
            if (part == null) continue;
            bumpParts.Add((part, part.localPosition, part.localScale));
            // Ride on the cover (its rest pose is the visitor's origin) so bumps lean with the cloth.
            part.SetParent(cover, false);
            part.localPosition = bumpCenter + (part.localPosition - bumpCenter) * bumpScale;
            part.localScale *= bumpScale;
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            {
                var original = r.sharedMaterials;
                var cloth = new Material[original.Length];
                for (int i = 0; i < cloth.Length; i++) cloth[i] = clothMaterial;
                bumpRenderers.Add((r, original));
                r.sharedMaterials = cloth;
            }
        }
    }

    void RestoreBumps()
    {
        foreach (var (r, materials) in bumpRenderers)
        {
            if (r != null) r.sharedMaterials = materials;
        }
        foreach (var (part, position, scale) in bumpParts)
        {
            if (part == null) continue;
            part.SetParent(transform, false);
            part.localPosition = position;
            part.localScale = scale;
        }
        bumpRenderers.Clear();
        bumpParts.Clear();
    }

    void Update()
    {
        if (blowing)
        {
            UpdateBlowOff();
        }
        else if (Covered)
        {
            UpdateLean();
        }
    }

    void UpdateBlowOff()
    {
        blowTime += Time.deltaTime;
        float t = Mathf.Clamp01(blowTime / blowOffDuration);
        float ease = 1f - (1f - t) * (1f - t);
        Quaternion spin = Quaternion.AngleAxis(spinDegrees * ease, spinAxis);
        Vector3 pivot = startPivot + blowDirection * flyDistance * ease + Vector3.up * flyRise * ease;
        float scale = 1f - 0.7f * t * t;
        cover.rotation = spin * startRotation;
        cover.localScale = Vector3.one * scale;
        cover.position = pivot - cover.rotation * (pivotInSheet * scale);
        if (t < 1f) return;

        blowing = false;
        cover.gameObject.SetActive(false);
        cover.SetParent(transform, false);
        cover.localPosition = Vector3.zero;
        cover.localRotation = Quaternion.identity;
        cover.localScale = Vector3.one;
        Removed?.Invoke();
    }

    // The sheet's top trails behind while walking: tilt it against the direction of travel.
    void UpdateLean()
    {
        if (IsPumpkin) return; // a hard shell does not trail
        Vector3 velocity = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up);
        lastPosition = transform.position;
        if (velocity.sqrMagnitude > 1e-8f)
        {
            leanAxis = transform.InverseTransformDirection(Vector3.Cross(velocity, Vector3.up)).normalized;
        }
        bool moving = visitor != null ? visitor.IsMoving : velocity.sqrMagnitude > 1e-8f;
        lean = Mathf.MoveTowards(lean, moving ? walkLean : 0f, walkLean * leanSpeed * Time.deltaTime);
        cover.localRotation = Quaternion.AngleAxis(lean, leanAxis);
    }

    // Play-mode testing from the component's context menu (e.g. in the VisitorPreview scene).
    [ContextMenu("Wear")]
    void WearPreview() => Wear();

    [ContextMenu("Wear Pumpkin")]
    void WearPumpkinPreview() => Wear(true);

    [ContextMenu("Blow Off")]
    void BlowOffPreview() => BlowOff(Camera.main != null ? transform.position - Camera.main.transform.position : -transform.forward);

    void OnDestroy()
    {
        // While flying the sheet is not our child, so it would outlive a destroyed visitor.
        if (cover != null && cover.parent != transform) Destroy(cover.gameObject);
    }
}

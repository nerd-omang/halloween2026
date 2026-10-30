using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// A small stage far above the game world where visitor models are shown up close (review, gallery,
// dress-up). Enter() moves the main camera there, Exit() puts it back. Dragging with the left mouse
// button turns the model nearest to the cursor.
public class ModelStage : MonoBehaviour
{
    [SerializeField] OutfitCatalog catalog;
    [SerializeField] Material floorMaterial;
    [SerializeField] Vector3 origin = new Vector3(0f, 200f, 0f);
    [SerializeField] float pivotHeight = 0.45f;
    [SerializeField] float spacing = 1.5f;
    [SerializeField] float dragYawSpeed = 0.5f;
    [SerializeField] float dragPitchSpeed = 0.25f;
    [SerializeField] float maxPitch = 35f;

    class Slot
    {
        public Transform pivot;
        public GameObject model;
        public float yaw;
        public float pitch;
    }

    readonly List<Slot> slots = new List<Slot>();
    Camera cam;
    Vector3 savedPosition;
    Quaternion savedRotation;
    GameObject floor;
    int dragging = -1;

    public bool Active { get; private set; }
    public int Count => slots.Count;

    public void Enter()
    {
        if (Active) return;
        cam = Camera.main;
        if (cam == null) return;
        savedPosition = cam.transform.position;
        savedRotation = cam.transform.rotation;
        Active = true;

        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "StageFloor";
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.position = origin + Vector3.down * 0.05f;
            floor.transform.localScale = new Vector3(9f, 0.05f, 9f);
            if (floorMaterial != null) floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        }
        floor.SetActive(true);
    }

    public void Exit()
    {
        if (!Active) return;
        Clear();
        floor.SetActive(false);
        cam.transform.SetPositionAndRotation(savedPosition, savedRotation);
        Active = false;
    }

    // Shows the outfits side by side. viewportX is where (0..1 across the screen) the group is centered,
    // so a screen can keep a control column on one side.
    public void Show(IList<Outfit> outfits, float viewportX = 0.5f)
    {
        if (!Active) return;
        Clear();

        int n = outfits.Count;
        bool group = n > 1;
        float distance = group ? 3.2f : 2.2f;
        float height = group ? 1f : 0.9f;
        float pitch = group ? 12f : 6f;
        cam.transform.SetPositionAndRotation(origin + new Vector3(0f, height, -distance), Quaternion.Euler(pitch, 0f, 0f));

        float halfWidth = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect * distance;
        float centerX = (viewportX - 0.5f) * 2f * halfWidth;
        for (int i = 0; i < n; i++)
        {
            float x = centerX + (i - (n - 1) * 0.5f) * spacing;
            var pivot = new GameObject("Slot" + i).transform;
            pivot.SetParent(transform, false);
            pivot.position = origin + new Vector3(x, pivotHeight, 0f);
            var slot = new Slot { pivot = pivot };
            slot.model = MakeModel(slot, outfits[i]);
            slots.Add(slot);
        }
    }

    // Changes the look in one slot, keeping how far the player has turned it.
    public void SetOutfit(int index, Outfit outfit)
    {
        if (index < 0 || index >= slots.Count) return;
        var slot = slots[index];
        if (slot.model != null) Destroy(slot.model);
        slot.model = MakeModel(slot, outfit);
    }

    public float SlotViewportX(int index)
    {
        return cam.WorldToViewportPoint(slots[index].pivot.position).x;
    }

    GameObject MakeModel(Slot slot, Outfit outfit)
    {
        var model = catalog.CreateModel(outfit, slot.pivot);
        model.transform.localPosition = new Vector3(0f, -pivotHeight, 0f);
        return model;
    }

    void Clear()
    {
        foreach (var slot in slots)
        {
            if (slot.pivot != null) Destroy(slot.pivot.gameObject);
        }
        slots.Clear();
        dragging = -1;
    }

    void Update()
    {
        if (!Active || slots.Count == 0) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 position = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame && !UiKit.OverControl(position))
        {
            dragging = NearestSlot(position.x);
        }
        if (!mouse.leftButton.isPressed) dragging = -1;
        if (dragging < 0) return;

        Vector2 delta = mouse.delta.ReadValue();
        var slot = slots[dragging];
        slot.yaw += delta.x * dragYawSpeed;
        slot.pitch = Mathf.Clamp(slot.pitch - delta.y * dragPitchSpeed, -maxPitch, maxPitch);
        slot.pivot.rotation = Quaternion.Euler(slot.pitch, slot.yaw, 0f);
    }

    int NearestSlot(float screenX)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            float d = Mathf.Abs(cam.WorldToScreenPoint(slots[i].pivot.position).x - screenX);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }
}

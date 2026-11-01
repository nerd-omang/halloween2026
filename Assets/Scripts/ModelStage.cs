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
    [SerializeField] Visitor[] guestPrefabs = new Visitor[SpecialGuest.Count]; // by SpecialGuest id
    [SerializeField] Vector3 origin = new Vector3(0f, 200f, 0f);
    [SerializeField] float pivotHeight = 0.45f;
    [SerializeField] float spacing = 1.5f;
    [SerializeField] float dragYawSpeed = 0.5f;
    [SerializeField] float dragPitchSpeed = 0.25f;
    [SerializeField] float maxPitch = 35f;
    const float ModelHalfWidth = 0.7f; // room kept around one model (arms, props, tail)
    [SerializeField] float modelLift = 0.3f; // lowers the camera so models sit higher on screen, clear of the text below
    [SerializeField] float minZoom = 0.6f;
    [SerializeField] float maxZoom = 3f;

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
    PorchFrame porch; // follows the camera, so on the stage its posts would cover the outer models
    int dragging = -1;
    Vector3 basePosition;
    Quaternion baseRotation;
    float baseDistance = 2.2f;
    Vector3 focus; // the models' center: zooming moves the camera straight toward or away from it
    float zoom = 1f;
    Vector2 pan; // camera-space offset in world units
    float lastPinch;
    Slot spinSlot;
    float spinTime;
    float spinDuration;

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

        if (porch == null) porch = FindFirstObjectByType<PorchFrame>(FindObjectsInactive.Include);
        if (porch != null) porch.gameObject.SetActive(false);
    }

    public void Exit()
    {
        if (!Active) return;
        Clear();
        floor.SetActive(false);
        if (porch != null) porch.gameObject.SetActive(true);
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
        PlaceCamera(n, viewportX, out float centerX);
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

    // Camera framing for n models, and the x where the group is centered so it sits at viewportX on screen.
    void PlaceCamera(int n, float viewportX, out float centerX)
    {
        bool group = n > 1;
        float distance = group ? 3.2f : 2.2f;
        float height = group ? 1f : 0.9f;
        float pitch = group ? 12f : 6f;

        // Back away until the whole row fits on the side of the screen the group is centered on
        // (a control column on the left leaves less room on the right than the screen center suggests).
        float perDistance = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;
        float room = 2f * Mathf.Min(viewportX, 1f - viewportX) * perDistance;
        float halfExtent = (n - 1) * 0.5f * spacing + ModelHalfWidth;
        if (room > 0.01f) distance = Mathf.Max(distance, halfExtent / room);
        basePosition = origin + new Vector3(0f, height - modelLift, -distance);
        baseRotation = Quaternion.Euler(pitch, 0f, 0f);
        baseDistance = distance;
        zoom = 1f;
        pan = Vector2.zero;
        ApplyView();

        float halfWidth = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect * distance;
        centerX = (viewportX - 0.5f) * 2f * halfWidth;
        focus = origin + new Vector3(centerX, pivotHeight, 0f);
        ApplyView();
    }

    // Shows one special guest (SpecialGuest id) alone, with the same camera and controls as Show().
    public void ShowGuest(int guestId, float viewportX = 0.5f)
    {
        if (!Active) return;
        Clear();
        if (guestId < 0 || guestId >= guestPrefabs.Length || guestPrefabs[guestId] == null) return;

        PlaceCamera(1, viewportX, out float centerX);
        var pivot = new GameObject("Slot0").transform;
        pivot.SetParent(transform, false);
        pivot.position = origin + new Vector3(centerX, pivotHeight, 0f);
        var slot = new Slot { pivot = pivot };
        var visitor = Instantiate(guestPrefabs[guestId], pivot);
        visitor.enabled = false; // no waddling on the stage
        visitor.transform.localPosition = new Vector3(0f, -pivotHeight, 0f);
        visitor.transform.localRotation = Quaternion.identity;
        slot.model = visitor.gameObject;
        slots.Add(slot);
    }

    // Changes the look in one slot, keeping how far the player has turned it.
    public void SetOutfit(int index, Outfit outfit)
    {
        if (index < 0 || index >= slots.Count) return;
        var slot = slots[index];
        if (slot.model != null) Destroy(slot.model);
        slot.model = MakeModel(slot, outfit);
    }

    // Turns the first model once around, front to front, in the given time.
    public void SpinOnce(float seconds)
    {
        if (slots.Count == 0 || seconds <= 0f) return;
        spinSlot = slots[0];
        spinTime = 0f;
        spinDuration = seconds;
    }

    // Puts the camera back where Show() left it and turns every model to face front again.
    public void ResetView()
    {
        if (!Active) return;
        zoom = 1f;
        pan = Vector2.zero;
        ApplyView();
        foreach (var slot in slots)
        {
            slot.yaw = 0f;
            slot.pitch = 0f;
            slot.pivot.rotation = Quaternion.identity;
        }
    }

    void ApplyView()
    {
        // Zooming slides the camera along the line to the models' center, so the models stay where they are on
        // screen and only grow; panning then slides the camera sideways and up/down.
        Vector3 zoomed = focus + (basePosition - focus) / zoom;
        cam.transform.SetPositionAndRotation(zoomed + baseRotation * new Vector3(-pan.x, -pan.y, 0f), baseRotation);
    }

    // Mouse wheel, two-finger pinch (zoom) and right/middle drag or two-finger drag (move the view).
    void UpdateView()
    {
        float factor = 1f;
        Vector2 move = Vector2.zero;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && !UiKit.OverControl(GameInput.PointerPosition)) factor *= wheel > 0f ? 1.1f : 1f / 1.1f;
            if (mouse.rightButton.isPressed || mouse.middleButton.isPressed) move = mouse.delta.ReadValue();
        }

        var touch = Touchscreen.current;
        if (touch != null && touch.touches.Count >= 2 && touch.touches[0].press.isPressed && touch.touches[1].press.isPressed)
        {
            Vector2 a = touch.touches[0].position.ReadValue();
            Vector2 b = touch.touches[1].position.ReadValue();
            float pinch = (a - b).magnitude;
            if (lastPinch > 1f && pinch > 1f) factor *= pinch / lastPinch;
            lastPinch = pinch;
            move = (touch.touches[0].delta.ReadValue() + touch.touches[1].delta.ReadValue()) * 0.5f;
        }
        else
        {
            lastPinch = 0f;
        }

        if (factor == 1f && move == Vector2.zero) return;
        zoom = Mathf.Clamp(zoom * factor, minZoom, maxZoom);
        float perPixel = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * baseDistance / Mathf.Max(1, Screen.height);
        pan += move * perPixel / zoom;
        ApplyView();
    }

    bool TwoFingers
    {
        get
        {
            var touch = Touchscreen.current;
            return touch != null && touch.touches.Count >= 2 && touch.touches[1].press.isPressed;
        }
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
        spinSlot = null;
    }

    void Update()
    {
        if (!Active || slots.Count == 0) return;
        UpdateView();
        if (spinSlot != null)
        {
            spinTime += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(spinTime / spinDuration);
            spinSlot.yaw = 360f * Mathf.SmoothStep(0f, 1f, u);
            spinSlot.pivot.rotation = Quaternion.Euler(spinSlot.pitch, spinSlot.yaw, 0f);
            if (u >= 1f) spinSlot = null;
        }
        if (TwoFingers || (Mouse.current != null && (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed)))
        {
            dragging = -1;
            return;
        }
        Vector2 position = GameInput.PointerPosition;
        if (GameInput.PointerPressedThisFrame && !UiKit.OverControl(position))
        {
            dragging = NearestSlot(position.x);
        }
        if (!GameInput.PointerHeld) dragging = -1;
        if (dragging < 0) return;

        Vector2 delta = GameInput.PointerDelta;
        var slot = slots[dragging];
        // The surface under the cursor follows the drag: drag right turns the front to the right
        // (negative yaw), drag up tips the front up (positive pitch).
        slot.yaw -= delta.x * dragYawSpeed;
        slot.pitch = Mathf.Clamp(slot.pitch + delta.y * dragPitchSpeed, -maxPitch, maxPitch);
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

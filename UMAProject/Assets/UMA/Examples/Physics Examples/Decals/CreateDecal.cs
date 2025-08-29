using UMA;
using UMA.CharacterSystem;
using UnityEngine;

/// <summary>
/// Runtime helper to orbit a camera around an UMA avatar and place decal slots on left click.
/// </summary>
public class CreateDecal : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used for orbiting & raycasting.")]
    public Camera OrbitCamera;
    [Tooltip("UMA avatar to target.")]
    public DynamicCharacterAvatar Avatar;
    [Tooltip("OverlayDataAsset used for the decal (must reference the correct UMAMaterial).")]
    public OverlayDataAsset DecalOverlay;
    [Tooltip("(Optional) Unity Material for visual reference (not used by DecalSlotBuilder).")]
    public Material DecalMaterialForSubmesh;

    [Header("Decal Settings")]
    [Tooltip("World-space radius for decal selection.")]
    public float DecalRadius = 0.05f;
    [Tooltip("Rotation around surface normal (degrees, clockwise looking along normal).")]
    public float DecalRotationDegrees = 0f;

    [Header("Orbit Settings")]
    [Tooltip("Offset from avatar root used as orbit pivot.")]
    public Vector3 OrbitOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("Horizontal orbit sensitivity (degrees per normalized screen movement).")]
    public float OrbitSensitivityX = 180f;
    [Tooltip("Vertical orbit sensitivity (degrees per normalized screen movement).")]
    public float OrbitSensitivityY = 120f;
    [Tooltip("Clamp for vertical orbit (min pitch).")]
    public float MinPitch = -80f;
    [Tooltip("Clamp for vertical orbit (max pitch).")]
    public float MaxPitch = 80f;
    [Tooltip("Scroll wheel zoom speed.")]
    public float ZoomSensitivity = 2f;
    [Tooltip("Minimum orbit distance.")]
    public float MinDistance = 2f;
    [Tooltip("Maximum orbit distance.")]
    public float MaxDistance = 10f;

    [Header("Input")]
    [Tooltip("Hold this mouse button (1 = right) to orbit.")]
    public int OrbitMouseButton = 1;
    [Tooltip("Mouse button (0 = left) used to place decals.")]
    public int PlaceMouseButton = 0;

    // Internal orbit state
    private float _yaw;
    private float _pitch;
    private float _distance = 5f;
    private Vector3 _targetPos;

    private bool _initialized;

    void Start()
    {
        InitializeOrbit();
    }

    void InitializeOrbit()
    {
        if (Avatar == null || OrbitCamera == null)
            return;

        _targetPos = Avatar.transform.position + OrbitOffset;
        Vector3 camPos = OrbitCamera.transform.position;
        Vector3 dir = camPos - _targetPos;
        _distance = Mathf.Clamp(dir.magnitude, MinDistance, MaxDistance);
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.back;

        // Derive yaw/pitch
        _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        _pitch = Mathf.Asin(dir.normalized.y) * Mathf.Rad2Deg;
        _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);

        _initialized = true;
        UpdateCameraTransform();
    }

    void Update()
    {
        if (!_initialized)
            InitializeOrbit();

        if (Avatar == null || OrbitCamera == null)
            return;

        HandleOrbitInput();
        HandleZoom();
        UpdateCameraTransform();
        HandlePlacement();
    }

    private void HandleOrbitInput()
    {
        if (!Input.GetMouseButton(OrbitMouseButton))
            return;

        float dx = Input.GetAxis("Mouse X");
        float dy = Input.GetAxis("Mouse Y");

        // Normalize by screen size for consistent feel
        float normX = dx / Mathf.Max(1f, Screen.width);
        float normY = dy / Mathf.Max(1f, Screen.height);

        _yaw += normX * OrbitSensitivityX * Time.deltaTime * Screen.width;   // scale back by width to keep same overall sensitivity
        _pitch -= normY * OrbitSensitivityY * Time.deltaTime * Screen.height;

        _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
    }

    private void HandleZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            _distance -= scroll * ZoomSensitivity;
            _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
        }
    }

    private void UpdateCameraTransform()
    {
        if (Avatar == null) return;
        _targetPos = Avatar.transform.position + OrbitOffset;

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 camPos = _targetPos + rot * (Vector3.back * _distance);
        OrbitCamera.transform.position = camPos;
        OrbitCamera.transform.rotation = rot;
    }

    private void HandlePlacement()
    {
        if (!Input.GetMouseButtonDown(PlaceMouseButton))
            return;

        if (DecalOverlay == null || DecalOverlay.material == null)
        {
            Debug.LogWarning("DecalOverlay or its UMAMaterial is missing. Cannot place decal.");
            return;
        }

        if (Avatar == null || Avatar.umaData == null)
        {
            Debug.LogWarning("Avatar or UMAData not ready.");
            return;
        }

        Ray ray = OrbitCamera.ScreenPointToRay(Input.mousePosition);

        // Ensure click hit avatar hierarchy: perform physics raycast manually
        if (!Physics.Raycast(ray, out RaycastHit hitInfo, 1000f))
            return;
        if (!hitInfo.collider.transform.IsChildOf(Avatar.transform))
            return;

        // Build decal slot
        var slotAsset = DecalSlotBuilder.CreateDecalSlot(
            Avatar,
            ray,
            DecalRadius,
            DecalRotationDegrees,
            DecalOverlay.material,  // Using UMAMaterial from overlay (requirement: use existing Material field -> we leverage overlay's UMAMaterial)
            new DecalSlotBuilder.DecalBuildOptions
            {
                multithread = false,              // requirement: allocate per click, no async
                copyBlendshapes = true,
                facingThreshold = 0.15f
            });

        if (slotAsset == null)
        {
            Debug.Log("Decal creation produced no geometry (nothing within radius or facing threshold).");
            return;
        }
        UMAAssetIndexer.Instance.ProcessNewItem(slotAsset, false, false); // Ensure new asset is indexed

        // Wrap into SlotData and add overlay
        SlotData slotData = new SlotData(slotAsset);
        if (DecalOverlay != null)
        {
            var overlayInstance = new OverlayData(DecalOverlay);
            slotData.AddOverlay(overlayInstance);
        }
        slotData.expandAlongNormal = 10000; // Slight expansion to avoid z-fighting

        // Add (accumulate) into existing UMA recipe
        Avatar.umaData.umaRecipe.MergeSlot(slotData, true);
        Avatar.ForceUpdate(true, true, true);

        // Force rebuild – common UMA approach is to ask avatar to rebuild. If ForceUpdate exists in your version, prefer that.
        // Fallback minimal triggers if rebuild API differs:
        if (Avatar.umaData.Validate())
        {
            // Depending on UMA version you may need: Avatar.ForceUpdate(); or Avatar.BuildCharacter();
            // Provided signatures do not show those, so we rely on UMA's generator pipeline to notice the change.
            Debug.Log("Decal slot added: " + slotAsset.slotName);
        }
    }

    // Optional helper to allow external scripts to programmatically place a decal
    public void PlaceDecalAtCenter()
    {
        if (OrbitCamera == null) return;
        Ray ray = new Ray(OrbitCamera.transform.position, OrbitCamera.transform.forward);
        HandlePlacementRay(ray);
    }

    private void HandlePlacementRay(Ray ray)
    {
        // Duplicate logic if needed by other systems (not used directly in Update).
    }
}
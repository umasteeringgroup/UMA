using UnityEngine;
using System.Collections;
using UMA;
using UMA.CharacterSystem;
using UnityEngine.InputSystem;

namespace UMA.Dynamics.Examples
{
    public class UMAShooter : MonoBehaviour
    {
        private UMAPlayerActions controls;

        float impactEndTime = 0;
        int hits = 0;
        Rigidbody impactTarget = null;
        Vector3 impact;

        public Camera currentCamera;
        public LayerMask layers;
        public AudioClip Bang;
        public float announcerDelay = 0.5f;

        public AudioClip KillingSpree;
        public AudioClip HeadShot;
        public AudioClip HadToHurt;
        public GameObject Blood;
        public OverlayDataAsset bulletDecal;

        private void Awake()
        {
            controls = new UMAPlayerActions();

            // Bind actions
            controls.Player.Shoot.performed += ctx => OnShoot();
            controls.Player.GlobalUndo.performed += ctx => OnGlobalUndo();
            controls.Player.Undo.performed += ctx => OnUndo();
        }

        private void OnEnable()
        {
            controls.Enable();
        }

        private void OnDisable()
        {
            controls.Disable();
        }

        // -------------------------------
        // ACTION HANDLERS
        // -------------------------------

        private void OnGlobalUndo()
        {
            // Escape key behavior
            UMAPhysicsAvatar[] components = GameObject.FindObjectsByType<UMAPhysicsAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                UMAPhysicsAvatar player = components[i];
                if (player.ragdolled)
                    player.ragdolled = false;
            }
        }

        private void OnShoot()
        {
			//Debug.Log("Shoot action triggered");
            // Left mouse button behavior
            AudioSource src = gameObject.GetComponent<AudioSource>();
            if (src != null && Bang != null)
                src.PlayOneShot(Bang, 1.0f);

            Ray ray = currentCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 100f, layers))
            {
                if (hit.rigidbody != null)
                {
                    Transform avatar = hit.rigidbody.transform.root;
                    UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();

                    if (player)
                    {
                        if (Blood != null)
                            Instantiate(Blood, hit.point, Quaternion.identity);

                        if (!player.ragdolled)
                        {
                            hits++;
                            if (hits == 5)
                                StartCoroutine(PlayHit(KillingSpree));
                            else
                                AnnounceHit(hit);
                        }

                        // Add decal
                        if (bulletDecal != null)
                            ApplyDecal(player, ray);

                        player.ragdolled = true;
                    }

                    impactTarget = hit.rigidbody;
                    impact = ray.direction * 2.0f;
                    impactEndTime = Time.time + 0.1f;
                }
            }
        }

        private void OnUndo()
        {
            // Right mouse button behavior
            Ray ray = currentCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0));

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 100f, layers))
            {
                if (hit.rigidbody != null)
                {
                    Transform avatar = hit.rigidbody.transform.root;
                    UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();
                    if (player == null)
                        player = avatar.GetComponentInChildren<UMAPhysicsAvatar>();

                    if (player)
                        player.ragdolled = false;
                }
            }
        }

        // -------------------------------
        // UPDATE (only for impact force)
        // -------------------------------

        private void Update()
        {
            if (Time.time < impactEndTime && impactTarget != null)
                impactTarget.AddForce(impact, ForceMode.VelocityChange);
        }

        // -------------------------------
        // SUPPORT FUNCTIONS
        // -------------------------------

        private RaycastHit AnnounceHit(RaycastHit hit)
        {
            if (hit.rigidbody != null)
            {
                string name = hit.rigidbody.gameObject.name.ToLower();
                if (name == "head")
                    StartCoroutine(PlayHit(HeadShot));
                if (name == "hips")
                    StartCoroutine(PlayHit(HadToHurt));
            }
            return hit;
        }

        IEnumerator PlayHit(AudioClip clip)
        {
            yield return new WaitForSeconds(announcerDelay);
            AudioSource src = gameObject.GetComponent<AudioSource>();
            if (src != null)
                src.PlayOneShot(clip);
        }

        IEnumerator TimedRagdoll(RaycastHit hit)
        {
            Transform avatar = hit.rigidbody.transform.root;
            UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();
            player.ragdolled = true;
            yield return new WaitForSeconds(0.1f);
            player.ragdolled = false;
        }

        private void ApplyDecal(UMAPhysicsAvatar player, Ray ray)
        {
            var Avatar = player.gameObject.GetComponent<DynamicCharacterAvatar>();
            var slotAsset = DecalSlotBuilder.CreateDecalSlot(
                Avatar,
                ray,
                0.035f,
                0.01f,
                0,
                bulletDecal.material,
                bulletDecal,
                new DecalSlotBuilder.DecalBuildOptions
                {
                    useHitNormalForProjection = true,
                    backOffset = 0.04f,
                    facingThreshold = 0.2f,
                    enableDebug = false
                });

            if (slotAsset != null)
            {
                UMAAssetIndexer.Instance.ProcessNewItem(slotAsset, false, false);

                SlotData slotData = new SlotData(slotAsset);
                var overlayInstance = new OverlayData(bulletDecal);
                DecalSlotBuilder.SetLastDecalOverlay(overlayInstance);
                slotData.AddOverlay(overlayInstance);
                slotData.expandAlongNormal = 1000;

                Avatar.umaData.umaRecipe.MergeSlot(slotData, true);
                Avatar.ForceUpdate(true, true, true);
            }
        }
    }
}

using UnityEngine;
using System.Collections;
using UMA;
using UMA.CharacterSystem;
using UnityEngine.InputSystem;

namespace UMA.Dynamics.Examples
{
    /// <summary>
    /// Optional per-character combat state used by <see cref="UMAShooter"/>. It lives in UMA
    /// Core so sample movement components can implement it without creating an assembly cycle.
    /// </summary>
    public interface IUMAShooterTarget
    {
        /// <returns>True when this hit should ragdoll the character.</returns>
        bool ApplyShot(Transform attacker, bool lethalHit);
    }

    public class UMAShooter : MonoBehaviour
    {
        private UMAPlayerActions controls;

        float impactEndTime = 0;
        int hits = 0;
        Rigidbody impactTarget = null;
        Vector3 impact;

        public Camera currentCamera;
        public LayerMask layers;
        [Tooltip("Character that victims should pursue after a non-lethal hit. " +
            "When empty, the root of this shooter object is used.")]
        public Transform shooterCharacter;
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

        private void OnDestroy()
        {
            controls?.Dispose();
        }

        // -------------------------------
        // ACTION HANDLERS
        // -------------------------------

        private void OnGlobalUndo()
        {
            // Escape key behavior
            UMAPhysicsAvatar[] components = UMAObjectUtility.FindObjectsByType<UMAPhysicsAvatar>(
                FindObjectsInactive.Exclude);
            for (int i = 0; i < components.Length; i++)
            {
                UMAPhysicsAvatar player = components[i];
                if (player.ragdolled)
                {
                    player.ragdolled = false;
                }
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
                    UMAPhysicsAvatar player = FindPhysicsAvatar(hit.rigidbody);

                    if (player)
                    {
                        if (Blood != null)
                            Instantiate(Blood, hit.point, Quaternion.identity);

                        if (!player.ragdolled)
                        {
                            IUMAShooterTarget target = FindShooterTarget(player);
                            bool lethalHit = IsLethalHit(hit);
                            bool shouldRagdoll = target == null ||
                                target.ApplyShot(ResolveShooterCharacter(), lethalHit);
                            if (shouldRagdoll)
                            {
                                hits++;
                                AnnounceHit(hit);
                                if (hits == 5)
                                    StartCoroutine(PlayHit(KillingSpree));
                                player.ragdolled = true;
                            }
                        }

                        // Add decal
                        if (bulletDecal != null)
                            ApplyDecal(player, ray);

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
                    UMAPhysicsAvatar player = FindPhysicsAvatar(hit.rigidbody);

                    if (player)
                    {
                        player.ragdolled = false;
                    }
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
                string name = hit.rigidbody.gameObject.name;
                if (IsHeadHit(name))
                    StartCoroutine(PlayHit(HeadShot));
                if (IsGroinHit(name))
                    StartCoroutine(PlayHit(HadToHurt));
            }
            return hit;
        }

        private static bool IsLethalHit(RaycastHit hit)
        {
            if (hit.rigidbody == null)
                return false;

            string name = hit.rigidbody.gameObject.name;
            return IsHeadHit(name) || IsGroinHit(name);
        }

        private static bool IsHeadHit(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) &&
                boneName.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGroinHit(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return false;

            return boneName.IndexOf("hips", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                boneName.IndexOf("pelvis", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                boneName.IndexOf("groin", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Transform ResolveShooterCharacter()
        {
            return shooterCharacter != null ? shooterCharacter : transform.root;
        }

        private static UMAPhysicsAvatar FindPhysicsAvatar(Rigidbody hitRigidbody)
        {
            if (hitRigidbody == null)
                return null;

            UMAPhysicsAvatar player =
                hitRigidbody.GetComponentInParent<UMAPhysicsAvatar>();
            return player != null
                ? player
                : hitRigidbody.transform.root.GetComponentInChildren<UMAPhysicsAvatar>(true);
        }

        private static IUMAShooterTarget FindShooterTarget(UMAPhysicsAvatar player)
        {
            if (player == null)
                return null;

            MonoBehaviour[] parents =
                player.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < parents.Length; i++)
            {
                if (parents[i] is IUMAShooterTarget target)
                    return target;
            }

            MonoBehaviour[] children =
                player.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i] is IUMAShooterTarget target)
                    return target;
            return null;
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
            UMAPhysicsAvatar player = FindPhysicsAvatar(hit.rigidbody);
            if (player == null)
                yield break;

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

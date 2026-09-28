using UnityEngine;

namespace UMA
{
    [AddComponentMenu("UMA/Generator Quality Controller"), DefaultExecutionOrder(-20100), DisallowMultipleComponent]
    public sealed class GeneratorQualityController : MonoBehaviour
    {
        public UMAGenerator generator;
        public GeneratorQualitySettings settings;
        [Tooltip("Optional fixed profile instead of the platform/quality mapping.")]
        public GeneratorQualityProfile profile;
        public int priority;
        public GeneratorQualityRebuild rebuildExisting = GeneratorQualityRebuild.NewBuildsOnly;
        [Min(1)] public int rebuildsPerFrame = 1;
        [Tooltip("Preview another runtime platform in the Editor. Never overrides the platform in a player.")]
        public bool previewPlatform;
        public RuntimePlatform editorPlatform = RuntimePlatform.WindowsPlayer;
        private UMAGenerator registeredGenerator;
        private int lastQuality = -1;
        private GeneratorQualityProfile lastProfile;
        private void OnEnable() { Refresh(); }
        private void Update()
        {
            if (registeredGenerator == null || lastQuality != QualitySettings.GetQualityLevel() ||
                (lastProfile != null && !GeneratorQualityRuntime.HasOwner(registeredGenerator, this))) Refresh();
        }
        private void OnDisable() { GeneratorQualityRuntime.Remove(registeredGenerator, this); registeredGenerator = null; }
        private void OnDestroy() { GeneratorQualityRuntime.Remove(registeredGenerator, this); }
        public void Refresh()
        {
            if (!isActiveAndEnabled) return;
            var nextGenerator = generator != null ? generator : UMAAssetIndexer.Instance?.Generator;
            if (registeredGenerator != nextGenerator) GeneratorQualityRuntime.Remove(registeredGenerator, this);
            registeredGenerator = nextGenerator;
            if (registeredGenerator == null) return;
            lastQuality = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            string quality = lastQuality >= 0 && lastQuality < names.Length ? names[lastQuality] : "";
            var platform = Application.platform;
#if UNITY_EDITOR
            if (previewPlatform) platform = editorPlatform;
            else if (platform == RuntimePlatform.WindowsEditor) platform = RuntimePlatform.WindowsPlayer;
            else if (platform == RuntimePlatform.OSXEditor) platform = RuntimePlatform.OSXPlayer;
            else if (platform == RuntimePlatform.LinuxEditor) platform = RuntimePlatform.LinuxPlayer;
#endif
            lastProfile = profile != null ? profile : settings != null ? settings.Resolve(platform, quality) : null;
            if (lastProfile == null) GeneratorQualityRuntime.Remove(registeredGenerator, this);
            else GeneratorQualityRuntime.SetProfile(registeredGenerator, this, lastProfile, priority, rebuildExisting, rebuildsPerFrame);
        }
        public GeneratorQualityProfile SelectedProfile => lastProfile;
        public bool ChangePending => registeredGenerator != null && GeneratorQualityRuntime.IsPending(registeredGenerator);
        public void SetProfile(GeneratorQualityProfile selected, GeneratorQualityRebuild rebuild = GeneratorQualityRebuild.NewBuildsOnly)
        { profile = selected; rebuildExisting = rebuild; Refresh(); }
    }
}

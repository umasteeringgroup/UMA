using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Example component showing how to use the new optimized mesh combiners.
    /// This demonstrates the performance improvements available with the MeshData API.
    /// </summary>
    public class UMAOptimizedMeshCombinerExample : MonoBehaviour
    {
        [Header("Mesh Combiner Selection")]
        [SerializeField] private MeshCombinerType combinerType = MeshCombinerType.MeshDataCombiner;
        
        [Header("Performance Monitoring")]
        [SerializeField] private bool showPerformanceStats = true;
        
        private UMAData umaData;
        private float lastCombineTime;
        
        public enum MeshCombinerType
        {
            Default,
            Jobified,
            MeshDataCombiner
        }

        void Start()
        {
            umaData = GetComponent<UMAData>();
            if (umaData != null)
            {
                SetupOptimizedMeshCombiner();
            }
        }

        /// <summary>
        /// Sets up the appropriate mesh combiner based on the selected type
        /// </summary>
        public void SetupOptimizedMeshCombiner()
        {
            if (umaData == null) return;

            // Remove existing mesh combiner
            var existingCombiner = GetComponent<UMAMeshCombiner>();
            if (existingCombiner != null)
            {
                DestroyImmediate(existingCombiner);
            }

            // Add the selected mesh combiner
            switch (combinerType)
            {
                case MeshCombinerType.Default:
                    gameObject.AddComponent<UMADefaultMeshCombiner>();
                    Debug.Log("Using UMADefaultMeshCombiner (traditional approach)");
                    break;
                    
                case MeshCombinerType.Jobified:
                    gameObject.AddComponent<UMAJobifiedMeshCombiner>();
                    Debug.Log("Using UMAJobifiedMeshCombiner (job-based vertex processing)");
                    break;
                    
                case MeshCombinerType.MeshDataCombiner:
                    gameObject.AddComponent<UMAMeshDataCombiner>();
                    Debug.Log("Using UMAMeshDataCombiner (MeshData API with jobs)");
                    break;
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying && umaData != null)
            {
                SetupOptimizedMeshCombiner();
            }
        }

        /// <summary>
        /// Measures and compares performance of different mesh combiners
        /// </summary>
        [ContextMenu("Performance Test")]
        public void PerformanceTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Performance test only works in play mode");
                return;
            }

            if (umaData == null)
            {
                Debug.LogError("UMAData component not found");
                return;
            }

            Debug.Log("Starting mesh combiner performance test...");
            
            // Test each combiner type
            TestCombinerPerformance(MeshCombinerType.Default);
            TestCombinerPerformance(MeshCombinerType.Jobified);
            TestCombinerPerformance(MeshCombinerType.MeshDataCombiner);
        }

        private void TestCombinerPerformance(MeshCombinerType type)
        {
            // Switch to the specified combiner
            var oldType = combinerType;
            combinerType = type;
            SetupOptimizedMeshCombiner();

            // Force rebuild to measure performance
            var startTime = Time.realtimeSinceStartup;
            
            umaData.Dirty(true, true, true);
            umaData.ForceUpdate();
            
            var endTime = Time.realtimeSinceStartup;
            var duration = (endTime - startTime) * 1000f; // Convert to milliseconds

            Debug.Log($"{type} took {duration:F2}ms to combine meshes");

            // Restore original setting
            combinerType = oldType;
        }

        void OnGUI()
        {
            if (!showPerformanceStats) return;

            GUI.Box(new Rect(10, 10, 300, 120), "Mesh Combiner Performance");
            
            GUILayout.BeginArea(new Rect(15, 35, 290, 100));
            
            GUILayout.Label($"Current Combiner: {combinerType}");
            GUILayout.Label($"Last Combine Time: {lastCombineTime:F2}ms");
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("Run Performance Test"))
            {
                PerformanceTest();
            }
            
            if (GUILayout.Button("Switch to MeshData Combiner"))
            {
                combinerType = MeshCombinerType.MeshDataCombiner;
                SetupOptimizedMeshCombiner();
            }
            
            GUILayout.EndArea();
        }

        /// <summary>
        /// Called when UMA character is updated - measures combine time
        /// </summary>
        public void OnCharacterUpdated(UMAData umaData)
        {
            if (showPerformanceStats)
            {
                // This would need to be hooked up to UMA's update events
                // to properly measure timing
            }
        }
    }
}
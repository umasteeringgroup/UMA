using UnityEngine;
using UMA;

namespace UMA.Tests
{
    /// <summary>
    /// Simple test class to validate the SkinnedMeshCombinerJobified implementation
    /// </summary>
    public class SkinnedMeshCombinerJobifiedTest : MonoBehaviour
    {
        [Header("Test Settings")]
        public bool runTestOnStart = false;
        public bool testParallelJobs = false;
        
        void Start()
        {
            if (runTestOnStart)
            {
                RunBasicTest();
            }
        }
        
        /// <summary>
        /// Basic functionality test
        /// </summary>
        public void RunBasicTest()
        {
            Debug.Log("Testing SkinnedMeshCombinerJobified basic functionality...");
            
            try
            {
                // Create empty test data
                var target = new UMAMeshData();
                var sources = new SkinnedMeshCombiner.CombineInstance[0]; // Empty array
                var blendShapeSettings = new BlendShapeSettings();
                var recipe = ScriptableObject.CreateInstance<UMAData.UMARecipe>();
                
                // Test with empty sources (should not throw)
                SkinnedMeshCombinerJobified.CombineMeshes(target, sources, blendShapeSettings, recipe, 0);
                
                Debug.Log("✓ Basic empty test passed");
                
                // Test with null sources (should not throw)
                SkinnedMeshCombinerJobified.CombineMeshes(target, null, blendShapeSettings, recipe, 0);
                
                Debug.Log("✓ Null sources test passed");
                
                // Test with job settings
                var jobSettings = SkinnedMeshCombinerJobified.JobifiedSettings.Default;
                jobSettings.useParallelJobs = testParallelJobs;
                
                SkinnedMeshCombinerJobified.CombineMeshes(target, sources, blendShapeSettings, recipe, 0, jobSettings);
                
                Debug.Log("✓ Job settings test passed");
                Debug.Log("SkinnedMeshCombinerJobified basic tests completed successfully!");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SkinnedMeshCombinerJobified test failed: {e.Message}");
                Debug.LogException(e);
            }
        }
    }
}
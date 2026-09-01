using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public class TextureMaterialUsageWindowTests
    {
        [Test]
        public void MaterialUsesTextureMatchesAssignedTextureProperty()
        {
            Shader shader = Shader.Find("Unlit/Texture") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Standard");
            Assert.IsNotNull(shader, "A built-in shader with a texture property is required for this test.");

            Material material = new Material(shader);
            Texture2D target = new Texture2D(2, 2);
            Texture2D other = new Texture2D(2, 2);
            try
            {
                string[] textureProperties = material.GetTexturePropertyNames();
                Assert.IsNotEmpty(textureProperties, $"Shader '{shader.name}' has no texture properties.");

                material.SetTexture(textureProperties[0], target);

                Assert.IsTrue(TextureMaterialUsageWindow.MaterialUsesTexture(material, target));
                Assert.IsFalse(TextureMaterialUsageWindow.MaterialUsesTexture(material, other));
            }
            finally
            {
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }
        }
    }
}

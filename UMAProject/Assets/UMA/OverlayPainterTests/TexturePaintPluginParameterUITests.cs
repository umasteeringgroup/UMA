#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPluginParameterUITests
    {
        [Test]
        public void AssigningClothPatternEnablesLegacyZeroOpacityWithoutOverwritingAuthoredAppearance()
        {
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            try
            {
                var saved = new TexturePaintPluginParameterSet();
                saved.Get("patternOpacity", true).number = 0;
                saved.Get("usePatternColor", true).boolean = false;
                var editing = saved.Clone();
                PluginManagerWindow.SetClothPatternSprite(editing, sprite);
                Assert.That(editing.Float("patternOpacity"), Is.EqualTo(1));
                Assert.That(saved.Float("patternOpacity"), Is.Zero, "The cloned inspector edit must not mutate saved state.");
                Assert.That(editing.Boolean("usePatternColor"), Is.False, "Preserve authored tint mode.");
                editing.Get("patternOpacity").number = .35f;
                PluginManagerWindow.SetClothPatternSprite(editing, null);
                PluginManagerWindow.SetClothPatternSprite(editing, sprite);
                Assert.That(editing.Float("patternOpacity"), Is.EqualTo(.35f));
                editing.Get("patternOpacity").number = 0;
                PluginManagerWindow.SetClothPatternSprite(editing, sprite);
                Assert.That(editing.Float("patternOpacity"), Is.Zero, "Reading or replacing an existing sprite must not re-enable it.");
            }
            finally { UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void ClothMappingControlsFollowProjectionAndKeepHiddenValues()
        {
            var values = new TexturePaintPluginParameterSet();
            values.Get("projection", true).number = 0;
            values.Get("triplanarBlend", true).number = 0;
            values.Get("triplanarBlendSharpness", true).number = 12;
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "triplanarSize"), Is.False);
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "projection"), Is.True);
            values.Get("projection").number = 1;
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "triplanarSize"), Is.True);
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "triplanarBlendSharpness"), Is.True);
            values.Get("triplanarBlend").number = 1;
            string before = JsonUtility.ToJson(values);
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "triplanarBlendOffset"), Is.False);
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "triplanarBlendSharpness"), Is.False);
            Assert.That(PluginManagerWindow.IsClothMappingParameterVisible(values, "triplanarRotationY"), Is.True);
            Assert.That(JsonUtility.ToJson(values), Is.EqualTo(before));
        }

        [TestCase(0, "puffHeight", "quilt")]
        [TestCase(1, "pattern", "embroidery")]
        [TestCase(2, "holeRadius", "perforation")]
        [TestCase(3, "scatterSize", "atlasHeader")]
        public void TextileModeShowsOnlyItsOwnControlsWithoutDiscardingOtherModeSettings(int mode,
            string activeParameter, string activeSection)
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                type ??= assembly.GetType("UMA.TexturePaint.Examples.TextileSurfaceGeneratorPlugin");
            Assert.That(type, Is.Not.Null);
            var asset = ScriptableObject.CreateInstance(type);
            try
            {
                var descriptor = ((ITexturePaintCommandExtensionV2)asset).Descriptor;
                var parameters = new TexturePaintPluginParameterSet(); parameters.ResetToDefaults(descriptor);
                parameters.Get("mode").number = mode;
                parameters.Get("atlasColumns").number = 7;
                string before = JsonUtility.ToJson(parameters);
                foreach (string id in new[] { "puffHeight", "pattern", "holeRadius", "scatterSize" })
                    Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, id),
                        Is.EqualTo(id == activeParameter), id);
                foreach (string id in new[] { "quilt", "embroidery", "perforation", "atlasHeader" })
                    Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, id),
                        Is.EqualTo(id == activeSection), id);
                Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, "baseRoughness"), Is.True);
                Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, "atlas"),
                    Is.EqualTo(mode == 0 || mode == 3), "Quilt panels and freestanding scatter share the atlas input.");
                Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, "quiltAtlasSelection"),
                    Is.EqualTo(mode == 0));
                Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, "quiltSpriteSet"),
                    Is.EqualTo(mode == 0));
                Assert.That(PluginManagerWindow.IsTextileParameterVisible(descriptor, mode, "outputSpriteChannels"),
                    Is.EqualTo(mode == 0));
                Assert.That(JsonUtility.ToJson(parameters), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        private static void Layout(TexturePaintPluginParameterInputTestWindow window)
        {
            window.SendEvent(new Event{type=EventType.Layout});
            window.SendEvent(new Event{type=EventType.Repaint});
        }
        private static void Click(TexturePaintPluginParameterInputTestWindow window,Vector2 point)
        {
            window.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=point});
            window.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=point});
            Layout(window);
        }

        [TestCase(TexturePaintStripeDirection.Vertical,false)]
        [TestCase(TexturePaintStripeDirection.Horizontal,false)]
        [TestCase(TexturePaintStripeDirection.Vertical,true)]
        [TestCase(TexturePaintStripeDirection.Horizontal,true)]
        public void StripeButtonsCommitTheirEditsThroughTheClonedInspectorChangeCheck(
            TexturePaintStripeDirection direction,bool existingStripe)
        {
            var descriptor=new TexturePaintPluginDescriptor{id="com.uma.tests.stripe-buttons",
                parameters=new List<TexturePaintPluginParameterDefinition>{new TexturePaintPluginParameterDefinition{
                    id="stripes",displayName="Stripes",type=TexturePaintPluginParameterType.StripeList}}};
            var saved=new TexturePaintPluginParameterSet();saved.ResetToDefaults(descriptor);
            if(existingStripe)saved.Stripes("stripes").Add(new TexturePaintStripeDefinition{color=Color.blue,width=.27f});
            int initialCount=saved.Stripes("stripes").Count;
            var window=ScriptableObject.CreateInstance<TexturePaintPluginParameterInputTestWindow>();
            var previous=EditorWindow.focusedWindow;Rect buttons=default;int commits=0;
            window.draw=()=>
            {
                var editing=saved.Clone();EditorGUI.BeginChangeCheck();
                PluginManagerWindow.DrawParameters(descriptor,editing);
                if(EditorGUI.EndChangeCheck()){saved=editing;commits++;}
                if(Event.current.type==EventType.Repaint)buttons=GUILayoutUtility.GetLastRect();
            };
            try
            {
                window.ShowUtility();window.position=new Rect(100,100,520,850);window.Focus();
                // Assign size after ShowUtility, which can restore the previous short
                // foldout-test window and clip the buttons below its first row.
                Layout(window);
                Assert.That(buttons.height,Is.GreaterThan(0));
                // Test each button against a fully laid-out initial tree. Inserting a stripe
                // adds an entire editor; a second immediate synthetic click can otherwise
                // use the previous event's cached GUILayout rectangle.
                float across=direction==TexturePaintStripeDirection.Vertical?.25f:.75f;
                Rect clickedBounds=buttons;
                Click(window,new Vector2(buttons.x+buttons.width*across,buttons.center.y));
                Assert.That(saved.Stripes("stripes"),Has.Count.EqualTo(initialCount+1),
                    "The "+direction+" button at "+clickedBounds+" must persist its new stripe");
                Assert.That(saved.Stripes("stripes")[initialCount].direction,Is.EqualTo(direction));
                Assert.That(saved.Stripes("stripes")[initialCount].Rotation,
                    Is.EqualTo(direction == TexturePaintStripeDirection.Vertical ? 90f : 0f));
                if(existingStripe)
                {
                    Assert.That(saved.Stripes("stripes")[0].color,Is.EqualTo(Color.blue));
                    Assert.That(saved.Stripes("stripes")[0].width,Is.EqualTo(.27f));
                }
                Assert.That(commits,Is.EqualTo(1),"Each button must create exactly one persisted parameter edit");
            }
            finally{window.draw=null;window.Close();if(previous!=null)previous.Focus();}
        }

        [Test]
        public void ExpandingAPluginSectionDoesNotCreateAParameterEdit()
        {
            const string key="UMA.OverlayPainter.PluginSection.com.uma.tests.foldout-view.section";
            bool hadPreference=EditorPrefs.HasKey(key),oldPreference=EditorPrefs.GetBool(key,true);
            EditorPrefs.SetBool(key,true);
            var descriptor=new TexturePaintPluginDescriptor{id="com.uma.tests.foldout-view",
                parameters=new List<TexturePaintPluginParameterDefinition>{new TexturePaintPluginParameterDefinition{
                    id="section",displayName="Section",type=TexturePaintPluginParameterType.Header},
                    new TexturePaintPluginParameterDefinition{id="enabled",type=TexturePaintPluginParameterType.Boolean,defaultBoolean=true}}};
            var values=new TexturePaintPluginParameterSet();
            var window=ScriptableObject.CreateInstance<TexturePaintPluginParameterInputTestWindow>();
            var previous=EditorWindow.focusedWindow;Rect foldout=default;int commits=0;
            window.draw=()=>
            {
                EditorGUI.BeginChangeCheck();PluginManagerWindow.DrawParameters(descriptor,values);
                if(EditorGUI.EndChangeCheck())commits++;
                if(Event.current.type==EventType.Repaint)
                {
                    foldout=GUILayoutUtility.GetLastRect();
                    if(EditorPrefs.GetBool(key))
                        foldout.y-=EditorGUIUtility.singleLineHeight+EditorGUIUtility.standardVerticalSpacing;
                }
            };
            try
            {
                window.ShowUtility();window.position=new Rect(100,100,520,180);window.Focus();Layout(window);
                Click(window,new Vector2(foldout.x+8,foldout.center.y));
                Assert.That(EditorPrefs.GetBool(key),Is.False,"The section really changed its view state");
                Assert.That(commits,Is.Zero,"Opening/closing a section must not dirty the layer or make cached output stale");
                Assert.That(values.Boolean("enabled"),Is.True);
            }
            finally
            {
                window.draw=null;window.Close();if(previous!=null)previous.Focus();
                if(hadPreference)EditorPrefs.SetBool(key,oldPreference);else EditorPrefs.DeleteKey(key);
            }
        }
    }

    internal sealed class TexturePaintPluginParameterInputTestWindow:EditorWindow
    {
        internal Action draw;
        private void OnGUI()=>draw?.Invoke();
    }
}
#endif

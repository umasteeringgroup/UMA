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

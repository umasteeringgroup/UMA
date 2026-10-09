using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAWelcomePluginStatusTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private WelcomeToUMA window, previous;
        private string dismissed;

        [SetUp]
        public void SetUp()
        {
            previous = WelcomeToUMA.Instance;
            dismissed = SessionState.GetString("UMA.WelcomeToUMA.DismissedAutomaticPrompt", string.Empty);
            window = ScriptableObject.CreateInstance<WelcomeToUMA>();
            EditorApplication.delayCall -= window.DelayAwake;
            window.currentButton = 11;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(window);
            WelcomeToUMA.Instance = previous;
            SessionState.SetString("UMA.WelcomeToUMA.DismissedAutomaticPrompt", dismissed);
        }

        [Test]
        public void IdlePollingKeepsTheSameVerifiedCards()
        {
            Invoke("RefreshPluginStatus");
            var cards = (IList)Get("pluginCards");
            Assert.That(cards.Count, Is.GreaterThan(0));
            object first = cards[0];
            for (int i = 0; i < 20; i++) Tick();
            Assert.That(cards[0], Is.SameAs(first), "Idle inspector ticks must not reread and validate manifests.");
            Assert.That(Get("pluginStatusDirty"), Is.False);
        }

        [Test]
        public void ProjectChangesRefreshOnceAndKeepActionsCurrent()
        {
            Invoke("RefreshPluginStatus");
            var cards = (IList)Get("pluginCards");
            object first = cards[0];
            Invoke("InvalidatePluginStatus");
            Tick();
            Assert.That(cards[0], Is.Not.SameAs(first));
            Assert.That(Get("pluginStatusDirty"), Is.False);
            first = cards[0];
            Tick();
            Assert.That(cards[0], Is.SameAs(first));
        }

        [Test]
        public void BusyOperationsDeferVerificationUntilTheyFinish()
        {
            Invoke("RefreshPluginStatus");
            var cards = (IList)Get("pluginCards");
            object first = cards[0];
            Set("pluginActionQueued", true);
            Invoke("InvalidatePluginStatus");
            Tick();
            Assert.That(cards[0], Is.SameAs(first));
            Assert.That(Get("pluginStatusDirty"), Is.True);
            Set("pluginActionQueued", false);
            Tick();
            Assert.That(cards[0], Is.Not.SameAs(first));
            Assert.That(Get("pluginStatusDirty"), Is.False);
        }

        [Test]
        public void OtherPagesDoNotRunPluginVerification()
        {
            window.currentButton = 0;
            Tick();
            Assert.That(((IList)Get("pluginCards")).Count, Is.Zero);
            Assert.That(Get("pluginStatusDirty"), Is.True);
        }

        [Test]
        public void NavigationStatusRefreshesOnlyAfterInvalidationOnAnyPage()
        {
            window.currentButton = 0;
            Tick();
            Assert.That(Get("navigationPackageStatusDirty"), Is.False);
            // A sentinel proves subsequent idle polls use the cached navigation result.
            Set("navigationSrpUpdateAvailable", true);
            Tick();
            Assert.That(Get("navigationSrpUpdateAvailable"), Is.True);
            Invoke("InvalidatePluginStatus");
            Assert.That(Get("navigationPackageStatusDirty"), Is.True);
            Set("pluginActionQueued", true);
            Tick();
            Assert.That(Get("navigationPackageStatusDirty"), Is.True);
            Set("pluginActionQueued", false);
            Tick();
            Assert.That(Get("navigationPackageStatusDirty"), Is.False);
        }

        private void Tick() { Set("nextPluginStatusRefresh", 0d); Invoke("OnInspectorUpdate"); }
        private object Get(string name) => typeof(WelcomeToUMA).GetField(name, Flags).GetValue(window);
        private void Set(string name, object value) => typeof(WelcomeToUMA).GetField(name, Flags).SetValue(window, value);
        private void Invoke(string name)
        {
            try { typeof(WelcomeToUMA).GetMethod(name, Flags).Invoke(window, null); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }
    }
}

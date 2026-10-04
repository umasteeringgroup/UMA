using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal sealed class TexturePaintSamplePreview : IDisposable
    {
        private readonly TexturePaintPreviewDisplay display = new TexturePaintPreviewDisplay();
        private readonly Action repaint;
        private Func<Dictionary<TexturePaintChannel, Color[]>> render;
        private string key, help, status = "Preparing preview…";
        private double due;
        private bool pending, disposed, mask;
        internal Texture2D Texture => display.Texture;
        internal TexturePaintPreviewDisplay Display => display;

        internal TexturePaintSamplePreview(Action repaint, TexturePaintPreviewView initialView = TexturePaintPreviewView.Channel)
        {
            this.repaint = repaint;
            display.SelectView(initialView);
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal void Request(string next, Func<Dictionary<TexturePaintChannel, Color[]>> renderer, string description, bool mask = false)
        {
            if (disposed || next == key) return;
            key = next; render = renderer; help = description; this.mask = mask;
            pending = true; due = EditorApplication.timeSinceStartup + .25;
            status = "Updating preview…";
        }

        private void Update()
        {
            if (!pending || disposed || EditorApplication.timeSinceStartup < due) return;
            pending = false;
            try { display.Set(render(), mask: mask); status = "128 × 128 sample"; }
            catch (Exception exception) { display.Clear(); status = "Preview unavailable: " + exception.Message; }
            finally { render = null; repaint?.Invoke(); }
        }

        internal void Draw() { if (display.Draw(status, help)) key = null; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; render = null;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            display.Dispose();
        }
    }
}

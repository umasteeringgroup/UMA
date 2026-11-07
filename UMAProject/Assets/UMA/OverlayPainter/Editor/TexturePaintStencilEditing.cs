using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        [SerializeField] private TexturePaintStencil paintingStencil = new TexturePaintStencil();
        [SerializeField] private float stencilPreviewOpacity = .25f;
        private bool editStencil;
        private int stencilControl;
        private TexturePaintStencil stencilDragStart;
        private Vector2 stencilMouseStart;
        private TexturePaintStencil CaptureStencil(bool directUV)
        {
            if (directUV || paintingStencil?.IsActive != true) return null;
            var camera = SceneView.currentDrawingSceneView?.camera ?? SceneView.lastActiveSceneView?.camera;
            if (camera == null) return null;
            var stencil = paintingStencil.Clone(); stencil.Normalize();
            stencil.viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            stencil.viewportAspect = camera.aspect;
            return stencil;
        }
        private void DrawStencilProperties()
        {
            paintingStencil ??= new TexturePaintStencil();
            var next = paintingStencil.Clone();
            EditorGUILayout.HelpBox("Paint through an image fixed to the 3D view. This stencil limits brush coverage for every channel and mask. It does not affect the 2D canvas or procedural paths.", MessageType.None);
            EditorGUI.BeginChangeCheck();
            next.enabled = EditorGUILayout.Toggle("Enable Stencil", next.enabled);
            next.texture = (Texture2D)EditorGUILayout.ObjectField("Image", next.texture, typeof(Texture2D), false);
            next.channel = (TexturePaintLayerMaskTextureChannel)EditorGUILayout.EnumPopup("Mask Channel", next.channel);
            next.invert = EditorGUILayout.Toggle("Invert", next.invert);
            next.center = EditorGUILayout.Vector2Field("Viewport Center", next.center);
            next.size = EditorGUILayout.Vector2Field("Viewport Size", next.size);
            next.rotation = EditorGUILayout.FloatField("Rotation", next.rotation);
            bool changed = EditorGUI.EndChangeCheck();
            using (new EditorGUI.DisabledScope(!next.IsActive))
                editStencil = GUILayout.Toggle(editStencil, "Edit Stencil in 3D View", "Button");
            stencilPreviewOpacity = EditorGUILayout.Slider("Preview Opacity", stencilPreviewOpacity, 0, 1);
            if (editStencil) EditorGUILayout.HelpBox("Drag to move. Shift-drag around the center to rotate. Ctrl-drag vertically to scale. Esc cancels a drag or exits Edit Stencil. Turn editing off to paint.", MessageType.None);
            if (GUILayout.Button("Center and Match Image Aspect"))
            {
                next.center = new Vector2(.5f, .5f);
                float aspect = SceneView.lastActiveSceneView?.camera?.aspect ?? 1;
                float imageAspect = next.texture != null ? (float)next.texture.width / next.texture.height : 1;
                next.size = new Vector2(.5f, .5f * aspect / imageAspect); next.rotation = 0; changed = true;
            }
            if (changed) { next.Normalize(); ChangeStencil(paintingStencil.Clone(), next); }
            if (GUI.changed) SceneView.RepaintAll();
        }
        private void ChangeStencil(TexturePaintStencil before, TexturePaintStencil after)
        {
            paintingStencil = after.Clone();
            PushLightweightCommand("Edit Painting Stencil",
                () => { paintingStencil = before.Clone(); RepaintAll(); },
                () => { paintingStencil = after.Clone(); RepaintAll(); });
            RepaintAll();
        }
        private bool HandleStencilScene(SceneView view, Event current)
        {
            if (paintingStencil?.IsActive != true) { ReleaseStencilControl(); return false; }
            var camera = view.camera;
            Vector2 topLeft = HandleUtility.WorldToGUIPoint(camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane + 1)));
            var viewport = new Rect(topLeft.x, topLeft.y, camera.pixelWidth / EditorGUIUtility.pixelsPerPoint, camera.pixelHeight / EditorGUIUtility.pixelsPerPoint);
            Vector2 center = viewport.position + new Vector2(paintingStencil.center.x * viewport.width, (1-paintingStencil.center.y) * viewport.height);
            Vector2 size = Vector2.Scale(paintingStencil.size, viewport.size);
            if (current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                Matrix4x4 oldMatrix = GUI.matrix; Color oldColor = GUI.color;
                GUIUtility.RotateAroundPivot(paintingStencil.rotation, center);
                GUI.color = new Color(1,1,1,stencilPreviewOpacity);
                GUI.DrawTexture(new Rect(center-size*.5f,size),paintingStencil.texture,ScaleMode.StretchToFill,true);
                GUI.color = oldColor; GUI.matrix = oldMatrix;
                if (editStencil) GUI.Label(new Rect(viewport.x+15,viewport.y+45,460,30), "Stencil: drag move | Shift rotate | Ctrl scale | Esc finish", EditorStyles.whiteLabel);
                Handles.EndGUI();
            }
            int control=GUIUtility.GetControlID(793041,FocusType.Passive);
            if (!editStencil || current.alt || Tools.viewToolActive || strokeActive || uvStrokeActive) return false;
            if (current.type==EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (current.type==EventType.KeyDown && current.keyCode==KeyCode.Escape)
            {
                if(stencilDragStart!=null) paintingStencil=stencilDragStart.Clone(); else editStencil=false;
                ReleaseStencilControl();current.Use();RepaintAll();return true;
            }
            if(current.type==EventType.MouseDown && current.button==0 && GUIUtility.hotControl==0)
            {
                stencilControl=GUIUtility.hotControl=control;stencilDragStart=paintingStencil.Clone();stencilMouseStart=current.mousePosition;current.Use();
            }
            else if(current.type==EventType.MouseDrag && GUIUtility.hotControl==stencilControl && stencilDragStart!=null)
            {
                Vector2 delta=current.mousePosition-stencilMouseStart;var next=stencilDragStart.Clone();
                if(current.control || current.command) next.size*=Mathf.Exp(-delta.y*.01f);
                else if(current.shift)
                {
                    Vector2 a=stencilMouseStart-center,b=current.mousePosition-center;
                    if(a.sqrMagnitude>16 && b.sqrMagnitude>16) next.rotation+=Vector2.SignedAngle(a,b);
                }
                else next.center+=new Vector2(delta.x/viewport.width,-delta.y/viewport.height);
                next.Normalize();paintingStencil=next;current.Use();RepaintAll();
            }
            else if((current.rawType==EventType.MouseUp || current.type==EventType.Ignore || current.type==EventType.MouseLeaveWindow) && stencilDragStart!=null)
            {
                FinishStencilDrag();if(current.type==EventType.MouseUp)current.Use();
            }
            return current.button==0 || current.type==EventType.Layout || current.type==EventType.Repaint;
        }
        private void FinishStencilDrag()
        {
            var before = stencilDragStart;
            var after = paintingStencil?.Clone();
            ReleaseStencilControl();
            if (before != null && after != null) ChangeStencil(before, after);
        }

        private void ReleaseStencilControl()
        {
            if(stencilControl!=0 && GUIUtility.hotControl==stencilControl)GUIUtility.hotControl=0;
            stencilControl=0;stencilDragStart=null;
        }
    }
}

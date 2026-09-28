using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairCardStage
    {
        private HairCharacterBindingAsset displayedBinding;
        private Material boundBodyMaterial;
        internal bool HasBoundCharacter => groom?.CharacterBinding?.Matches(groom)==true;
        internal void RefreshBoundCharacter(bool changeVisibility=true)
        {
            avatarPreview?.Dispose();avatarPreview=null;visibilityCatalog=null;
            if(boundBodyMaterial!=null) DestroyImmediate(boundBodyMaterial);boundBodyMaterial=null;
            displayedBinding=groom?.CharacterBinding;
            if(HasBoundCharacter)
            {
                sourceVisibility?.Dispose();sourceVisibility=null;
                if(authoringSurfaceMesh!=null) DestroyImmediate(authoringSurfaceMesh);authoringSurfaceMesh=null;
                scalpFilter.sharedMesh=groom.SourceMesh;scalpCollider.sharedMesh=groom.SourceMesh;
                authoringPose=new HairAuthoringPose(groom.SourceMesh,groom.SourceMesh);authoringBonePositions=null;
                var shader=Shader.Find("UMA/Hair Cards/Scalp Vertex Preview URP");
                if(shader!=null) boundBodyMaterial=new Material(shader) { name="Bound Body Scalp Preview",hideFlags=HideFlags.HideAndDontSave };
                avatarPreview=HairAvatarPreview.Build(displayedBinding,boundBodyMaterial!=null ? boundBodyMaterial : scalpMaterial);
                SceneManager.MoveGameObjectToScene(avatarPreview.Root,scene);
                visibilityCatalog=HairAvatarVisibilityCatalog.Build(null,avatarPreview.RenderedSlots,displayedBinding.race as RaceData);
                sourceSpaceObject.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                sourceSpaceObject.transform.localScale=Vector3.one;
                authoringHeadPosition=HairCharacterBindingUtility.BonePosition(displayedBinding,"Head");
                authoringNeckPosition=HairCharacterBindingUtility.BonePosition(displayedBinding,"Neck");
                if(changeVisibility) { showScalp=false;showAvatar=true; }
                ApplyBoundScalpPreview();
            }
            else { authoringHeadPosition=authoringNeckPosition=null;if(changeVisibility) {showScalp=true;showAvatar=false;} }
            ApplyVisibility();QueuePreviewChange(HairPreviewChange.Evaluation);RepaintAll();
        }
        private void ApplyBoundScalpPreview()
        {
            if(!HasBoundCharacter || avatarPreview==null) return;
            var colors=HairScalpShadingEditor.BoundColors(groom);
            avatarPreview.ApplyVertexColors(0,colors);
        }
    }
}

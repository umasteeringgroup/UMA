
//    ============================================================
//    Name:        UMABlendShapeExpressions
//    Author:     Andrew Marunchak
//    Copyright:    (c) 2020 Andrew Marunchak
//  Thank you to Eli Curtz for creating the expression player
//  script upon which this draws and maps its behaviours.
//    ============================================================
using UMA.PoseTools;
using UnityEngine;

//When attached to the skinned mesh renderer object in UMA, this script creates dummy blendshapes and uses the values to control the expression player script
//Use cases: When an UMA workflow requires blendshapes rather than bones to work
namespace UMA
{

    public class UMABlendShapeExpressions : MonoBehaviour
    {
        public SkinnedMeshRenderer targetSkinnedRenderer;
        public Mesh bakedMesh;
        public UMAExpressionPlayer expressionPlayer;

        void Start()
        {

            if (this.transform.parent.GetComponent<UMAExpressionPlayer>() == null)
            {
                Debug.Log("Please ensure an expression player has been added to the parent UMA GameObject - this script will now stop running");
                return;
            }
            else
            {
                expressionPlayer = this.transform.parent.GetComponent<UMAExpressionPlayer>();
            }

            targetSkinnedRenderer = this.GetComponent<SkinnedMeshRenderer>(); //
            bakedMesh = new Mesh();
            Debug.Log("UMA skinned mesh found - now baking");
            targetSkinnedRenderer.BakeMesh(bakedMesh);

            Vector3[] junkData = new Vector3[bakedMesh.vertices.Length];


            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Eyes Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Eyes Right", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Eyes Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Eyes Down", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Eyelid", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Eyelid", 100, junkData, junkData, junkData);


            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Neck Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Neck Right", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Neck Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Neck Down", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Neck Tilt Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Neck Tilt Right", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Head Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Head Right", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Head Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Head Down", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Head Tilt Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Head Tilt Right", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Smile Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Smile Right", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Frown Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Frown Right", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Jaw Open", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Jaw Forward", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Jaw Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Jaw Right", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mouth Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mouth Down", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mouth Left", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mouth Right", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mouth Narrow", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mouth Pucker", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Tongue Up", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Lower Lip Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Lower Lip Down", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Lower Lip Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Lower Lip Down", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Upper Lip Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Upper Lip Down", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Upper Lip Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Upper Lip Down", 100, junkData, junkData, junkData);



            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Cheek Out", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Cheek Squint", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Cheek Out", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Cheek Squint", 100, junkData, junkData, junkData);

            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Nose Sneer", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Brows In", 100, junkData, junkData, junkData);





            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Brow Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Right Brow Down", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Brow Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Left Brow Down", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mid Brow Up", 100, junkData, junkData, junkData);
            targetSkinnedRenderer.sharedMesh.AddBlendShapeFrame("UMA Player Mid Brow Down", 100, junkData, junkData, junkData);
            //Mesh.GetBlendShapeFrameVertices.del;
            //ResetUMABlendShapes(50);//the value to set the blendshapes to
        }

        public void ResetUMABlendShapes(int value)
        {
            int blendShapeCount = 36;
            int blendShapeValue = value;
            for (int blendShapeIndex = 0; blendShapeIndex < blendShapeCount; blendShapeIndex++)
            {
                targetSkinnedRenderer.SetBlendShapeWeight(blendShapeIndex, blendShapeValue);
            }
        }

        private float ValueConverter(float param)//Convert range(0 - 100) to range(-1 to +1)
        {
            param = param * 0.02f;
            param = param + -1;
            return param;
        }

        // Update is called once per frame
        void Update()
        {
            //BLENDSHAPE EMULATION START//

            //EYE GROUPS
            float eyesLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Eyes Left"));
            float eyesRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Eyes Right"));
            if (eyesLeft >= eyesRight)
            {
                expressionPlayer.leftEyeIn_Out = ValueConverter3(eyesRight - eyesLeft);
                expressionPlayer.rightEyeIn_Out = ValueConverter3(eyesLeft - eyesRight);
            }
            if (eyesLeft < eyesRight)
            {
                expressionPlayer.leftEyeIn_Out = ValueConverter4(eyesLeft - eyesRight);
                expressionPlayer.rightEyeIn_Out = ValueConverter4(eyesRight - eyesLeft);
            }

            float eyesUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Eyes Up"));
            float eyesDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Eyes Down"));
            if (eyesUp >= eyesDown)
            {
                expressionPlayer.leftEyeUp_Down = ValueConverter3(eyesUp - eyesDown);
                expressionPlayer.rightEyeUp_Down = ValueConverter3(eyesUp - eyesDown);
            }
            if (eyesUp < eyesDown)
            {
                expressionPlayer.leftEyeUp_Down = ValueConverter4(eyesDown - eyesUp);
                expressionPlayer.rightEyeUp_Down = ValueConverter4(eyesDown - eyesUp);
            }



            //TONGUE
            float tongueUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Tongue Up"));
            expressionPlayer.tongueUp_Down = ValueConverter3(tongueUp);

            //JAW
            float jawForward = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Jaw Forward"));
            expressionPlayer.jawForward_Back = ValueConverter3(jawForward);

            float jawLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Jaw Left"));
            float jawRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Jaw Right"));
            if (jawLeft >= jawRight) { expressionPlayer.jawLeft_Right = ValueConverter3(jawLeft - jawRight); }
            if (jawLeft < jawRight) { expressionPlayer.jawLeft_Right = ValueConverter4(jawRight - jawLeft); }

            float jawOpen = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Jaw Open"));
            expressionPlayer.jawOpen_Close = ValueConverter3(jawOpen);


            //MOUTH
            float mouthNarrow = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mouth Narrow"));
            float mouthPucker = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mouth Pucker"));
            if (mouthNarrow >= mouthPucker) { expressionPlayer.mouthNarrow_Pucker = ValueConverter3(mouthPucker - mouthNarrow); }
            if (mouthNarrow < mouthPucker) { expressionPlayer.mouthNarrow_Pucker = ValueConverter4(mouthNarrow - mouthPucker); }

            float mouthDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mouth Down"));
            float mouthUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mouth Up"));
            if (mouthDown >= mouthUp) { expressionPlayer.mouthUp_Down = ValueConverter3(mouthUp - mouthDown); }
            if (mouthDown < mouthUp) { expressionPlayer.mouthUp_Down = ValueConverter4(mouthDown - mouthUp); }

            float mouthLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mouth Left"));
            float mouthRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mouth Right"));
            if (mouthLeft >= mouthRight) { expressionPlayer.mouthLeft_Right = ValueConverter3(mouthLeft - mouthRight); }
            if (mouthLeft < mouthRight) { expressionPlayer.mouthLeft_Right = ValueConverter4(mouthRight - mouthLeft); }


            //SMILE/FROWN

            float smileLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Smile Left"));
            float frownLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Frown Left"));
            if (smileLeft >= frownLeft) { expressionPlayer.leftMouthSmile_Frown = ValueConverter3(smileLeft - frownLeft); }
            if (smileLeft < frownLeft) { expressionPlayer.leftMouthSmile_Frown = ValueConverter4(frownLeft - smileLeft); }

            float smileRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Smile Right"));
            float frownRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Frown Right"));
            if (smileRight >= frownRight) { expressionPlayer.rightMouthSmile_Frown = ValueConverter3(smileRight - frownRight); }
            if (smileRight < frownRight) { expressionPlayer.rightMouthSmile_Frown = ValueConverter4(frownRight - smileRight); }


            //LIPS
            float leftLowerLipUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Lower Lip Up"));
            float leftLowerLipDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Lower Lip Down"));
            if (leftLowerLipUp >= leftLowerLipDown) { expressionPlayer.leftLowerLipUp_Down = ValueConverter3(leftLowerLipUp - leftLowerLipDown); }
            if (leftLowerLipUp < leftLowerLipDown) { expressionPlayer.leftLowerLipUp_Down = ValueConverter4(leftLowerLipDown - leftLowerLipUp); }

            float rightLowerLipUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Lower Lip Up"));
            float rightLowerLipDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Lower Lip Down"));
            if (rightLowerLipUp >= rightLowerLipDown) { expressionPlayer.rightLowerLipUp_Down = ValueConverter3(rightLowerLipUp - rightLowerLipDown); }
            if (rightLowerLipUp < rightLowerLipDown) { expressionPlayer.rightLowerLipUp_Down = ValueConverter4(rightLowerLipDown - rightLowerLipUp); }


            float leftUpperLipUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Upper Lip Up"));
            float leftUpperLipDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Upper Lip Down"));
            if (leftUpperLipUp >= leftUpperLipDown) { expressionPlayer.leftUpperLipUp_Down = ValueConverter3(leftUpperLipUp - leftUpperLipDown); }
            if (leftUpperLipUp < leftUpperLipDown) { expressionPlayer.leftUpperLipUp_Down = ValueConverter4(leftUpperLipDown - leftUpperLipUp); }

            float rightUpperLipUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Upper Lip Up"));
            float rightUpperLipDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Upper Lip Down"));
            if (rightUpperLipUp >= rightUpperLipDown) { expressionPlayer.rightUpperLipUp_Down = ValueConverter3(rightUpperLipUp - rightUpperLipDown); }
            if (rightUpperLipUp < rightUpperLipDown) { expressionPlayer.rightUpperLipUp_Down = ValueConverter4(rightUpperLipDown - rightUpperLipUp); }




            //Cheek & Squint (the squint in this case negates the cheek out) - this is the way the expression player is mapped
            float leftCheekOut = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Cheek Out"));
            float leftCheekSquint = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Cheek Squint"));
            if (leftCheekOut >= leftCheekSquint) { expressionPlayer.leftCheekPuff_Squint = ValueConverter3(leftCheekOut - leftCheekSquint); }
            if (leftCheekOut < leftCheekSquint) { expressionPlayer.leftCheekPuff_Squint = ValueConverter4(leftCheekSquint - leftCheekOut); }

            float rightCheekOut = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Cheek Out"));
            float rightCheekSquint = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Cheek Squint"));
            if (rightCheekOut >= rightCheekSquint) { expressionPlayer.rightCheekPuff_Squint = ValueConverter3(rightCheekOut - rightCheekSquint); }
            if (rightCheekOut < rightCheekSquint) { expressionPlayer.rightCheekPuff_Squint = ValueConverter4(rightCheekSquint - rightCheekOut); }

            //NOSE
            float noseSneer = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Nose Sneer"));
            expressionPlayer.noseSneer = ValueConverter3(noseSneer);

            //BROWS (up/down & left/right values shared and averaged to UMA ExpressionPlayer ...BrowUp_Down &BrowLeft_Right
            float leftBrowUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Brow Up"));
            float leftBrowDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Brow Down"));
            if (leftBrowUp >= leftBrowDown) { expressionPlayer.leftBrowUp_Down = ValueConverter3(leftBrowUp - leftBrowDown); }
            if (leftBrowUp < leftBrowDown) { expressionPlayer.leftBrowUp_Down = ValueConverter4(leftBrowDown - leftBrowUp); }

            float rightBrowUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Brow Up"));
            float rightBrowDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Brow Down"));
            if (rightBrowUp >= rightBrowDown) { expressionPlayer.rightBrowUp_Down = ValueConverter3(rightBrowUp - rightBrowDown); }
            if (rightBrowUp < rightBrowDown) { expressionPlayer.rightBrowUp_Down = ValueConverter4(rightBrowDown - rightBrowUp); }

            float midBrowUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mid Brow Up"));
            float midBrowDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Mid Brow Down"));
            if (midBrowUp >= midBrowDown) { expressionPlayer.midBrowUp_Down = ValueConverter3(midBrowUp - midBrowDown); }
            if (midBrowUp < midBrowDown) { expressionPlayer.midBrowUp_Down = ValueConverter4(midBrowDown - midBrowUp); }

            float browsIn = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Brows In"));
            expressionPlayer.browsIn = ValueConverter3(browsIn);



            //EYELIDS (0 is open, 100 is closed)
            float eyelidLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Left Eyelid"));
            expressionPlayer.leftEyeOpen_Close = ValueConverter4(eyelidLeft);
            float eyelidRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Right Eyelid"));
            expressionPlayer.rightEyeOpen_Close = ValueConverter4(eyelidRight);


            //NECK left/right/up/down and tilt (values shared and averaged to UMA ExpressionPlayer neckTiltLeft_Right etc...

            float neckLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Neck Left"));
            float neckRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Neck Right"));
            if (neckLeft >= neckRight) { expressionPlayer.neckLeft_Right = ValueConverter3(neckLeft - neckRight); }
            if (neckLeft < neckRight) { expressionPlayer.neckLeft_Right = ValueConverter4(neckRight - neckLeft); }

            float neckUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Neck Up"));
            float neckDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Neck Down"));
            if (neckUp >= neckDown) { expressionPlayer.neckUp_Down = ValueConverter3(neckUp - neckDown); }
            if (neckUp < neckDown) { expressionPlayer.neckUp_Down = ValueConverter4(neckDown - neckUp); }

            float neckTiltLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Neck Tilt Left"));
            float neckTiltRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Neck Tilt Right"));
            if (neckTiltLeft >= neckTiltRight) { expressionPlayer.neckTiltLeft_Right = ValueConverter3(neckTiltLeft - neckTiltRight); }
            if (neckTiltLeft < neckTiltRight) { expressionPlayer.neckTiltLeft_Right = ValueConverter4(neckTiltRight - neckTiltLeft); }



            //HEAD left/right/up/down and tilt (values shared and averaged to UMA ExpressionPlayer headTiltLeft_Right etc...

            float headLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Head Left"));
            float headRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Head Right"));
            if (headLeft >= headRight) { expressionPlayer.headLeft_Right = ValueConverter3(headLeft - headRight); }
            if (headLeft < headRight) { expressionPlayer.headLeft_Right = ValueConverter4(headRight - headLeft); }

            float headUp = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Head Up"));
            float headDown = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Head Down"));
            if (headUp >= headDown) { expressionPlayer.headUp_Down = ValueConverter3(headUp - headDown); }
            if (headUp < headDown) { expressionPlayer.headUp_Down = ValueConverter4(headDown - headUp); }

            float headTiltLeft = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Head Tilt Left"));
            float headTiltRight = targetSkinnedRenderer.GetBlendShapeWeight(BlendShapeByString("UMA Player Head Tilt Right"));
            if (headTiltLeft >= headTiltRight) { expressionPlayer.headTiltLeft_Right = ValueConverter3(headTiltLeft - headTiltRight); }
            if (headTiltLeft < headTiltRight) { expressionPlayer.headTiltLeft_Right = ValueConverter4(headTiltRight - headTiltLeft); }

            //BLENDSHAPE EMULATION END//
        }

        public int BlendShapeByString(string arg)
        {
            int blendShapeLength;
            blendShapeLength = targetSkinnedRenderer.sharedMesh.blendShapeCount;
            for (int i = 0; i < blendShapeLength; i++)
            {
                if (targetSkinnedRenderer.sharedMesh.GetBlendShapeName(i) == arg)
                {
                    return i;
                }
            }
            return -1;
        }



        private float ValueConverter2(float param)//Convert range(0 - 100) to range(-1 to +1) [0 is eyes open, 100 is now eyes closed]
        {
            param = -param;
            param = param * 0.02f;
            param = param + 1f;
            return param;
        }

        private float ValueConverter3(float param)//Convert range(0 - 100) to range(-1 to +1) [0 to 100 left]
        {
            param = param * 0.01f;
            return param;
        }

        private float ValueConverter4(float param)//Convert range(0 - 100) to range(-1 to +1) [0 to 100 right]
        {
            param = -param;
            param = param * 0.01f;
            //param = param + 1f;
            return param;
        }

    }
}
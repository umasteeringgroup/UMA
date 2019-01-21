#if UNITY_2017_1_OR_NEWER
using UnityEngine;
using UnityEngine.Playables;
using UMA.CharacterSystem;

namespace UMA.Timeline
{
    public class UmaColorMixerBehaviour : PlayableBehaviour
    {
        DynamicCharacterAvatar avatar;
        public float elapsedTime = 0f;
        public float timeStep = 0.2f;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            string sharedColorName = "";
            avatar = playerData as DynamicCharacterAvatar;

            if (avatar == null)
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("No DynamicCharacterAvatar set for UmaColor Playable!");
                }
                return;
            }

            int inputCount = playable.GetInputCount();
            bool colorUpdated = false;

            if (inputCount <= 0)
                return;

            UmaColorBehaviour firstBehaviour = ((ScriptPlayable<UmaColorBehaviour>)playable.GetInput(0)).GetBehaviour();
            Color finalColor = Color.black;
            if( avatar.GetColor(firstBehaviour.sharedColorName) != null)
                finalColor = avatar.GetColor(firstBehaviour.sharedColorName).color;

            elapsedTime += info.deltaTime;

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                ScriptPlayable<UmaColorBehaviour> inputPlayable = (ScriptPlayable<UmaColorBehaviour>)playable.GetInput(i);
                UmaColorBehaviour input = inputPlayable.GetBehaviour();

                sharedColorName = input.sharedColorName;
                finalColor = (finalColor * (1f - inputWeight)) + (input.color * inputWeight);
            }

            if (elapsedTime >= timeStep)
            {
                elapsedTime = 0f;
                avatar.SetColor(sharedColorName, finalColor);
                colorUpdated = true;
            }

            if (colorUpdated)
                avatar.UpdateColors(true);
        }
    }
}
#endif

using UnityEngine;
using UnityEngine.Playables;
using UMA.CharacterSystem;

namespace UMA.Timeline
{
    public class UmaColorMixerBehaviour : PlayableBehaviour
    {
        DynamicCharacterAvatar m_TrackBinding;
        public float elapsedTime = 0f;
        public float timeStep = 0.2f;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            Color finalColor = Color.black;
            string sharedColorName = "";
            m_TrackBinding = playerData as DynamicCharacterAvatar;

            if (m_TrackBinding == null)
            {
                Debug.LogWarning("No DynamicCharacterAvatar set for UmaColor Playable!");
                return;
            }

            int inputCount = playable.GetInputCount();
            bool colorUpdated = false;

            elapsedTime += info.deltaTime;

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                ScriptPlayable<UmaColorBehaviour> inputPlayable = (ScriptPlayable<UmaColorBehaviour>)playable.GetInput(i);
                UmaColorBehaviour input = inputPlayable.GetBehaviour();

                sharedColorName = input.sharedColorName;
                finalColor += input.color * inputWeight;
            }

            if (elapsedTime >= timeStep)
            {
                elapsedTime = 0f;
                m_TrackBinding.SetColor(sharedColorName, finalColor);
                colorUpdated = true;
            }

            if (colorUpdated)
                m_TrackBinding.UpdateColors(true);
        }
    }
}

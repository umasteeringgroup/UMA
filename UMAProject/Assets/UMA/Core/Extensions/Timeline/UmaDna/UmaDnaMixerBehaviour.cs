using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA;
using UMA.CharacterSystem;
using System.Collections.Generic;

public class UmaDnaMixerBehaviour : PlayableBehaviour
{
    DynamicCharacterAvatar avatar;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        DynamicCharacterAvatar avatar = playerData as DynamicCharacterAvatar;
        if (avatar == null || !Application.isPlaying)
            return;

        Dictionary<string, DnaSetter> allDNA = avatar.GetDNA();

        int inputCount = playable.GetInputCount();
        for (int i = 0; i < inputCount; i++)
        {
            float inputWeight = playable.GetInputWeight(i);
            ScriptPlayable<UmaDnaBehaviour> inputPlayable = (ScriptPlayable<UmaDnaBehaviour>)playable.GetInput(i);
            UmaDnaBehaviour input = inputPlayable.GetBehaviour();

            foreach (UmaDnaBehaviour.DnaTuple dna in input.dnaValues)
            {
                if (allDNA.ContainsKey(dna.Name))
                {
                    float currentValue = allDNA[dna.Name].Value * (1f - inputWeight);
                    allDNA[dna.Name].Set( currentValue + (dna.Value * inputWeight));
                }
            }

            if (input.rebuildImmediately)
                avatar.ForceUpdate(true, false, false);
        }
    }
}

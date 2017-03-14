using UnityEngine;
using System.Collections;

namespace UMA.Examples
{
    public class EnvPresetSwitch : MonoBehaviour
    {

        public KeyCode switchKey = KeyCode.V;
        EnvPresetChooser m_envPresetChooser;

        void Start()
        {
            m_envPresetChooser = GetComponent<EnvPresetChooser>();
        }

        void Update()
        {
            if (Input.GetKeyDown(switchKey))
            {
                Switch();
            }
        }

        void Switch()
        {
            int presetToActivate = ((m_envPresetChooser.GetActivePreset() + 1) % m_envPresetChooser.presets.Length);
            m_envPresetChooser.SetActivePreset(presetToActivate);
        }
    }
}

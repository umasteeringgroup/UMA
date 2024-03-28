#if UNITY_2017_1_OR_NEWER
using UnityEditor;
using UMA.Timeline;

namespace UMA.Editors
{
    [CustomEditor(typeof(UmaRaceClip))]
    public class UmaRaceClipEditor : Editor
    {
        SerializedProperty raceToChangeTo;

        string[] raceOptions;
        int selectedIndex = -1;

        void OnEnable()
        {
            UMAContextBase context = UMAContextBase.Instance;
            RaceData[] races = context.GetAllRaces();

            raceToChangeTo = serializedObject.FindProperty("raceToChangeTo");

            raceOptions = new string[races.Length];
            for (int i = 0; i < races.Length; i++)
            {
                raceOptions[i] = races[i].raceName;
                if (raceToChangeTo.stringValue == raceOptions[i])
                {
                    selectedIndex = i;
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            int newIndex = EditorGUILayout.Popup("Race To Change To", selectedIndex, raceOptions);
            if (newIndex != selectedIndex)
            {
                selectedIndex = newIndex;
                raceToChangeTo.stringValue = raceOptions[selectedIndex];
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
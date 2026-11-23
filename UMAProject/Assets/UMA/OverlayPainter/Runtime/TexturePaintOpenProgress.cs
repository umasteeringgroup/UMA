using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace UMA.TexturePaint
{
    /// <summary>
    /// The "Opening Overlay Painter" progress dialog. Opening rebuilds the character's meshes and reads every
    /// texture of every surface, which can take a while; each step names what is being done. The dialog is
    /// closed by <see cref="End"/> (also when opening fails) and hidden while another dialog asks a question.
    /// Each step's duration is kept in <see cref="LastTimings"/>.
    /// </summary>
    public static class TexturePaintOpenProgress
    {
        private const string Title = "Opening Overlay Painter";
        private static readonly Stopwatch total = new Stopwatch();
        private static readonly Stopwatch step = new Stopwatch();
        private static readonly List<KeyValuePair<string, double>> steps = new List<KeyValuePair<string, double>>();
        private static string currentStep;
        private static float lastFraction;

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>How long each step of the last opening took, in seconds.</summary>
        public static string LastTimings { get; private set; } = string.Empty;

        /// <summary>Shows the dialog. Calling it while it is already shown only reports the step.</summary>
        public static void Begin(string firstStep)
        {
            if (!IsActive)
            {
                IsActive = true;
                steps.Clear();
                total.Restart();
                currentStep = null;
                lastFraction = 0f;
            }
            Report(firstStep, 0f);
        }

        /// <summary>Names the step that starts now; fraction is the progress from 0 to 1.</summary>
        public static void Report(string stepName, float fraction)
        {
            if (!IsActive) return;
            FinishStep();
            currentStep = stepName;
            step.Restart();
            lastFraction = UnityEngine.Mathf.Clamp01(UnityEngine.Mathf.Max(lastFraction, fraction));
#if UNITY_EDITOR
            UnityEditor.EditorUtility.DisplayProgressBar(Title, stepName, lastFraction);
#endif
        }

        /// <summary>Hides the dialog so another dialog can be answered. The next <see cref="Report"/> shows it again.</summary>
        public static void Hide()
        {
#if UNITY_EDITOR
            if (IsActive) UnityEditor.EditorUtility.ClearProgressBar();
#endif
        }

        /// <summary>Closes the dialog and keeps the timings.</summary>
        public static void End()
        {
            if (!IsActive) return;
            FinishStep();
            total.Stop();
            IsActive = false;
            var text = new StringBuilder();
            text.Append("Total ").Append(total.Elapsed.TotalSeconds.ToString("0.00")).Append(" s");
            for (int i = 0; i < steps.Count; i++)
                text.Append("; ").Append(steps[i].Key).Append(' ').Append(steps[i].Value.ToString("0.00")).Append(" s");
            LastTimings = text.ToString();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.ClearProgressBar();
#endif
        }

        private static void FinishStep()
        {
            if (currentStep == null) return;
            steps.Add(new KeyValuePair<string, double>(currentStep, step.Elapsed.TotalSeconds));
            currentStep = null;
        }
    }
}

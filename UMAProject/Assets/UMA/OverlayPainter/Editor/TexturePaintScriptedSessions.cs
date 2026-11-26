using System;
using System.Collections.Generic;
using UMA.Editors;
using UnityEditor.SceneManagement;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// For scripted checks (an assistant's or a developer's evals, reached by reflection), not for the UI: a painter opened
    /// as a scripted session neither asks about a recovery asset nor touches one (an earlier session's recovery stays as it
    /// was), and writes none while it is open or as it closes; and it is closed discarding its document. A script that
    /// opened the painter the ordinary way left a recovery asset behind on closing, which replaced the user's own for that
    /// slot and stopped the next opening at the recovery prompt, a modal dialog a script cannot answer.
    /// </summary>
    public sealed partial class TexturePaintStageWindow
    {
        [NonSerialized] private bool scriptedSession;
        private static bool openingScripted;

        /// <summary>This painter was opened as a scripted session.</summary>
        internal bool IsScriptedSession => scriptedSession;

        /// <summary>Runs <paramref name="open"/>, which opens the painter, so the painter it opens is a scripted session.</summary>
        internal static T OpenScripted<T>(Func<T> open)
        {
            openingScripted = true;
            try { return open(); }
            finally { openingScripted = false; }
        }

        /// <summary>
        /// Opens the painter on a slot and its overlay with trims, as Publish and Open in Overlay Painter does, as a
        /// scripted session. False, with the reason, when it could not.
        /// </summary>
        internal static bool OpenTrimsScripted(SlotDataAsset slot, OverlayDataAsset overlay, IReadOnlyList<UMATrimPath> trims, out string message)
        {
            string reason = null;
            IUMATrimPainter painter = UMATrimPainting.Painter;
            bool opened = painter != null && OpenScripted(() => painter.PaintTrims(slot, overlay, trims ?? new List<UMATrimPath>(), out reason));
            message = painter == null ? "No trim painter is registered." : reason;
            return opened;
        }

        /// <summary>Closes the open painter, discarding its document: nothing is saved and no recovery is written.</summary>
        internal static void CloseDiscarding()
        {
            if (StageUtility.GetCurrentStage() is TexturePaintStageWindow stage)
            {
                stage.scriptedSession = true;
                stage.recoveryDirty = false;
            }
            ExitIfActive();
        }
    }
}

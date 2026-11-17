using System;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// The Vertex Editor's tool choices, as they are remembered between sessions: the workflow,
    /// the Sculpt and Vertex Paint brushes with their settings, and the Advanced selection tool,
    /// mode and action. Kept per user in EditorPrefs as one JSON string.
    /// </summary>
    /// <remarks>
    /// Choices are stored by name, so a reordered list still restores the right one. Every value is
    /// optional: a value that is missing, unknown (for example a brush that no longer exists) or out
    /// of range is ignored when the settings are applied, and that setting keeps its default.
    /// Numbers start as NaN and switches as -1 so that a missing value can be told apart from a
    /// stored one.
    /// </remarks>
    [Serializable]
    public sealed class VertexEditorToolSettings
    {
        /// <summary>The EditorPrefs key the Vertex Editor uses.</summary>
        public const string PrefsKey = "UMA.VertexEditor.ToolSettings";

        /// <summary>The layout written by this version.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        // Workflow and Advanced
        public string workflow;
        public string advancedMode;
        public string advancedTool;
        public string defineMode;
        public string selectAction;
        public int selectionBrushRadiusPixels = int.MinValue;

        // Sculpt
        public string sculptTool;
        public string sculptMaskTool;
        public string sculptFalloff;
        public AnimationCurve sculptCustomFalloff;
        public float sculptRadius = float.NaN;
        public float sculptStrengthPercent = float.NaN;
        public float sculptPinchStrength = float.NaN;
        public float sculptCreaseDepth = float.NaN;
        public string sculptPlaneMode;
        public string sculptBoundaryMode;
        public string sculptElasticMode;
        public float sculptElasticVolumePreservation = float.NaN;
        public int sculptSymmetryX = -1;
        public int sculptConnectedOnly = -1;
        public int sculptUpdateNormalsWhileSculpting = -1;
        public int sculptFrontFacesOnly = -1;
        public int showSculptNormals = -1;
        public float sculptNormalLength = float.NaN;
        public float sculptNormalSmoothingAngle = float.NaN;

        // Sculpt and Vertex Paint
        public int brushIgnoresLockedSlots = -1;

        // Vertex Paint
        public string vertexPaintTool;
        public string vertexPaintBrushShape;
        public string vertexPaintBrushColor;
        public string vertexPaintBrushTextureGuid;
        public string vertexPaintBlend;
        public float vertexPaintRadius = float.NaN;
        public float vertexPaintStrengthPercent = float.NaN;
        public string vertexPaintFalloff;
        public AnimationCurve vertexPaintCustomFalloff;
        public float vertexPaintSmoothRadius = float.NaN;
        public int vertexPaintSymmetryX = -1;
        public int vertexPaintAffectAlpha = -1;
        public string vertexPaintNoiseType;
        public float vertexPaintNoiseScale = float.NaN;
        public float vertexPaintNoiseAmount = float.NaN;
        public int vertexPaintNoiseColor = -1;
        public int vertexPaintNoiseRepeat = -1;
        public int vertexPaintNoiseSeed = int.MinValue;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        /// <summary>
        /// Reads settings written by <see cref="ToJson"/>. Returns null for an empty or unreadable
        /// string. Values the string does not contain stay unset.
        /// </summary>
        public static VertexEditorToolSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            var settings = new VertexEditorToolSettings();
            // Missing values keep the "not stored" markers set above.
            settings.version = 0;
            try
            {
                JsonUtility.FromJsonOverwrite(json, settings);
            }
            catch (Exception)
            {
                // Unreadable settings must never stop the editor opening: use the defaults.
                return null;
            }
            return settings;
        }

        /// <summary>The settings stored under <paramref name="key"/>, or null when there are none.</summary>
        public static VertexEditorToolSettings Load(string key = PrefsKey)
        {
            return FromJson(EditorPrefs.GetString(key, string.Empty));
        }

        public void Save(string key = PrefsKey)
        {
            EditorPrefs.SetString(key, ToJson());
        }

        // ---- Reading values back, with the fallback for anything missing or invalid ----

        /// <summary>The enum value named <paramref name="name"/>, or <paramref name="fallback"/> when no such value exists.</summary>
        public static TEnum ReadEnum<TEnum>(string name, TEnum fallback) where TEnum : struct, Enum
        {
            if (!IsIdentifier(name))
            {
                // Names only: a number, or a list such as "Paint, Erase" (which Enum.TryParse
                // would combine into another value), is not a stored choice.
                return fallback;
            }
            if (Enum.TryParse(name, false, out TEnum value) && Enum.IsDefined(typeof(TEnum), value))
            {
                return value;
            }
            return fallback;
        }

        /// <summary>
        /// <paramref name="value"/> when it is a number from <paramref name="min"/> to <paramref name="max"/>,
        /// otherwise <paramref name="fallback"/>.
        /// </summary>
        public static float ReadFloat(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }
            // Allow for rounding at the ends of a slider's range.
            float tolerance = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max), 1f) * 1e-5f;
            if (value < min - tolerance || value > max + tolerance)
            {
                return fallback;
            }
            return Mathf.Clamp(value, min, max);
        }

        /// <summary><paramref name="value"/> when it is from <paramref name="min"/> to <paramref name="max"/>, otherwise <paramref name="fallback"/>.</summary>
        public static int ReadInt(int value, int min, int max, int fallback)
        {
            return value >= min && value <= max ? value : fallback;
        }

        public static int WriteBool(bool value)
        {
            return value ? 1 : 0;
        }

        /// <summary>A switch stored by <see cref="WriteBool"/>, or <paramref name="fallback"/> when it was not stored.</summary>
        public static bool ReadBool(int value, bool fallback)
        {
            return value == 1 || (value != 0 && fallback);
        }

        public static string WriteColor(Color color)
        {
            return ColorUtility.ToHtmlStringRGBA(color);
        }

        public static Color ReadColor(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 8)
            {
                return fallback;
            }
            return ColorUtility.TryParseHtmlString("#" + value, out Color color) ? color : fallback;
        }

        /// <summary>A copy of the stored curve, or <paramref name="fallback"/> when none was stored or it has no usable keys.</summary>
        public static AnimationCurve ReadCurve(AnimationCurve value, AnimationCurve fallback)
        {
            if (value == null || value.length == 0)
            {
                return fallback;
            }
            Keyframe[] keys = value.keys;
            foreach (Keyframe key in keys)
            {
                // Tangents may be infinite (a constant step); times and values may not.
                if (!IsFinite(key.time) || !IsFinite(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent))
                {
                    return fallback;
                }
            }
            return new AnimationCurve(keys)
            {
                preWrapMode = value.preWrapMode,
                postWrapMode = value.postWrapMode,
            };
        }

        public static AnimationCurve CopyCurve(AnimationCurve curve)
        {
            if (curve == null)
            {
                return null;
            }
            return new AnimationCurve(curve.keys)
            {
                preWrapMode = curve.preWrapMode,
                postWrapMode = curve.postWrapMode,
            };
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }
            return true;
        }
    }
}

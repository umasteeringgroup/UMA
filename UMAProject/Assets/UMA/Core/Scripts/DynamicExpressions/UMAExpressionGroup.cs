using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    [Flags]
    public enum ExpressionRole
    {
        None = 0,
        BlinkLeft = 1 << 0,
        BlinkRight = 1 << 1,
        EyeHorizontal = 1 << 2,
        EyeVertical = 1 << 3,
        Viseme = 1 << 4,
        Emotion = 1 << 5,
        Custom = 1 << 6,
        EyeHorizontalLeft = 1 << 7,
        EyeHorizontalRight = 1 << 8,
        EyeVerticalLeft = 1 << 9,
        EyeVerticalRight = 1 << 10
    }

    [Flags]
    public enum ExpressionJoint
    {
        None = 0,
        Head = 1 << 0,
        Neck = 1 << 1,
        Jaw = 1 << 2,
        Eyes = 1 << 3,
        Hands = 1 << 4,
        Other = 1 << 5
    }

    public enum ExpressionBlendMode
    {
        Override,
        Additive,
        Maximum
    }

    public enum ExpressionSource
    {
        Manual = 0,
        Animation = 1,
        ProceduralGaze = 2,
        ProceduralBlink = 3
    }

    public enum ExpressionValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class ExpressionValidationMessage
    {
        public ExpressionValidationSeverity severity;
        public int expressionIndex = -1;
        public string message;

        public ExpressionValidationMessage(
            ExpressionValidationSeverity severity,
            string message,
            int expressionIndex = -1)
        {
            this.severity = severity;
            this.message = message;
            this.expressionIndex = expressionIndex;
        }
    }

    [Serializable]
    public sealed class UMAExpressionDefinition
    {
        [Tooltip("Stable, case-insensitive identity used by scripts, animation, save data, and networking.")]
        public string id;

        public string displayName;
        public DNA dna;
        public ExpressionRole roles;
        public ExpressionJoint affectedJoints = ExpressionJoint.Other;
        public int priority;
        public ExpressionBlendMode blendMode = ExpressionBlendMode.Override;

        [Min(0f)]
        [Tooltip("Seconds used to smooth the effective value. Zero applies source changes immediately.")]
        public float responseTime;

        [Range(0f, 1f)]
        [Tooltip("Value produced at the peak of a procedural blink. The DNA default remains the open/neutral value.")]
        public float blinkClosedValue;

        public float DefaultValue
        {
            get { return dna != null ? Mathf.Clamp01(dna.defaultValue) : 0.5f; }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }

                if (dna != null && !string.IsNullOrWhiteSpace(dna.displayName))
                {
                    return dna.displayName;
                }

                return id;
            }
        }
    }

    [CreateAssetMenu(
        fileName = "ExpressionGroup",
        menuName = "UMA/Expression Group",
        order = 210)]
    public sealed class UMAExpressionGroup : ScriptableObject
    {
        public List<UMAExpressionDefinition> expressions =
            new List<UMAExpressionDefinition>();

        public int Count
        {
            get { return expressions != null ? expressions.Count : 0; }
        }

        public bool TryGetDefinition(
            string id,
            out UMAExpressionDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(id) || expressions == null)
            {
                return false;
            }

            for (int i = 0; i < expressions.Count; i++)
            {
                UMAExpressionDefinition candidate = expressions[i];
                if (candidate != null &&
                    string.Equals(
                        candidate.id,
                        id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetDefinitionByRole(
            ExpressionRole role,
            out UMAExpressionDefinition definition)
        {
            definition = null;
            if (role == ExpressionRole.None || expressions == null)
            {
                return false;
            }

            for (int i = 0; i < expressions.Count; i++)
            {
                UMAExpressionDefinition candidate = expressions[i];
                if (candidate != null &&
                    (candidate.roles & role) == role)
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool Validate(List<ExpressionValidationMessage> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (expressions == null)
            {
                results.Add(new ExpressionValidationMessage(
                    ExpressionValidationSeverity.Error,
                    "The expression list is null."));
                return false;
            }

            HashSet<string> ids =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<ExpressionRole, int> uniqueRoles =
                new Dictionary<ExpressionRole, int>();
            Dictionary<int, KeyValuePair<int, int>> boneOwners =
                new Dictionary<int, KeyValuePair<int, int>>();
            List<int> effectBones = new List<int>();

            ExpressionRole[] rolesRequiringUniqueOwner =
            {
                ExpressionRole.BlinkLeft,
                ExpressionRole.BlinkRight,
                ExpressionRole.EyeHorizontal,
                ExpressionRole.EyeVertical,
                ExpressionRole.EyeHorizontalLeft,
                ExpressionRole.EyeHorizontalRight,
                ExpressionRole.EyeVerticalLeft,
                ExpressionRole.EyeVerticalRight
            };

            for (int i = 0; i < expressions.Count; i++)
            {
                UMAExpressionDefinition definition = expressions[i];
                if (definition == null)
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "Expression definition is null.",
                        i));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.id))
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "A stable expression ID is required.",
                        i));
                }
                else if (!ids.Add(definition.id.Trim()))
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "Duplicate expression ID '" + definition.id + "'.",
                        i));
                }

                if (definition.dna == null)
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "Expression '" + definition.id +
                        "' has no DNA asset.",
                        i));
                    continue;
                }

                if (definition.dna.defaultValue < 0f ||
                    definition.dna.defaultValue > 1f)
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "DNA default value must be in the 0..1 range.",
                        i));
                }

                if (definition.responseTime < 0f)
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "Response time cannot be negative.",
                        i));
                }

                if (definition.blinkClosedValue < 0f ||
                    definition.blinkClosedValue > 1f)
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Error,
                        "Blink closed value must be in the 0..1 range.",
                        i));
                }

                if (definition.dna.effects == null ||
                    definition.dna.effects.Count == 0)
                {
                    results.Add(new ExpressionValidationMessage(
                        ExpressionValidationSeverity.Warning,
                        "Expression '" + definition.id +
                        "' has no DNA effects.",
                        i));
                }
                else
                {
                    for (int effectIndex = 0;
                         effectIndex < definition.dna.effects.Count;
                         effectIndex++)
                    {
                        DNAEffect effect = definition.dna.effects[effectIndex];
                        if (effect == null)
                        {
                            results.Add(new ExpressionValidationMessage(
                                ExpressionValidationSeverity.Error,
                                "Expression '" + definition.id +
                                "' has a null DNA effect.",
                                i));
                            continue;
                        }

                        if (effect.enabled &&
                            effect.ExpressionPhases ==
                            ExpressionEffectPhase.None)
                        {
                            results.Add(new ExpressionValidationMessage(
                                ExpressionValidationSeverity.Error,
                                "Effect '" + effect.GetType().Name +
                                "' does not declare an expression phase.",
                                i));
                        }

                        if (definition.responseTime > 0f &&
                            effect.enabled &&
                            effect.RequiresExpressionBuild)
                        {
                            results.Add(new ExpressionValidationMessage(
                                ExpressionValidationSeverity.Warning,
                                "Smoothed expression '" + definition.id +
                                "' contains a build-lane effect. This can " +
                                "request repeated avatar rebuilds.",
                                i));
                        }

                        if (effect.enabled)
                        {
                            ValidateEffectConfiguration(
                                effect,
                                definition.id,
                                i,
                                results);
                            effectBones.Clear();
                            effect.CollectExpressionBones(effectBones);
                            for (int boneIndex = 0;
                                 boneIndex < effectBones.Count;
                                 boneIndex++)
                            {
                                int hash = effectBones[boneIndex];
                                if (boneOwners.TryGetValue(
                                        hash,
                                        out KeyValuePair<int, int> owner) &&
                                    owner.Key != i &&
                                    owner.Value == definition.priority)
                                {
                                    results.Add(
                                        new ExpressionValidationMessage(
                                            ExpressionValidationSeverity.Warning,
                                            "Expression '" + definition.id +
                                            "' shares a bone with expression " +
                                            "index " + owner.Key +
                                            " at the same priority. Assign " +
                                            "explicit priorities to make the " +
                                            "layering intent clear.",
                                            i));
                                }
                                else
                                {
                                    boneOwners[hash] =
                                        new KeyValuePair<int, int>(
                                            i,
                                            definition.priority);
                                }
                            }
                        }
                    }
                }

                const ExpressionRole continuousRoles =
                    ExpressionRole.BlinkLeft |
                    ExpressionRole.BlinkRight |
                    ExpressionRole.EyeHorizontal |
                    ExpressionRole.EyeVertical |
                    ExpressionRole.EyeHorizontalLeft |
                    ExpressionRole.EyeHorizontalRight |
                    ExpressionRole.EyeVerticalLeft |
                    ExpressionRole.EyeVerticalRight |
                    ExpressionRole.Viseme;
                if ((definition.roles & continuousRoles) != 0 &&
                    definition.dna.effects != null)
                {
                    for (int effectIndex = 0;
                         effectIndex < definition.dna.effects.Count;
                         effectIndex++)
                    {
                        DNAEffect effect =
                            definition.dna.effects[effectIndex];
                        if (effect != null && effect.enabled &&
                            effect.RequiresExpressionBuild)
                        {
                            results.Add(new ExpressionValidationMessage(
                                ExpressionValidationSeverity.Warning,
                                "Continuously driven expression '" +
                                definition.id +
                                "' contains a build-lane effect. Prefer a " +
                                "rig, blendshape, or runtime material effect.",
                                i));
                            break;
                        }
                    }
                }

                for (int roleIndex = 0;
                     roleIndex < rolesRequiringUniqueOwner.Length;
                     roleIndex++)
                {
                    ExpressionRole role =
                        rolesRequiringUniqueOwner[roleIndex];
                    if ((definition.roles & role) == 0)
                    {
                        continue;
                    }

                    if (uniqueRoles.TryGetValue(role, out int ownerIndex))
                    {
                        results.Add(new ExpressionValidationMessage(
                            ExpressionValidationSeverity.Error,
                            "Role '" + role + "' is already assigned to " +
                            "expression index " + ownerIndex + ".",
                            i));
                    }
                    else
                    {
                        uniqueRoles.Add(role, i);
                    }
                }
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].severity ==
                    ExpressionValidationSeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateEffectConfiguration(
            DNAEffect effect,
            string expressionId,
            int expressionIndex,
            List<ExpressionValidationMessage> results)
        {
            string problem = null;
            if (effect is DNAEffect_BonePose bonePose &&
                bonePose.bonePose == null)
                problem = "bone pose";
            else if (effect is DNAEffect_BoneRotate rotate &&
                string.IsNullOrWhiteSpace(rotate.BoneName))
                problem = "bone name";
            else if (effect is DNAEffect_BoneTranslate translate &&
                string.IsNullOrWhiteSpace(translate.BoneName))
                problem = "bone name";
            else if (effect is DNAEffect_BoneScale scale &&
                string.IsNullOrWhiteSpace(scale.BoneName))
                problem = "bone name";
            else if (effect is DNAEffect_BoneTransform transform &&
                string.IsNullOrWhiteSpace(transform.boneName))
                problem = "bone name";
            else if (effect is DNAEffect_BlendShape blendShape &&
                string.IsNullOrWhiteSpace(blendShape.BlendShapeName))
                problem = "blendshape name";
            else if (effect is DNAEffect_MeshModifier mesh &&
                mesh.meshModifier == null)
                problem = "mesh modifier";
            else if (effect is DNAEffect_OverlayUVTransform uv &&
                string.IsNullOrWhiteSpace(uv.overlayName))
                problem = "overlay name";
            else if (effect is DNAEffect_RuntimeMaterialProperty runtime &&
                string.IsNullOrWhiteSpace(runtime.propertyName))
                problem = "runtime material property name";
            else if (effect is DNAEffect_SharedColor color &&
                string.IsNullOrWhiteSpace(color.sharedColorName))
                problem = "shared color name";
            else if (effect is DNAEffect_SharedColorChannel channel &&
                string.IsNullOrWhiteSpace(channel.SharedColorName))
                problem = "shared color name";
            else if (effect is DNAEffect_SharedColorProperty property &&
                (string.IsNullOrWhiteSpace(property.sharedColorName) ||
                 string.IsNullOrWhiteSpace(property.propertyName)))
                problem = "shared color and property names";

            if (problem != null)
                results.Add(new ExpressionValidationMessage(
                    ExpressionValidationSeverity.Error,
                    "Expression '" + expressionId +
                    "' has an effect with a missing " + problem + ".",
                    expressionIndex));
        }

        private void OnValidate()
        {
            if (expressions == null)
            {
                expressions = new List<UMAExpressionDefinition>();
                return;
            }

            for (int i = 0; i < expressions.Count; i++)
            {
                UMAExpressionDefinition definition = expressions[i];
                if (definition == null)
                {
                    continue;
                }

                definition.id = definition.id != null
                    ? definition.id.Trim()
                    : string.Empty;
                definition.responseTime =
                    Mathf.Max(0f, definition.responseTime);
                definition.blinkClosedValue =
                    Mathf.Clamp01(definition.blinkClosedValue);
            }
        }
    }
}

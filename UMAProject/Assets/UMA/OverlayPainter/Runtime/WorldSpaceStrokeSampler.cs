using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Resamples pointer/path input by cumulative world-space arc length.</summary>
    public sealed class WorldSpaceStrokeSampler
    {
        private bool hasInput;
        private bool hasOutput;
        private StrokeSample previousInput;
        private StrokeSample lastOutput;
        private float distanceAfterLastStamp;
        private Vector3 filteredDirection;

        public float Spacing { get; set; } = 0.01f;
        public float Stabilization { get; set; }
        public float DirectionSmoothing { get; set; } = 0.35f;
        public float DistanceAfterLastStamp => distanceAfterLastStamp;

        public void Reset()
        {
            hasInput = false;
            hasOutput = false;
            distanceAfterLastStamp = 0f;
            filteredDirection = Vector3.zero;
            previousInput = default;
            lastOutput = default;
        }

        public void Add(StrokeSample raw, List<StrokeSample> output)
        {
            if (output == null) return;
            StrokeSample current = Stabilize(raw);
            if (!hasInput)
            {
                hasInput = true;
                previousInput = current;
                Emit(current, output);
                return;
            }

            Vector3 segment = current.worldPosition - previousInput.worldPosition;
            float remaining = segment.magnitude;
            if (remaining <= 0.000001f)
            {
                previousInput = current;
                return;
            }

            StrokeSample from = previousInput;
            float spacing = Mathf.Max(0.0001f, Spacing);
            while (distanceAfterLastStamp + remaining >= spacing)
            {
                float needed = spacing - distanceAfterLastStamp;
                float t = Mathf.Clamp01(needed / remaining);
                StrokeSample stamp = Interpolate(from, current, t);
                Emit(stamp, output);
                from = stamp;
                remaining = Vector3.Distance(from.worldPosition, current.worldPosition);
                distanceAfterLastStamp = 0f;
                if (remaining <= 0.000001f) break;
            }
            distanceAfterLastStamp += remaining;
            previousInput = current;
        }

        public void Flush(List<StrokeSample> output, float minimumFraction = 0.25f)
        {
            if (!hasInput || !hasOutput || output == null) return;
            float minimum = Mathf.Max(0.0001f, Spacing) * Mathf.Clamp01(minimumFraction);
            if (Vector3.Distance(lastOutput.worldPosition, previousInput.worldPosition) < minimum) return;
            Emit(previousInput, output);
            distanceAfterLastStamp = 0f;
        }

        private StrokeSample Stabilize(StrokeSample raw)
        {
            if (!hasInput || Stabilization <= 0f) return raw;
            float response = Mathf.Lerp(1f, 0.08f, Mathf.Clamp01(Stabilization));
            raw.worldPosition = Vector3.Lerp(previousInput.worldPosition, raw.worldPosition, response);
            raw.worldNormal = Vector3.Slerp(previousInput.worldNormal, raw.worldNormal, response).normalized;
            raw.uv = Vector2.Lerp(previousInput.uv, raw.uv, response);
            raw.pressure = Mathf.Lerp(previousInput.pressure, raw.pressure, response);
            return raw;
        }

        private void Emit(StrokeSample stamp, List<StrokeSample> output)
        {
            if (hasOutput)
            {
                stamp.previousWorldPosition = lastOutput.worldPosition;
                Vector3 rawDirection = stamp.worldPosition - lastOutput.worldPosition;
                if (rawDirection.sqrMagnitude > 0.00000001f)
                {
                    rawDirection.Normalize();
                    filteredDirection = filteredDirection.sqrMagnitude <= 0.00000001f
                        ? rawDirection
                        : Vector3.Slerp(rawDirection, filteredDirection,
                            Mathf.Clamp01(DirectionSmoothing)).normalized;
                }
                stamp.direction = filteredDirection;
                // A complete path keeps its output list, so correct its provisional first
                // sample as soon as the first segment establishes a direction.
                if (lastOutput.direction.sqrMagnitude <= 0.00000001f &&
                    filteredDirection.sqrMagnitude > 0.00000001f)
                {
                    lastOutput.direction = filteredDirection;
                    if (output.Count > 0) output[output.Count - 1] = lastOutput;
                }
            }
            else
            {
                stamp.previousWorldPosition = stamp.worldPosition;
                stamp.direction = Vector3.zero;
            }
            output.Add(stamp);
            lastOutput = stamp;
            hasOutput = true;
        }

        public static StrokeSample Interpolate(StrokeSample from, StrokeSample to, float t)
        {
            StrokeSample result = t < 0.5f ? from : to;
            result.worldPosition = Vector3.Lerp(from.worldPosition, to.worldPosition, t);
            result.previousWorldPosition = from.worldPosition;
            result.worldNormal = Vector3.Slerp(from.worldNormal, to.worldNormal, t).normalized;
            result.projectionDirection = Vector3.Slerp(from.projectionDirection, to.projectionDirection, t).normalized;
            result.uv = Vector2.Lerp(from.uv, to.uv, t);
            result.previousUV = from.uv;
            result.barycentric = Vector3.Lerp(from.barycentric, to.barycentric, t);
            result.pressure = Mathf.Lerp(from.pressure, to.pressure, t);
            result.sizeMultiplier = Mathf.Lerp(from.sizeMultiplier, to.sizeMultiplier, t);
            result.flowMultiplier = Mathf.Lerp(from.flowMultiplier, to.flowMultiplier, t);
            result.time = Mathf.Lerp(from.time, to.time, t);
            result.rotation = Mathf.LerpAngle(from.rotation, to.rotation, t);
            result.footprintScale = Vector2.Lerp(from.footprintScale, to.footprintScale, t);
            result.sourceUVScale = Vector2.Lerp(from.sourceUVScale, to.sourceUVScale, t);
            result.sourceUVOffset = Vector2.Lerp(from.sourceUVOffset, to.sourceUVOffset, t);
            result.color = Color.Lerp(from.color, to.color, t);
            result.hasColor = from.hasColor || to.hasColor;
            return result;
        }
    }
}

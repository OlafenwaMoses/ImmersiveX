using System.Collections.Generic;

namespace ImmersiveX
{
    public enum MatchConfidence
    {
        None,
        Low,
        Medium,
        High,
    }

    /// <summary>The best saved room for the current one, and how sure the match is.</summary>
    public readonly struct SpaceMatch
    {
        public readonly MappedSpace Space;
        public readonly float Score;
        public readonly MatchConfidence Confidence;
        public readonly bool ByNativeKey;

        public SpaceMatch(MappedSpace space, float score, MatchConfidence confidence, bool byNativeKey)
        {
            Space = space;
            Score = score;
            Confidence = confidence;
            ByNativeKey = byNativeKey;
        }
    }

    /// <summary>
    /// Recognises a saved room. The platform's own room ID wins outright; otherwise rooms are compared by layout.
    /// High confidence loads silently, medium asks the user once, low is treated as a new room.
    /// </summary>
    public static class SpaceMatcher
    {
        public const float HighConfidence = 0.9f;
        public const float MediumConfidence = 0.75f;

        public static SpaceMatch Match(IEnumerable<MappedSpace> spaces, SpaceSignature current, string nativeKey)
        {
            MappedSpace best = null;
            var bestScore = 0f;
            foreach (var space in spaces)
            {
                if (!string.IsNullOrEmpty(nativeKey) && space.NativeKey == nativeKey)
                    return new SpaceMatch(space, 1f, MatchConfidence.High, byNativeKey: true);

                if (space.Signature == null)
                    continue;
                var score = space.Signature.Similarity(current);
                if (score > bestScore)
                {
                    best = space;
                    bestScore = score;
                }
            }

            var confidence = best == null ? MatchConfidence.None
                : bestScore >= HighConfidence ? MatchConfidence.High
                : bestScore >= MediumConfidence ? MatchConfidence.Medium
                : MatchConfidence.Low;
            return new SpaceMatch(best, bestScore, confidence, byNativeKey: false);
        }
    }
}

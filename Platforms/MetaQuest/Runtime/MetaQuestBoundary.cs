using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>Hides the Quest boundary while passthrough is showing, so users never draw or see a play area.</summary>
    sealed class MetaQuestBoundary : IBoundaryProvider
    {
        static BoundaryVisibilityFeature Feature =>
            OpenXRSettings.Instance != null ? OpenXRSettings.Instance.GetFeature<BoundaryVisibilityFeature>() : null;

        public BoundaryState State
        {
            get
            {
                var feature = Feature;
                if (feature == null || !feature.enabled)
                    return BoundaryState.NotSupported;

                switch (feature.currentVisibility)
                {
                    case XrBoundaryVisibility.VisibilitySuppressed:
                        return BoundaryState.Hidden;
                    case XrBoundaryVisibility.VisibilityNotSuppressed:
                        return BoundaryState.Visible;
                    default:
                        return BoundaryState.Unknown;
                }
            }
        }

        public void RequestHide()
        {
            var feature = Feature;
            if (feature == null || !feature.enabled)
            {
                if (Application.isEditor)
                    ImmersiveXLog.Info("Boundary hiding isn't available in the editor simulators.");
                else
                    ImmersiveXLog.Warn("Boundary Visibility feature isn't enabled; the boundary stays visible. Run Configure in Platform Setup.");
                return;
            }

            var result = (int)feature.TryRequestBoundaryVisibility(XrBoundaryVisibility.VisibilitySuppressed);
            if (result == BoundaryVisibilityFeature.XR_BOUNDARY_VISIBILITY_SUPPRESSION_NOT_ALLOWED_META)
                ImmersiveXLog.Warn("Quest refused to hide the boundary because passthrough isn't showing yet.");
            else if (result < 0)
                ImmersiveXLog.Warn($"Hiding the boundary failed (XrResult {result}).");
            else
                ImmersiveXLog.Info("Requested boundary suppression.");
        }
    }
}

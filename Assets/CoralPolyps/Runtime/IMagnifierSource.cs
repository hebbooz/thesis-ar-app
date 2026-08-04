/// <summary>
/// The magnifier's footage seam (CONTROL_INTEGRATION.md §3.3), mirroring the control
/// repo's temperature.py idiom: one interface, two implementations chosen by config,
/// with everything above the seam identical.
///
/// This exists so the entire magnifier is finishable and testable BEFORE a single
/// frame of footage exists — waiting on assets to start an integration is how a
/// pending delivery turns into a blocked build. The placeholder is not throwaway
/// scaffolding: it stays permanently as the regression harness, so the magnifier
/// remains testable on a laptop with no footage and no device. That is exactly the
/// role fake_rig.py plays for the server.
///
/// Three clips, not four. Recovery heals dead → alive directly, never back through
/// fluorescent, because fluorescence is a stress response: the way out is not the
/// way in.
/// </summary>
using UnityEngine;

namespace CoralPolyps
{
    public interface IMagnifierSource
    {
        Texture Alive { get; }
        Texture Fluorescent { get; }
        Texture Dead { get; }

        /// <summary>All three are decoding and safe to show. Until then the magnifier
        /// holds black rather than flashing an unprepared frame.</summary>
        bool Ready { get; }

        /// <summary>Short name for the HUD, so "which source am I running?" is
        /// answerable on a plinth without a rebuild.</summary>
        string SourceName { get; }

        /// <summary>
        /// One-line health for the HUD. "Ready" is not the same question as "is it
        /// MOVING": a video source that prepared and then parked on frame 0 is Ready,
        /// shows a correct-looking still, and is indistinguishable from working footage
        /// unless something reports the playhead. That failure has cost this project real
        /// time twice — once to a URL that would not parse, once to several VideoPlayers
        /// sharing a GameObject — so it gets a permanent readout rather than a comment.
        /// </summary>
        string Status { get; }
    }
}

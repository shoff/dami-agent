namespace Dami.Contracts.Models;

/// <summary>Who Dami is in a picture, for any generator handed her identity anchor.</summary>
/// <remarks>
/// One text, because two callers describing her differently would drift into two
/// different women. The Gallery composer and the daily portrait pass both send this
/// ahead of their scene, alongside the single approved reference image.
/// </remarks>
public static class PortraitIdentity
{
    /// <summary>The identity instruction sent with the reference image.</summary>
    public const string PROMPT = """
        The attached image is the sole identity reference for Dami, a fictional Korean American
        woman in her early thirties. Preserve her recognizable facial identity, fine-boned narrow
        frame, narrow waist and hips, high hip line, and very long slim trained legs. Use the
        reference for identity only: create a genuinely new head angle, gaze, expression, hair
        geometry, pose, camera, lighting, wardrobe, setting, and narrative moment. Photorealistic,
        warm, candid, affectionate and playfully flirty rather than a polished stock advertisement.
        Render complete coherent adult anatomy with physically grounded hands and feet.
        """;
}

namespace SafeSnap.Shell;

/// <summary>Pure decisions for auto-copy, kept out of the window so they can be unit tested.</summary>
public static class ClipboardPolicy
{
    /// <summary>
    /// True when the current image should be put on the clipboard after an action
    /// (capture, pixelate, highlight, arrow, undo, redo).
    /// </summary>
    public static bool ShouldAutoCopy(bool autoCopyEnabled, bool screenshotMode, bool hasImage)
        => autoCopyEnabled && !screenshotMode && hasImage;

    /// <summary>
    /// True when the clipboard still holds what SafeSnap last put there: nothing else has
    /// written to it since (the clipboard sequence number is unchanged).
    /// </summary>
    public static bool ClipboardStillOurs(uint? sequenceAfterOurCopy, uint currentSequence)
        => sequenceAfterOurCopy.HasValue && sequenceAfterOurCopy.Value == currentSequence;
}

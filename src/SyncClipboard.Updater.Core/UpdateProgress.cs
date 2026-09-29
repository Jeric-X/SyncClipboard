namespace SyncClipboard.Updater.Core;

public enum UpdatePhase { Preparing, WaitingForExit, BackingUp, Installing, Restoring, Starting }

// A null percentage means that the current phase has no measurable progress yet.
public sealed record UpdateProgress(UpdatePhase Phase, double? Percentage = null);

using System;

namespace MidiPlayer.App.Models;

public enum AudioEffectChainItemKind
{
    Eq,
    Plugin
}

public sealed record AudioEffectChainItem(
    Guid ItemId,
    AudioEffectChainItemKind Kind,
    string DisplayName,
    string FormatLabel,
    string Description,
    string Path,
    string StatusLabel,
    bool IsEnabled,
    bool HasEditor,
    bool CanMoveUp,
    bool CanMoveDown,
    bool CanRemove)
{
    public bool IsEq => Kind == AudioEffectChainItemKind.Eq;

    public bool IsPlugin => Kind == AudioEffectChainItemKind.Plugin;

    public bool HasPath => !string.IsNullOrWhiteSpace(Path);
}

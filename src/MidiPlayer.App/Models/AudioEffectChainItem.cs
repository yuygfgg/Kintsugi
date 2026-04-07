using System;

namespace MidiPlayer.App.Models;

public enum AudioEffectChainItemKind
{
    Builtin,
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
    public bool IsBuiltin => Kind == AudioEffectChainItemKind.Builtin;

    public bool IsEq => IsBuiltin && ItemId == Guid.Empty;

    public bool IsPlugin => Kind == AudioEffectChainItemKind.Plugin;

    public bool HasPath => !string.IsNullOrWhiteSpace(Path);
}

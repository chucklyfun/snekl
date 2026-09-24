namespace Snekl.Domain.Enums;

public enum OverlayKind
{
    Corner = 0,
    Ring = 1,
    Strip = 2,
    PulseRing = 3
}

public enum AnnotationSeverity
{
    Info = 0,
    Notice = 1,
    Highlight = 2,
    Warn = 3,
    Restrict = 4
}

public enum StatusPreset
{
    Custom = 0,
    Live = 1,
    AtEvent = 2,
    SupportingCause = 3,
    Safe = 4,
    Preparing = 5
}

public enum PostSort
{
    Hot = 0,
    New = 1,
    Updated = 2
}

public enum ListBehavior
{
    Highlight = 0,
    Warn = 1,
    Hide = 2,
    Mute = 3,
    Verify = 4
}

public enum ListTargetType
{
    User = 0,
    Post = 1
}

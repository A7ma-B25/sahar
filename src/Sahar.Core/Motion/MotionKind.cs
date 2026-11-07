namespace Sahar.Core.Motion;

/// <summary>The 25 v1 motions. 13–25 are implemented in Plan 2.</summary>
public enum MotionKind
{
    Wander = 1, Hover = 2, PeekTaskbar = 3, EyesFollow = 4, Drag = 5, Throw = 6, LandAndSit = 7,
    SleepInCorner = 8, Wake = 9, FullscreenHide = 10, PerchWindow = 11, CelebrateLoop = 12,
    FollowCursor = 13, HideBehindWindow = 14, PlayWithMoon = 15, Curious = 16, Shy = 17,
    StartupEntry = 18, Goodbye = 19, HopIcons = 20, Dance = 21, Breathe = 22, Attention = 23,
    OrbitCursor = 24, NapOnIcon = 25,
    // 0.8: shake reactions and living moves.
    Dizzy = 26, Angry = 27, WalkTaskbar = 28, Stretch = 29, Yawn = 30, ScratchHead = 31, WatchYou = 32, TypeAlong = 33,
    HopWindows = 34, MoonToss = 35, ChaseButterfly = 36,
}

public enum Edge { Bottom, Top, Left, Right }

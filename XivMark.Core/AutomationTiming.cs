namespace XivMark.Core;

public static class AutomationTiming
{
    public static long DelayRemaining(long now, long enteredAt, int delaySeconds) =>
        Math.Max(0, Math.Clamp(delaySeconds, 0, 120) * 1000L - Math.Max(0, now - enteredAt));

    public static bool CanOperate(bool enabled, long delayRemaining, bool pauseWhenDead, bool isDead) =>
        enabled && delayRemaining == 0 && !(pauseWhenDead && isDead);
}

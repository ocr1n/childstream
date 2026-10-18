using System;

// Tracks remote geometry independently from WinForms layout events and connection retries.
sealed class RdpViewport
{
    readonly Action<int, int> apply;
    readonly Action<string> notice;
    readonly Func<long> now;
    uint session;
    int width, height, failures;
    long retryAfter;
    bool reported;
    public RdpViewport(Action<int, int> apply, Action<string> notice, Func<long> now) { this.apply = apply; this.notice = notice; this.now = now; }
    public void Fit(uint child, bool connected, int availableWidth, int availableHeight) {
        if (!connected || child == 0 || child > int.MaxValue) { session = 0; return; }
        if (session != child) { session = child; width = height = failures = 0; retryAfter = 0; reported = false; }
        if (availableWidth < 200 || availableHeight < 200 || failures >= 3 || now() < retryAfter) return;
        double ratio = Math.Min(1, 8192.0 / Math.Max(availableWidth, availableHeight));
        int targetWidth = (int)(availableWidth * ratio), targetHeight = (int)(availableHeight * ratio);
        targetWidth -= targetWidth % 2; // RDP monitor widths must be even.
        if (targetWidth < 200 || targetHeight < 200) return;
        if (targetWidth == width && targetHeight == height) return;
        try { apply(targetWidth, targetHeight); width = targetWidth; height = targetHeight; failures = 0; }
        catch (Exception ex) {
            failures++; retryAfter = now() + 5000;
            if (!reported) { reported = true; notice("Windows could not resize the child display. The viewer will keep scaling its current resolution. " + ex.Message); }
        }
    }
}

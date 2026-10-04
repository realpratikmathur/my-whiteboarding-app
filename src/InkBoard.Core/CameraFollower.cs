namespace InkBoard.Core;

/// <summary>
/// Keeps the spot being written in view during replay. While that spot stays inside a comfortable middle
/// band of the screen nothing moves; when it leaves the band the view glides until the spot is centred
/// again. All values are in page coordinates.
/// </summary>
public sealed class CameraFollower
{
    /// <summary>Time constant of the glide, in milliseconds. Larger is slower.</summary>
    public const double EaseMs = 450;

    /// <summary>Faster glide used while the spot is not on screen at all (a jump to another part of the page).</summary>
    public const double OffScreenEaseMs = 140;

    private readonly Axis _horizontal = new(low: 0.15, high: 0.85, rest: 0.5);
    private readonly Axis _vertical = new(low: 0.2, high: 0.7, rest: 0.45);

    public (double X, double Y) Step(
        double offsetX, double offsetY, double viewWidth, double viewHeight, double targetX, double targetY, double elapsedMs) =>
        (_horizontal.Step(offsetX, viewWidth, targetX, elapsedMs), _vertical.Step(offsetY, viewHeight, targetY, elapsedMs));

    private sealed class Axis(double low, double high, double rest)
    {
        private bool _gliding;

        public double Step(double offset, double view, double target, double elapsedMs)
        {
            if (view <= 0)
            {
                return offset;
            }

            double position = (target - offset) / view;
            if (position < low || position > high)
            {
                _gliding = true;
            }

            if (!_gliding)
            {
                return offset;
            }

            // The page cannot scroll above/left of its origin.
            double wanted = Math.Max(0, target - view * rest);
            double ease = position < 0 || position > 1 ? OffScreenEaseMs : EaseMs;
            double next = offset + (wanted - offset) * (1 - Math.Exp(-elapsedMs / ease));
            if (Math.Abs(wanted - next) < view * 0.01)
            {
                _gliding = false;
                return wanted;
            }

            return next;
        }
    }
}

namespace InkBoard.Core;

/// <summary>How the view follows the writing during replay.</summary>
public enum ReplayFollowMode
{
    /// <summary>Keeps the writing around the middle of the screen.</summary>
    Centered,

    /// <summary>
    /// Leaves the view alone until the writing reaches the bottom or right edge of the screen (or is out of
    /// view), then eases over just far enough to keep it visible.
    /// </summary>
    EdgeNudge,

    /// <summary>
    /// The replay never moves the view: the presenter pans and zooms by hand (wheel, middle button, touch)
    /// while it plays, and the writing may go off screen.
    /// </summary>
    Off,
}

/// <summary>
/// Keeps the spot being written in view during replay. While that spot stays inside a band of the screen
/// nothing moves; when it leaves the band the view glides until the spot is back at a rest position.
/// All values are in page coordinates.
/// </summary>
public sealed class CameraFollower
{
    /// <summary>Smoothing time of the glide in centred mode, in milliseconds. Larger is slower.</summary>
    public const double EaseMs = 350;

    /// <summary>Softer glide used for edge nudges.</summary>
    public const double NudgeEaseMs = 500;

    /// <summary>Faster glide used while the spot is not on screen at all (a jump to another part of the page).</summary>
    public const double OffScreenEaseMs = 180;

    /// <summary>The same in edge-nudge mode: a carriage return to the next line stays unhurried.</summary>
    public const double NudgeOffScreenEaseMs = 320;

    private readonly Axis _horizontal;
    private readonly Axis _vertical;

    public CameraFollower(ReplayFollowMode mode = ReplayFollowMode.Centered)
    {
        Mode = mode;
        if (mode == ReplayFollowMode.Centered)
        {
            // Outside the middle band: bring the spot back to the centre.
            _horizontal = new Axis(low: 0.15, high: 0.85, restFromLow: 0.5, restFromHigh: 0.5, EaseMs, OffScreenEaseMs, chase: true);
            _vertical = new Axis(low: 0.2, high: 0.7, restFromLow: 0.45, restFromHigh: 0.45, EaseMs, OffScreenEaseMs, chase: true);
        }
        else
        {
            // Only the last strip before an edge triggers; the view then moves about a tenth of the screen,
            // so the spot sits just inside that edge again with room for the next few words or lines.
            _horizontal = new Axis(low: 0.05, high: 0.90, restFromLow: 0.15, restFromHigh: 0.78, NudgeEaseMs, NudgeOffScreenEaseMs, chase: false);
            _vertical = new Axis(low: 0.05, high: 0.86, restFromLow: 0.15, restFromHigh: 0.72, NudgeEaseMs, NudgeOffScreenEaseMs, chase: false);
        }
    }

    public ReplayFollowMode Mode { get; }

    public (double X, double Y) Step(
        double offsetX, double offsetY, double viewWidth, double viewHeight, double targetX, double targetY, double elapsedMs) =>
        Mode == ReplayFollowMode.Off
            ? (offsetX, offsetY)
            : (_horizontal.Step(offsetX, viewWidth, targetX, elapsedMs), _vertical.Step(offsetY, viewHeight, targetY, elapsedMs));

    /// <param name="low">Fraction of the view before which the spot is too close to the near edge.</param>
    /// <param name="high">Fraction of the view beyond which the spot is too close to the far edge.</param>
    /// <param name="restFromLow">Where the spot should end up after leaving through the near edge.</param>
    /// <param name="restFromHigh">Where the spot should end up after leaving through the far edge.</param>
    /// <param name="easeMs">How long a glide takes to mostly settle.</param>
    /// <param name="offScreenEaseMs">The same while the spot is not on screen at all.</param>
    /// <param name="chase">True to keep following the spot while gliding; false to move a fixed amount.</param>
    private sealed class Axis(double low, double high, double restFromLow, double restFromHigh, double easeMs, double offScreenEaseMs, bool chase)
    {
        private bool _gliding;
        private double _rest;
        private double _velocity;   // page units per second
        private double _wanted;

        public double Step(double offset, double view, double target, double elapsedMs)
        {
            if (view <= 0 || elapsedMs <= 0)
            {
                return offset;
            }

            double position = (target - offset) / view;
            if (position < low)
            {
                _gliding = true;
                _rest = restFromLow;
            }
            else if (position > high)
            {
                _gliding = true;
                _rest = restFromHigh;
            }

            if (!_gliding)
            {
                return offset;
            }

            // Centred mode keeps chasing the spot until it is centred. A nudge is one fixed move: where to go is
            // decided while the spot is outside the band and then left alone, so once the writing is back in
            // view the screen settles and stays still instead of trailing the pen.
            // (The page cannot scroll above/left of its origin.)
            if (chase || position < low || position > high)
            {
                _wanted = Math.Max(0, target - view * _rest);
            }

            double wanted = _wanted;

            // Quicker when the spot is out of view (a jump elsewhere on the page) or about to leave it
            // (writing faster than the gentle pace can follow); gentle otherwise.
            double ease = position < 0 || position > 1 ? offScreenEaseMs
                : position < 0.02 || position > 0.97 ? easeMs * 0.6
                : easeMs;
            double next = SmoothDamp(offset, wanted, ease / 1000, elapsedMs / 1000);
            if (Math.Abs(wanted - next) < 0.5 && Math.Abs(_velocity) < 8)
            {
                _gliding = false;
                _velocity = 0;
                return wanted;
            }

            return next;
        }

        /// <summary>
        /// Critically damped spring: starts gently, speeds up, and slows into the target without overshoot,
        /// so the view never lurches. (The usual game-camera "smooth damp".)
        /// </summary>
        private double SmoothDamp(double current, double target, double smoothTime, double dt)
        {
            double omega = 2 / smoothTime, x = omega * dt;
            double decay = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target;
            double temp = (_velocity + omega * change) * dt;
            _velocity = (_velocity - omega * temp) * decay;
            double result = target + (change + temp) * decay;
            if ((target - current > 0) == (result > target))
            {
                result = target;
                _velocity = 0;
            }

            return result;
        }
    }
}

using UnityEngine;

namespace MartianMap.HUD
{
    /// <summary>One-shot, single-pointer hold detection, independent of input devices and frame rate.</summary>
    public sealed class MapHoldGesture
    {
        private bool _active;
        private int _pointer;
        private Vector2 _origin;
        private float _started;

        public void Begin(int pointer, Vector2 position, float time)
        {
            _active = true;
            _pointer = pointer;
            _origin = position;
            _started = time;
        }

        public void Cancel() => _active = false;

        public bool Hold(int pointer, Vector2 position, float time, float duration, float tolerancePixels)
        {
            if (!_active)
            {
                return false;
            }

            if (pointer != _pointer || !Finite(time) || !Finite(_started) || !Finite(position.x) ||
                !Finite(position.y) || !Finite(_origin.x) || !Finite(_origin.y) ||
                !Finite(duration) || duration <= 0 || !Finite(tolerancePixels) || tolerancePixels < 0 ||
                time < _started || (position - _origin).sqrMagnitude > tolerancePixels * tolerancePixels)
            {
                Cancel();
                return false;
            }

            if (time - _started < duration)
            {
                return false;
            }

            Cancel();
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

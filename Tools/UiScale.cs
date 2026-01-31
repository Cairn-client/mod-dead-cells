using System;
using dc.hxd;

namespace DeadCellsMultiplayerMod.Tools
{
    public static class UiScale
    {
        private const double ReferenceWidth = 1920.0;
        private const double ReferenceHeight = 1080.0;
        private const double MinScale = 0.9;
        private const double MaxScale = 1.15;

        public static double GetResolutionScale()
        {
            var win = Window.Class.getInstance();
            if (win == null)
                return 1.0;

            double width = win.get_width();
            double height = win.get_height();
            if (width <= 0 || height <= 0)
                return 1.0;

            double scaleW = width / ReferenceWidth;
            double scaleH = height / ReferenceHeight;
            if (scaleW <= 0 || scaleH <= 0)
                return 1.0;

            var scale = System.Math.Min(scaleW, scaleH);
            if (scale <= 0)
                return 1.0;

            // Ease scaling: boost small windows, tame large resolutions.
            scale = System.Math.Sqrt(scale);
            if (scale < MinScale)
                scale = MinScale;
            if (scale > MaxScale)
                scale = MaxScale;
            return scale;
        }
    }
}

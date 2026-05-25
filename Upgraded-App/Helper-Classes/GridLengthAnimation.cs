using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace FishLens_App.Helper_Classes
{
    public class GridLengthAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register("From", typeof(GridLength), typeof(GridLengthAnimation));
        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register("To", typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty EasingModeProperty =
        DependencyProperty.Register("EasingMode", typeof(EasingMode), typeof(GridLengthAnimation),
        new PropertyMetadata(EasingMode.EaseOut));

        public EasingMode EasingMode
        {
            get => (EasingMode)GetValue(EasingModeProperty);
            set => SetValue(EasingModeProperty, value);
        }

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }
        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            double from = ((GridLength)GetValue(FromProperty)).Value;
            double to = ((GridLength)GetValue(ToProperty)).Value;
            double progress = animationClock.CurrentProgress ?? 0;

            progress = EasingMode switch
            {
                EasingMode.EaseOut => 1 - Math.Pow(1 - progress, 3),
                EasingMode.EaseIn => Math.Pow(progress, 3),
                _ => progress < 0.5          // EaseInOut
                                        ? 4 * Math.Pow(progress, 3)
                                        : 1 - Math.Pow(-2 * progress + 2, 3) / 2
            };

            return new GridLength(from + (to - from) * progress);
        }
    }
}

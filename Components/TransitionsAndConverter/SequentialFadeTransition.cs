using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace drive_desktop.Components.TransitionsAndConverter;

public class SequentialFadeTransition :IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(200);
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        // 🌟 关键防御：在旧页面消失前，先强行把新页面设置为透明，防止它提前闪烁出来
        if (to != null)
        {
            to.Opacity = 0;
            to.IsVisible = true;
        }

        // 1. 让旧页面 (from) 先执行淡出动画
        if (from != null)
        {
            var fadeOut = new Animation
            {
                Duration = Duration,
                FillMode = FillMode.Forward,
                Easing = new LinearEasing(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 0.0) } }
                }
            };

            // await 保证必须等旧页面彻底透明后，才往下走
            await fadeOut.RunAsync(from, cancellationToken);
            
            // 动画跑完后，把旧页面彻底隐藏
            from.IsVisible = false;
        }

        if (cancellationToken.IsCancellationRequested) return;

        // 2. 让新页面 (to) 开始淡入动画
        if (to != null)
        {
            var fadeIn = new Animation
            {
                Duration = Duration,
                FillMode = FillMode.Forward,
                Easing = new LinearEasing(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
                }
            };

            await fadeIn.RunAsync(to, cancellationToken);
        }
    }
}
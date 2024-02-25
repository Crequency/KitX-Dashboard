using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class AppLogo : UserControl
{
    private readonly List<Animation> animations = [];

    private CancellationTokenSource cancellationTokenSource = new();

    private bool isAnimating = false;

    public AppLogo()
    {
        InitializeComponent();
    }

    public new bool IsAnimating => isAnimating;

    public void InitAnimations()
    {
        animations.Clear();

        isAnimating = true;

        cancellationTokenSource = new();

        var cancellationToken = cancellationTokenSource.Token;

        var tasks = new List<Task>();

        var random = new Random();

        Task BuildAnimationTask(Shape control)
        {
            return new Task(() =>
                Dispatcher.UIThread.Invoke(async () =>
                {
                    var ani = new Animation
                    {
                        Duration = TimeSpan.FromSeconds(random.NextDouble() + random.Next(1, 3)),
                        IterationCount = IterationCount.Infinite,
                        PlaybackDirection = PlaybackDirection.Alternate,
                        Children =
                        {
                            new KeyFrame
                            {
                                Setters = { new Setter { Property = Shape.FillProperty, Value = control.Fill } },
                                Cue = Cue.Parse("0%", null)
                            },
                            new KeyFrame
                            {
                                Setters = {
                                    new Setter
                                    {
                                        Property = Shape.FillProperty,
                                        Value = new SolidColorBrush(
                                            Color.FromArgb(
                                                (byte)random.Next(0x0, 0xff),
                                                (byte)random.Next(0x0, 0xff),
                                                (byte)random.Next(0x0, 0xff),
                                                (byte)random.Next(0x0, 0xff)
                                            )
                                        )
                                    }
                                },
                                Cue = Cue.Parse("100%", null)
                            }
                        }
                    };

                    animations.Add(ani);

                    await ani.RunAsync(control, cancellationToken);
                })
            );
        }

        foreach (var item in PathsGrid.Children)
        {
            var path = item as Path ?? throw new InvalidOperationException("Paths grid include non-path item");

            tasks.Add(BuildAnimationTask(path));
        }

        foreach (var item in RectanglesGrid.Children)
        {
            var rect = item as Rectangle ?? throw new InvalidOperationException("Rectangles grid include non-rectangle item");

            tasks.Add(BuildAnimationTask(rect));
        }

        Task.Run(async () =>
        {
            foreach (var task in tasks)
            {
                task.Start();

                await Task.Delay(random.Next(200, 1300));
            }
        });
    }

    public void StopAnimations()
    {
        animations.ForEach(
            x =>
            {
                x.IterationCount = IterationCount.Parse("0");
                x.PlaybackDirection = PlaybackDirection.Normal;
            }
        );

        cancellationTokenSource.Cancel();

        animations.Clear();

        cancellationTokenSource.Dispose();

        isAnimating = false;
    }

    public void RestartAnimations()
    {
        StopAnimations();
        InitAnimations();
    }
}

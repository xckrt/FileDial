using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileDial.App.UI;

/// <summary>
/// Короткие анимации интерфейса. Если в Windows отключены анимации («Эффекты анимации» в специальных
/// возможностях), всё применяется мгновенно.
/// </summary>
internal static class Motion
{
    public const int Fast = 90;
    public const int Normal = 150;

    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Переключатель «Анимации» из настроек FileDial.</summary>
    public static bool UserEnabled { get; set; } = true;

    public static bool Enabled => UserEnabled && SystemParameters.ClientAreaAnimation;

    /// <summary>Плавно меняет числовое свойство элемента или анимируемого объекта.</summary>
    public static void To(IAnimatable target, DependencyProperty property, double to, int milliseconds,
        Action? completed = null)
    {
        if (!Enabled)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut };
        if (completed is not null)
            animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation);
    }

    /// <summary>Мягкая отдача в конце — как фиксатор у поворотной ручки.</summary>
    public static readonly IEasingFunction Detent = Freeze(new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut });

    /// <summary>
    /// Анимация с начальным значением, задержкой и своей кривой — для поочерёдного появления элементов.
    /// Без анимаций сразу ставит конечное значение.
    /// </summary>
    public static void Animate(IAnimatable target, DependencyProperty property, double from, double to, int milliseconds,
        int delay = 0, IEasingFunction? easing = null, Action? completed = null)
    {
        if (!Enabled)
        {
            To(target, property, to, milliseconds, completed);
            return;
        }

        // До начала (пока идёт задержка) свойство показывает базовое значение — ставим его в начальное.
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, from);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = easing ?? EaseOut,
        };
        if (completed is not null)
            animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation);
    }

    /// <summary>Как <see cref="To"/>, но со своей кривой.</summary>
    public static void To(IAnimatable target, DependencyProperty property, double to, int milliseconds,
        IEasingFunction easing)
    {
        if (!Enabled)
        {
            To(target, property, to, milliseconds);
            return;
        }
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
    }

    /// <summary>Плавно меняет цвет кисти (кисть не должна быть замороженной).</summary>
    public static void ColorTo(SolidColorBrush brush, Color to, int milliseconds)
    {
        if (!Enabled)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
            return;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut });
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

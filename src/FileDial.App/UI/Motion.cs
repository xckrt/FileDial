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

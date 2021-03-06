using System.Windows;
using System.Windows.Input;
using JetBrains.Annotations;

namespace Scar.Common.WPF.View
{
    public static class ControlExtensions
    {
        public static void PreventFocusLoss([NotNull] this UIElement uiElement)
        {
            uiElement.PreviewLostKeyboardFocus += Control_PreviewLostKeyboardFocus;
        }

        private static void Control_PreviewLostKeyboardFocus(object sender, [NotNull] KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus != null && (!e.NewFocus.Focusable || !e.NewFocus.IsEnabled))
            {
                e.Handled = true;
            }
        }
    }
}
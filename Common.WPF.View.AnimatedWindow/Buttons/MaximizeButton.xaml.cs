﻿using System.Windows;
using JetBrains.Annotations;

namespace Scar.Common.WPF.View.Buttons
{
    public partial class MaximizeButton
    {
        public MaximizeButton()
        {
            InitializeComponent();
        }

        private void MaximizeButton_Click([NotNull] object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow((DependencyObject)sender);
            if (window != null)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
    }
}
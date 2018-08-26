﻿using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using JetBrains.Annotations;

namespace Scar.Common.WPF.Converters
{
    [ValueConversion(typeof(bool[]), typeof(bool))]
    public class AllBoolsToVisibilityConverter : IMultiValueConverter
    {
        [NotNull]
        public object Convert([NotNull] object[] values, [NotNull] Type targetType, [CanBeNull] object parameter, [NotNull] CultureInfo culture)
        {
            return values.OfType<bool>().All(x => x) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack([NotNull] object value, [NotNull] Type[] targetTypes, [CanBeNull] object parameter, [NotNull] CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using JetBrains.Annotations;
using Microsoft.Windows.Themes;
using Scar.Common.WPF.Controls.AutoCompleteTextBox.Provider;

namespace Scar.Common.WPF.Controls.AutoCompleteTextBox.Controls
{
    public class CompletableTextBox : TextBox
    {
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(CompletableTextBox),
            new PropertyMetadata(null, SelectedItemPropertyChanged));

        public static readonly DependencyProperty DataProviderProperty = DependencyProperty.Register(
            nameof(DataProvider),
            typeof(IAutoCompleteDataProvider),
            typeof(CompletableTextBox),
            new PropertyMetadata(null));

        private readonly IRateLimiter _rateLimiter;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private SystemDropShadowChrome _chrome;

        private bool _disabled;

        private ListBox _listBox;

        private Popup _popup;

        private bool _supressAutoAppend;

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                  Internal States                                    |
          |                                                                     |
          +---------------------------------------------------------------------*/
        private bool _textChangedByCode;

        public CompletableTextBox()
        {
            _rateLimiter = new RateLimiter(SynchronizationContext.Current);
            Loaded += CompletableTextBox_Initialized;
        }

        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public bool AutoAppend { get; set; }

        public bool AutoCompleting => _popup.IsOpen;

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                     Public interface                                |
          |                                                                     |
          +---------------------------------------------------------------------*/
        public IAutoCompleteDataProvider DataProvider
        {
            get => (IAutoCompleteDataProvider)GetValue(DataProviderProperty);
            set => SetValue(DataProviderProperty, value);
        }

        public bool Disabled
        {
            get => _disabled;
            set
            {
                _disabled = value;
                if (_disabled && _popup != null)
                {
                    _popup.IsOpen = false;
                }
            }
        }

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        private static void SelectedItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (CompletableTextBox)d;
            control?.ChangeSelectedItem(e.NewValue);
        }

        private void ChangeSelectedItem([CanBeNull] object item)
        {
            Text = item?.ToString() ?? string.Empty;
        }

        private void CompletableTextBox_Initialized(object sender, EventArgs e)
        {
            if (Application.Current.Resources.FindName("AutoCompleteTextBoxListBoxStyle") == null)
            {
                var myResourceDictionary = new ResourceDictionary();
                var uri = new Uri("pack://application:,,,/Scar.Common.WPF.Controls.AutoCompleteTextBox;component/controls/resources.xaml", UriKind.RelativeOrAbsolute);
                myResourceDictionary.Source = uri;
                Application.Current.Resources.MergedDictionaries.Add(myResourceDictionary);
            }

            var ownerWindow = Window.GetWindow(this);
            if (ownerWindow == null)
            {
                throw new InvalidOperationException("Window should not be null");
            }

            if (ownerWindow.IsLoaded)
            {
                Initialize();
            }
            else
            {
                ownerWindow.Loaded += OwnerWindow_Loaded;
            }

            ownerWindow.LocationChanged += OwnerWindow_LocationChanged;
        }

        // ReSharper disable once RedundantAssignment
        private IntPtr HookHandler(IntPtr hwnd, int msg, IntPtr firstParam, IntPtr secondParam, ref bool handled)
        {
            const int wmNclbuttondown = 0x00A1;

            const int wmNcrbuttondown = 0x00A4;

            handled = false;

            switch (msg)
            {
                case wmNclbuttondown: // pass through
                case wmNcrbuttondown:
                    _popup.IsOpen = false;
                    break;
            }

            return IntPtr.Zero;
        }

        private void Initialize()
        {
            const int popupShadowDepth = 5;

            _listBox = new ListBox
            {
                Focusable = false,
                Style = (Style)Application.Current.Resources["AutoCompleteTextBoxListBoxStyle"]
            };

            _chrome = new SystemDropShadowChrome
            {
                Margin = new Thickness(0, 0, popupShadowDepth, popupShadowDepth),
                Child = _listBox
            };

            _popup = new Popup
            {
                SnapsToDevicePixels = true,
                AllowsTransparency = true,
                Placement = PlacementMode.Bottom,
                PlacementTarget = this,
                Child = _chrome,
                Width = Width,
                IsOpen = true
            };

            SetupEventHandlers();
            var tempItem = new ListBoxItem
            {
                Content = "TEMP_ITEM_FOR_MEASUREMENT"
            };
            _listBox.Items.Add(tempItem);
            _listBox.Items.Clear();
            _popup.IsOpen = false;
        }

        private void ListBox_MouseLeftButtonUp(object sender, [NotNull] MouseButtonEventArgs e)
        {
            ListBoxItem item = null;
            var d = e.OriginalSource as DependencyObject;
            while (d != null)
            {
                if (d is ListBoxItem boxItem)
                {
                    item = boxItem;
                    break;
                }

                d = VisualTreeHelper.GetParent(d);
            }

            if (item != null)
            {
                _popup.IsOpen = false;

                // UpdateText(item.Content as string, true);
                UpdateItem(item.Content, true);
            }
        }

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                     ListBox Event Handling                          |
          |                                                                     |
          +---------------------------------------------------------------------*/
        private void ListBox_PreviewMouseLeftButtonDown(object sender, [NotNull] MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(_listBox);
            var hitTestResult = VisualTreeHelper.HitTest(_listBox, pos);
            if (hitTestResult == null)
            {
                return;
            }

            var d = hitTestResult.VisualHit;
            while (d != null)
            {
                if (d is ListBoxItem)
                {
                    e.Handled = true;
                    break;
                }

                d = VisualTreeHelper.GetParent(d);
            }
        }

        private void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Mouse.Captured != null)
            {
                return;
            }

            var pos = e.GetPosition(_listBox);
            var hitTestResult = VisualTreeHelper.HitTest(_listBox, pos);
            if (hitTestResult == null)
            {
                return;
            }

            var d = hitTestResult.VisualHit;
            while (d != null)
            {
                if (d is ListBoxItem item)
                {
                    item.IsSelected = true;

                    // _listBox.ScrollIntoView(item);
                    break;
                }

                d = VisualTreeHelper.GetParent(d);
            }
        }

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                    Window Event Handling                            |
          |                                                                     |
          +---------------------------------------------------------------------*/
        private void OwnerWindow_Deactivated(object sender, EventArgs e)
        {
            _popup.IsOpen = false;
        }

        private void OwnerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                       Initializer                                    |
          |                                                                     |
          +---------------------------------------------------------------------*/
        private void OwnerWindow_LocationChanged(object sender, EventArgs e)
        {
            _popup.IsOpen = false;
        }

        private void OwnerWindow_PreviewMouseDown(object sender, [NotNull] MouseButtonEventArgs e)
        {
            if (!Equals(e.Source, this))
            {
                _popup.IsOpen = false;
            }
        }

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                    AcTb State And Behaviors                         |
          |                                                                     |
          +---------------------------------------------------------------------*/
        private void PopulatePopupList(IEnumerable<object> items)
        {
            var text = Text;

            _listBox.ItemsSource = items;
            if (_listBox.Items.Count == 0)
            {
                _popup.IsOpen = false;
                return;
            }

            var firstSuggestion = _listBox.Items[0];
            if (_listBox.Items.Count == 1 && text.Equals(firstSuggestion.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                _popup.IsOpen = false;
                SelectedItem = firstSuggestion;
            }
            else
            {
                _listBox.SelectedIndex = 0;
                ShowPopup();

                if (!AutoAppend || _supressAutoAppend || SelectionLength != 0 || SelectionStart != Text.Length)
                {
                    return;
                }

                _textChangedByCode = true;
                try
                {
                    // ReSharper disable once SuspiciousTypeConversion.Global
                    var appendText = DataProvider is IAutoAppendDataProvider appendProvider
                        ? appendProvider.GetAppendText(text, firstSuggestion.ToString())
                        : firstSuggestion.ToString().Substring(Text.Length);
                    if (!string.IsNullOrEmpty(appendText))
                    {
                        SelectedText = appendText;
                    }
                }
                finally
                {
                    _textChangedByCode = false;
                }
            }
        }

        private void SetupEventHandlers()
        {
            var ownerWindow = Window.GetWindow(this);
            if (ownerWindow == null)
            {
                throw new InvalidOperationException("Window should not be null");
            }

            ownerWindow.PreviewMouseDown += OwnerWindow_PreviewMouseDown;
            ownerWindow.Deactivated += OwnerWindow_Deactivated;

            var wih = new WindowInteropHelper(ownerWindow);
            var hwndSource = HwndSource.FromHwnd(wih.Handle);
            if (hwndSource == null)
            {
                throw new InvalidOperationException("Window handle should not be null");
            }

            var hwndSourceHook = new HwndSourceHook(HookHandler);
            hwndSource.AddHook(hwndSourceHook);

            // hwndSource.RemoveHook();?
            TextChanged += TextBox_TextChanged;
            PreviewKeyDown += TextBox_PreviewKeyDown;
            LostFocus += TextBox_LostFocus;

            _listBox.PreviewMouseLeftButtonDown += ListBox_PreviewMouseLeftButtonDown;
            _listBox.MouseLeftButtonUp += ListBox_MouseLeftButtonUp;
            _listBox.PreviewMouseMove += ListBox_PreviewMouseMove;
        }

        private void ShowPopup()
        {
            _popup.IsOpen = true;
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _popup.IsOpen = false;
            _cancellationTokenSource.Cancel();

            // Text = SelectedItem?.ToString() ?? string.Empty;
        }

        private void TextBox_PreviewKeyDown(object sender, [NotNull] KeyEventArgs e)
        {
            _rateLimiter.Throttle(
                TimeSpan.FromMilliseconds(20),
                () =>
                {
                    _cancellationTokenSource.Cancel();
                    _supressAutoAppend = e.Key == Key.Delete || e.Key == Key.Back;

                    if (!_popup.IsOpen)
                    {
                        return;
                    }

                    var index = _listBox.SelectedIndex;

                    if (e.Key == Key.Escape)
                    {
                        _popup.IsOpen = false;
                        e.Handled = true;

                        return;
                    }

                    if (e.Key == Key.Up)
                    {
                        if (index == -1)
                        {
                            index = _listBox.Items.Count - 1;
                        }
                        else
                        {
                            --index;
                        }
                    }
                    else if (e.Key == Key.Down)
                    {
                        ++index;
                    }

                    if (e.Key == Key.Enter)
                    {
                        _popup.IsOpen = false;
                        SelectAll();

                        UpdateItem(_listBox.SelectedItem, true);
                    }

                    if (index != _listBox.SelectedIndex)
                    {
                        if (index < 0 || index > _listBox.Items.Count - 1)
                        {
                            _listBox.SelectedIndex = -1;
                        }
                        else
                        {
                            _listBox.SelectedIndex = index;
                            _listBox.ScrollIntoView(_listBox.SelectedItem);
                        }

                        e.Handled = true;
                    }
                });
        }

        /*+---------------------------------------------------------------------+
          |                                                                     |
          |                   TextBox Event Handling                            |
          |                                                                     |
          +---------------------------------------------------------------------*/
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _rateLimiter.Debounce(
                TimeSpan.FromMilliseconds(300),
                async text =>
                {
                    if (_textChangedByCode || Disabled || DataProvider == null)
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(text))
                    {
                        _popup.IsOpen = false;
                        return;
                    }

                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource = new CancellationTokenSource();
                    try
                    {
                        var items = await DataProvider.GetItemsAsync(text, _cancellationTokenSource.Token).ConfigureAwait(true);
                        PopulatePopupList(items);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                },
                Text);
        }

        private void UpdateItem([CanBeNull] object item, bool selectAll)
        {
            _textChangedByCode = true;
            SelectedItem = item;
            Text = item?.ToString() ?? string.Empty;

            if (selectAll)
            {
                SelectAll();
            }
            else
            {
                SelectionStart = Text.Length;
            }

            _textChangedByCode = false;
        }
    }
}
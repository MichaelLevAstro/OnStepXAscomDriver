using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASCOM.OnStepX.Controls
{
    // Replaces WinForms NumericUpDown. Two-way `Value` binding with optional
    // Min/Max/Step/DecimalPlaces. Up/down spinners + mouse wheel + arrow keys.
    public partial class NumericBox : UserControl
    {
        private bool _suppressTextSync;

        public NumericBox()
        {
            InitializeComponent();
            SyncTextFromValue();
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(NumericBox),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.Journal,
                (d, e) => ((NumericBox)d).SyncTextFromValue()));
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

        public static readonly DependencyProperty MinProperty = DependencyProperty.Register(
            nameof(Min), typeof(double), typeof(NumericBox), new PropertyMetadata(double.MinValue));
        public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }

        public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
            nameof(Max), typeof(double), typeof(NumericBox), new PropertyMetadata(double.MaxValue));
        public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }

        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(double), typeof(NumericBox), new PropertyMetadata(1.0));
        public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

        public static readonly DependencyProperty DecimalPlacesProperty = DependencyProperty.Register(
            nameof(DecimalPlaces), typeof(int), typeof(NumericBox),
            new PropertyMetadata(0, (d, e) => ((NumericBox)d).SyncTextFromValue()));
        public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }

        private void OnUpClick(object sender, RoutedEventArgs e)   => Bump(+1);
        private void OnDownClick(object sender, RoutedEventArgs e) => Bump(-1);

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Bump(e.Delta > 0 ? +1 : -1);
            e.Handled = true;
        }

        private void OnTextLostFocus(object sender, RoutedEventArgs e) => CommitText();

        private void OnTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Up)   { Bump(+1); e.Handled = true; }
            else if (e.Key == Key.Down) { Bump(-1); e.Handled = true; }
        }

        private void Bump(int dir) => Value = Clamp(Value + dir * Step);

        private double Clamp(double v) => Math.Max(Min, Math.Min(Max, v));

        private void CommitText()
        {
            if (_suppressTextSync) return;
            var raw = (ValueTextBox.Text ?? "").Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                Value = Clamp(v);
            else
                SyncTextFromValue();
        }

        private void SyncTextFromValue()
        {
            if (ValueTextBox == null) return;
            _suppressTextSync = true;
            try
            {
                ValueTextBox.Text = DecimalPlaces > 0
                    ? Value.ToString("F" + DecimalPlaces, CultureInfo.InvariantCulture)
                    : ((long)Math.Round(Value)).ToString(CultureInfo.InvariantCulture);
            }
            finally { _suppressTextSync = false; }
        }
    }
}

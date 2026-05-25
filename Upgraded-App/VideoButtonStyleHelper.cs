// **************************************************
// File: VideoButtonStyleHelper.cs
// Description: Factory methods for video-list button styles.
//              Extracted from MainWindow to keep styling logic self-contained.
// Author: Benjamin Kerr
// 2025 – 2026
// **************************************************

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FishLens_App
{
    /// <summary>
    /// Builds <see cref="Style"/> and <see cref="ControlTemplate"/> objects for
    /// video-list buttons.  All colour decisions live here so MainWindow stays
    /// free of raw hex literals and hardcoded brush logic.
    /// </summary>
    internal class VideoButtonStyleHelper
    {
        // UI constants (kept in sync with MainWindow constants)
        private const int BUTTON_CORNER_RADIUS = 6;
        private const int CONTENT_PRESENTER_MARGIN = 8;

        private readonly Func<string, string, SolidColorBrush> _resBrush;

        /// <param name="resBrush">
        ///   Delegate that resolves a named resource brush, falling back to
        ///   <paramref name="resBrush"/>'s second argument (hex string) when
        ///   the resource key is missing.  Pass <c>MainWindow.ResBrush</c>.
        /// </param>
        public VideoButtonStyleHelper(Func<string, string, SolidColorBrush> resBrush)
        {
            _resBrush = resBrush ?? throw new ArgumentNullException(nameof(resBrush));
        }

        // ------------------------------------------------------------------
        // Public entry point
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a complete <see cref="Style"/> for a video-list button.
        /// </summary>
        public Style CreateButtonStyle(bool isLowConfidence)
        {
            var style = new Style(typeof(Button));
            SetButtonDefaultAppearance(style, isLowConfidence);
            style.Setters.Add(new Setter(Button.TemplateProperty, CreateButtonControlTemplate(isLowConfidence)));
            return style;
        }

        // ------------------------------------------------------------------
        // Appearance setters
        // ------------------------------------------------------------------

        private void SetButtonDefaultAppearance(Style style, bool isLowConfidence)
        {
            if (isLowConfidence)
            {
                // Low-confidence: soft red tint background, bright red text.
                // Semi-transparent overlay works on both light and dark card backgrounds.
                style.Setters.Add(new Setter(Button.BackgroundProperty,
                    new SolidColorBrush(Color.FromArgb(40, 220, 38, 38))));
                style.Setters.Add(new Setter(Button.ForegroundProperty,
                    new SolidColorBrush(Color.FromRgb(239, 68, 68))));
                style.Setters.Add(new Setter(Button.BorderBrushProperty,
                    new SolidColorBrush(Color.FromArgb(80, 220, 38, 38))));
            }
            else
            {
                // Normal: resolve from theme resources so the button matches
                // the card in whichever theme is active.
                style.Setters.Add(new Setter(Button.BackgroundProperty,
                    _resBrush("CardBackground", "#FFFFFF")));
                style.Setters.Add(new Setter(Button.ForegroundProperty,
                    _resBrush("PrimaryText", "#0D3640")));
                style.Setters.Add(new Setter(Button.BorderBrushProperty,
                    _resBrush("BorderBrush", "#E1E8ED")));
            }
        }

        // ------------------------------------------------------------------
        // Control template
        // ------------------------------------------------------------------

        private ControlTemplate CreateButtonControlTemplate(bool isLowConfidence)
        {
            var template = new ControlTemplate(typeof(Button));
            template.VisualTree = CreateButtonBorder();
            AddButtonTriggers(template, isLowConfidence);
            return template;
        }

        private FrameworkElementFactory CreateButtonBorder()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(BUTTON_CORNER_RADIUS));
            border.AppendChild(CreateContentPresenter());
            return border;
        }

        private static FrameworkElementFactory CreateContentPresenter()
        {
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty,
                new Thickness(CONTENT_PRESENTER_MARGIN, 0, CONTENT_PRESENTER_MARGIN, 0));
            return cp;
        }

        // ------------------------------------------------------------------
        // Triggers
        // ------------------------------------------------------------------

        private void AddButtonTriggers(ControlTemplate template, bool isLowConfidence)
        {
            template.Triggers.Add(CreateHoverTrigger(isLowConfidence));
            template.Triggers.Add(CreatePressedTrigger(isLowConfidence));
        }

        private Trigger CreateHoverTrigger(bool isLowConfidence)
        {
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };

            if (isLowConfidence)
            {
                // Solid red fill on hover — warning becomes explicit.
                trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    new SolidColorBrush(Color.FromRgb(239, 68, 68)), "border"));
                trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                    new SolidColorBrush(Colors.White)));
            }
            else
            {
                // Subtle hover: slightly different shade of the card background.
                trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    _resBrush("BorderColorBrush", "#E1E8ED"), "border"));
                trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                    _resBrush("PrimaryText", "#0D3640")));
            }

            return trigger;
        }

        private Trigger CreatePressedTrigger(bool isLowConfidence)
        {
            var trigger = new Trigger { Property = Button.IsPressedProperty, Value = true };

            trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : _resBrush("AccentBrush", "#1B4F5C"),
                "border"));

            // On press, flip to on-accent text so it reads against the teal press colour.
            if (!isLowConfidence)
            {
                trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                    _resBrush("OnAccentForeground", "#F5F8FA")));
            }

            return trigger;
        }
    }
}
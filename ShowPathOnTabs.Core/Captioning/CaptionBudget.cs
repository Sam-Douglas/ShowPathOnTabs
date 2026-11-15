using System;
using System.Collections.Generic;

namespace ShowPathOnTabs.Core.Captioning
{
    /// <summary>
    /// How much room a tab has for its caption, and how to measure a caption against it.
    /// </summary>
    public sealed class CaptionBudget
    {
        private readonly Func<string, double> _measure;
        private readonly Dictionary<string, double> _widths = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <param name="maxWidth">The widest caption that is shown without being trimmed.</param>
        /// <param name="measure">Returns the width of a caption in the same units as <paramref name="maxWidth"/>.</param>
        public CaptionBudget(double maxWidth, Func<string, double> measure)
        {
            MaxWidth = maxWidth;
            _measure = measure ?? throw new ArgumentNullException(nameof(measure));
        }

        public double MaxWidth { get; }

        public bool Fits(string caption)
        {
            if (!_widths.TryGetValue(caption, out double width))
            {
                width = _measure(caption);
                _widths[caption] = width;
            }

            return width <= MaxWidth;
        }
    }
}

// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.GPALForm
{
    /// <summary>
    /// A GPAL chart instantiated with GPAL.Chart for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALChart.WindowsControl.Clicked event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALChart gPalChart = (GPALChart)GPAL.Chart
    ///     .WithPalette(ChartColorPalette.Bright)
    ///     .WithCallback&lt;EventHandler&gt;(ChartHandler) // invoked when chart gets clicked 
    ///     .WithName("Chart 1");
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalChart)
    ///     .ShowDialog();
    ///     
    /// private static void GenerateBarSeries()
    /// {
    ///     string[] seriesArray = { "Cat", "Dog", "Bird", "Monkey" };
    ///     int[] pointsArray = { 2, 1, 7, 5 };
    ///     
    ///     gPalChart.Titles.Add("Animals");
    ///     // Add series
    ///     for (int i = 0; i &lt; seriesArray.Length; i++)
    ///     {
    ///         Series series = gPalChart.Series.Add(seriesArray[i]);
    ///         // series.ChartType = SeriesChartType.Bar;
    ///         series.Points.Add(pointsArray[i]);
    ///         Legend legend = new Legend(seriesArray[i]);
    ///         // legend.Alignment = StringAlignment.Near;
    ///         gPalChart.Legends.Add(legend);
    ///     }
    /// }
    /// </code>
    /// </example>
    public class GPALChart : GPALControl, IAllowControlSettingsAndChartSettings
    {
        internal ChartColorPalette ChartColorPalette { get; set; }
        internal Series mySeries { get; set; }
        internal string Title { get; set; }

        internal GPALChart()
        {
            ControlType = FormControlType.Chart;
            Chart chart = new Chart
            {
                Name = Name,
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Dock = DockStyle.Fill,
            };
            chart.ChartAreas.Add(new ChartArea(Name));
            WindowsControl = chart;
        }
        /// <summary>
        /// Set the color palette of the chart bars.
        /// </summary>
        /// <param name="colorPalette">Enum of possible choices</param>
        /// <returns>Fluent interface to define more chart settings</returns>
        public IAllowControlSettingsAndChartSettings WithPalette(ChartColorPalette colorPalette)
        {
            ((Chart)WindowsControl).Palette = colorPalette;
            return this;
        }
        /// <summary>
        /// Sets the series to plot in the chart.
        /// </summary>
        /// <param name="series">The name of the series</param>
        /// <returns>Fluent interface to define more chart settings</returns>
        public IAllowControlSettingsAndChartSettings WithSeries(Series series)
        {
            ((Chart)WindowsControl).Series.Add(series);
            return this;
        }
        /// <summary>
        /// The title to display at the top of the chart.
        /// </summary>
        /// <param name="title">The title text</param>
        /// <returns>Fluent interface to define more chart settings</returns>
        public IAllowControlSettingsAndChartSettings WithTitle(string title)
        {
            ((Chart)WindowsControl).Titles.Add(title);
            return this;
        }
        /// <summary>
        /// Underlying Windows chart Series collection
        /// </summary>
        public SeriesCollection Series
        {
            get
            {
                return ((Chart)(WindowsControl)).Series;
            }
        }
        /// <summary>
        /// Underlying Windows chart Legends collection
        /// </summary>
        public LegendCollection Legends
        {
            get
            {
                return ((Chart)(WindowsControl)).Legends;
            }
        }
        /// <summary>
        /// Underlying Windows chart Titles collection
        /// </summary>
        public TitleCollection Titles
        {
            get
            {
                return ((Chart)(WindowsControl)).Titles;
            }
        }
        /// <summary>
        /// Underlying Windows chart areas collection
        /// probably will not make this public to manipulate
        /// </summary>
        internal ChartAreaCollection ChartAreas
        {
            get
            {
                return ((Chart)WindowsControl).ChartAreas;
            }
        }
    }
}

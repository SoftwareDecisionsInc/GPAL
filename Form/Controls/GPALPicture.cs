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
    /// A GPAL picture instantiated with GPAL.Picture, for showing an image on a GPAL form.<br/>
    /// The image can be set before the form is shown or while it is up, and either replaces what is there.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALPicture shot = (GPALPicture)GPAL.Picture
    ///     .WithImage("screenshot.png")
    ///     .WithHeight(300)
    ///     .WithName("shot")
    ///     .ToGPALObject();
    ///
    /// // and later, from a callback, with the form still up
    /// shot.WithImage("the.next.one.png");
    /// </code>
    /// </example>
    public class GPALPicture : GPALControl, IAllowPictureControlSettings
    {
        private System.Drawing.Image _image;
        private PictureBoxSizeMode _sizeMode = PictureBoxSizeMode.Zoom;

        internal GPALPicture()
        {
            ControlType = FormControlType.Picture;
        }

        /// <summary>
        /// The image on display.
        /// </summary>
        /// <returns>The image, null when none has been set</returns>
        public System.Drawing.Image Image => _image;

        /// <summary>
        /// How the image fits the control.
        /// </summary>
        /// <returns>The size mode currently in use</returns>
        public PictureBoxSizeMode SizeMode => _sizeMode;

        /// <summary>
        /// The image to display.
        /// </summary>
        /// <param name="image">The image</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowPictureControlSettings WithImage(System.Drawing.Image image)
        {
            _image = image;
            SyncImageToControl();
            return this;
        }

        /// <summary>
        /// Sets the image from a file.
        /// </summary>
        /// <remarks>
        /// The file is not held open, so the workflow can overwrite it.
        /// </remarks>
        /// <param name="file">The image file</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowPictureControlSettings WithImage(GPALFile file)
        {
            if (true == System.IO.File.Exists(file?.Filename))
                using (var stream = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(file.Filename)))
                    _image = new Bitmap(System.Drawing.Image.FromStream(stream));
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Image file not found for picture [{file?.Filename}]", this, GPALObjectType.Other);

            SyncImageToControl();
            return this;
        }

        /// <summary>
        /// How the image fits the control.
        /// </summary>
        /// <param name="sizeMode">Zoom by default, which scales the image to fit and keeps its shape</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowPictureControlSettings WithSizeMode(PictureBoxSizeMode sizeMode)
        {
            _sizeMode = sizeMode;
            SyncImageToControl();
            return this;
        }

        /// <summary>
        /// Set the image on the control if it exists, so a change while the form is open shows.
        /// </summary>
        private void SyncImageToControl()
        {
            if (WindowsControl is PictureBox box && true == Alive(box))
                OnUi(box, () =>
                {
                    box.SizeMode = _sizeMode;
                    box.Image = _image;
                });
        }
    }
}

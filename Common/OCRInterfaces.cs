// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public class OCRInterfaces
    {
        #region <Application Interfaces>
        #region <Base Application Interfaces>
        public interface IAllowOCRRegion
        {
            [Settings(Description = "The part of the image to read")]
            IAllowOCRSettings WithRegion(ClientRectangle region);
        }

        public interface IAllowOCRSettings : IAllowOCRRegion, IAllowToGPALObject<IGPALOCR>
        {
            [Settings(Description = "The direction the text runs in")]
            IAllowOCRSettingsOrExecute WithScanDirection(OCRScanDirection direction);
            [Settings(Description = "The language of the text")]
            IAllowOCRSettingsOrExecute WithLanguage(OCRLanguage lang);
            [Settings(Description = "Which OCR engine reads the image")]
            IAllowOCRSettingsOrExecute WithEngineMode(EngineMode mode);
            [Settings(Description = "Only these characters can be read")]
            IAllowOCRSettingsOrExecute WithCharacterWhitelist(string chars);
            [Settings(Description = "Only read a standard set of characters, such as digits")]
            IAllowOCRSettingsOrExecute WithStandardWhitelist(OCRWhitelist preset);
            [Settings(Description = "The resolution the image is read at")]
            IAllowOCRSettingsOrExecute WithDPI(int dpi);
            [Settings(Description = "How many times to try again when nothing is read")]
            IAllowOCRSettingsOrExecute WithRetryCount(int count);
            [Settings(Description = "How long reading may take")]
            IAllowOCRSettingsOrExecute WithTimeout(TimeSpan timeout);
        }

        public interface IAllowOCRSettingsOrExecute : IAllowOCRSettings
        {
            [Action(Description = "Save the text that was read into a grid")]
            IAllowOCRSettings SaveTo(IGPALGrid<string> grid);
        }
        public interface IGPALOCR : IAllowOCRSettingsOrExecute { }

        #endregion <Base Application Interfaces>
        #endregion <Application Interfaces>

        public interface IAllowToGPALObject<TResult>
        {
            TResult ToGPALObject();
        }

    }
}


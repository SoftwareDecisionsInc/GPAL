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
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    class ElementSettings
    {
        internal GPALElement WebElement { get; set; }
        internal Browser Browser { get; set; }
        internal Selector Selector { get; set; }
        internal InteractionType InteractionType { get; set; } = InteractionType.Selenium;
    }
}


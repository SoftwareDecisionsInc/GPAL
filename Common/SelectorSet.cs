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
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerallyPositive
{
    public  class SelectorSet
    {
        // must match SelectorSet.js
        // lowercase first letter to map to javascript names
        [WithCSS(Description="Method to use to find css")]
        public string css { get; set; }
        [WithXPath(Description="Method to use to find X Path")]
        public string xPath { get; set; }
        [WithXPath(Description = "Method to use to find X Path")]
        public string fullXPath { get; set; }
        [WithValue(Description = "The element's value attribute, as text")]
        public string value { get; set; }
        [WithText(Description = "The element's text")]
        public string text { get; set; }
        [MatchSrc(Description = "The element's src attribute")]
        public string src { get; set; }
        [MatchHRef(Description = "The element's href attribute")]
        public string href { get; set; }
        [MatchValue(Description = "Match the element whose value attribute is this")]
        public string matchvalue { get; set; }
        [MatchText(Description = "Match the element whose text is this")]
        public string matchtext { get; set; }
        [MatchHRef(Description = "Match the element whose href is this")]
        public string matchhref { get; set; }
        [MatchSrc(Description = "Match the element whose src is this")]
        public string matchsrc { get; set; }
        [MatchPlaceholder(Description = "Match the element whose placeholder is this")]
        public string placeholder { get; set; }
        public string tagName { get; set; }
        [WithSelectorName(Description = "The selector's name, used as the column header when its data is saved")]
        public string selectorName { get; set; }
        [WithOffsetX(Description = "Where to click inside the element, in pixels right of its left edge")]
        public int offsetX { get; set; }
        [WithOffsetY(Description = "Where to click inside the element, in pixels down from its top edge")]
        public int offsetY { get; set; }
        [WithDeltaX(Description = "How far to drag horizontally from where the element was grabbed, in pixels")]
        public int deltaX { get; set; }
        [WithDeltaY(Description = "How far to drag vertically from where the element was grabbed, in pixels")]
        public int deltaY { get; set; }
        [WithImage(Description = "An image of the element, for finding it by image matching")]
        public string imageData { get; set; }
        public List<ClientRectangle> clientRects { get; set; }
        public ClientRectangle boundingRect { get; set; }
        public string pageURL { get; set; }

        public class ClientRectangle
        {
            public float top;
            public float left;
            public float bottom;
            public float right;
            public float x;
            public float y;
            public float width;
            public float height;
        }

        public SelectorSet()
        {

        }
    }
}


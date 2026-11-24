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

using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Spreadsheet;
using HtmlAgilityPack;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using YamlDotNet.Core;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Custom objet factory for our classes which do not have public constructors
    /// </summary>
    public static class ObjectFactory
    {
        /// <summary>
        /// Creates an instance of the specified type using reflection, looking for non-public constructors if necessary.
        /// </summary>
        /// <typeparam name="T">The type to instantiate.</typeparam>
        /// <returns>An instance of T.</returns>
        public static T CreateInstance<T>() where T : class
        {
            Type type = typeof(T);
            var constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null
            );

            if (constructor != null)
            {
                return (T)constructor.Invoke(new object[0]);
            }
            else
            {
                throw new InvalidOperationException($"No suitable constructor found for type {type.Name}.");
            }
        }
    }
}

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

//extern alias mUIA;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using static GenerallyPositive.Enums;
// The HiddenDesktop property hides the static HiddenDesktop class, so the class is used through this alias. Browser
// does the same.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    public partial class Application
    {
        /// <summary>
        /// Clicks the element that raises an Open dialog, waits for the dialog, and answers it with the given
        /// file or folder.
        /// </summary>
        /// <remarks>
        /// Needs no keystrokes or focus, so it works on a hidden desktop. Also answers Save As and the folder picker. A
        /// GPALFile naming several files answers a multi-select dialog.
        /// </remarks>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(attachButton)
        ///         .LeftClickAndUpload(@"C:\invoices\march.pdf");
        /// </code>
        /// </example>
        /// <param name="file">The file or folder to answer the dialog with</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClickAndOpen(GPALFile file)
        {
            return ClickAndAnswer(file);
        }

        /// <summary>
        /// Clicks the element that raises an Open dialog and answers it with what is on the clipboard. FileList
        /// answers with the copied files. Every other type is written to a file first, named by WithFilename or
        /// clipboard plus the type's extension, in GPAL's temp folder.
        /// </summary>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(attachButton)
        ///         .LeftClickAndOpen(GPAL.Clipboard.GetClipboard(ClipboardType.Image).WithFilename("receipt.png"));
        /// </code>
        /// </example>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClickAndOpen(IAllowClipboardInto clipboard)
        {
            if (true == InAction(true))
            {
                GPALFile file = ((GPALClipboard)clipboard).FileToUpload();

                if (null != file)
                {
                    ElementHelper.LeftClick(this, ModifierKeys.NONE);

                    FileDialogHelper.Answer(this, file, ApplicationSettings.WaitForWindowTimeout);
                }
            }

            return this;
        }

        /// <summary>
        /// Clicks the element that raises a Save dialog, waits for the dialog, and answers it with the given file. An
        /// existing file of that name is deleted first, so no replace prompt appears.
        /// </summary>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(saveAsMenuItem)
        ///         .LeftClickAndSave(@"C:\reports\march.txt");
        /// </code>
        /// </example>
        /// <param name="file">The file to save as</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClickAndSave(GPALFile file)
        {
            ClearTheWay(file);

            return ClickAndAnswer(file);
        }

        /// <summary>
        /// Deletes the file a Save is about to write, so the dialog never asks whether to replace it.
        /// </summary>
        /// <param name="file">The file being saved</param>
        private void ClearTheWay(GPALFile file)
        {
            foreach (string name in file?.Filenames ?? new List<string>())
                if (true == System.IO.File.Exists(name))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Deleting [{name}] before saving over it, so the dialog has nothing to ask about", this, GPALObjectType.Application);

                    System.IO.File.Delete(name);
                }
        }

        /// <summary>
        /// Clicks whatever the unit of work found and answers the dialog it raises.
        /// </summary>
        /// <param name="file">The file or folder to answer with</param>
        /// <returns>Fluent interface to write your workflow</returns>
        private IAllowSelectorActionOrAnySelector ClickAndAnswer(GPALFile file)
        {
            if (true == InAction(true))
            {
                ElementHelper.LeftClick(this, ModifierKeys.NONE);

                FileDialogHelper.Answer(this, file, ApplicationSettings.WaitForWindowTimeout);
            }

            return this;
        }

        /// <summary>
        /// Answers a file dialog that is already on screen with the given file or folder.
        /// </summary>
        /// <param name="file">The file or folder to answer the dialog with</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AnswerFileDialog(GPALFile file)
        {
            if (true == InAction(true))
                FileDialogHelper.Answer(this, file, ApplicationSettings.WaitForWindowTimeout);

            return this;
        }
    }
}

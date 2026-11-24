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

using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.GPALForm.GPALForm;
using static GenerallyPositive.Enums;
using GenerallyPositive.GPALForm;

namespace GenerallyPositive.GPALForm
{
    public partial class GPALForm
    {
        /// <summary>
        /// Creates the underlying Windows Forms StatusStrip and label for a GPALStatusStrip, wires its click callback and tooltip, and docks it to the bottom of the form.
        /// </summary>
        /// <param name="gPALStatusStrip">GPAL status strip definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddStatusStrip(GPALStatusStrip gPALStatusStrip)
        {
            FormSettings.myForm.ControlList.Add(gPALStatusStrip);

            StatusStrip statusStrip = new StatusStrip
            {
                Name = gPALStatusStrip.Name,
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                Enabled = gPALStatusStrip.IsEnabled,
                Height = 10 < gPALStatusStrip.Height ? gPALStatusStrip.Height : 20,
            };

            ToolStripStatusLabel statusLabel = new ToolStripStatusLabel
            {
                Text = gPALStatusStrip.Text,
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 10 < gPALStatusStrip.Height ? gPALStatusStrip.Height : 20,
            };

            statusStrip.Items.Add(statusLabel);

            gPALStatusStrip.WindowsControl = statusStrip;

            foreach (var (evt, handler) in gPALStatusStrip.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Click:
                    case ControlEventType.Default:
                        statusStrip.ItemClicked += (sender, e) => ((EventHandler)handler)(sender, e);
                        break;

                        // ignore unsupported
                }
            }

            // StatusStrip is added directly to the form (not TableLayoutPanel)
            FormSettings.myForm.Controls.Add(statusStrip);

            if (!string.IsNullOrWhiteSpace(gPALStatusStrip.ToolTipText))
                ApplyToolTip(gPALStatusStrip, statusStrip);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms MenuStrip for a GPALMenuBar, wires item callbacks, and docks it to the top of the form.
        /// </summary>
        /// <param name="gPALMenuBar">GPAL menu bar definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddMenuBar(GPALMenuBar gPALMenuBar)
        {
            FormSettings.myForm.ControlList.Add(gPALMenuBar);

            MenuStrip menuStrip = new MenuStrip
            {
                Name = gPALMenuBar.Name,
                Enabled = gPALMenuBar.IsEnabled,
            };

            var topMenus = new System.Collections.Generic.Dictionary<string, ToolStripMenuItem>();
            foreach (var (topMenu, item, isSeparator) in gPALMenuBar.menuEntries)
            {
                string menuKey = string.IsNullOrWhiteSpace(topMenu) ? "" : topMenu;
                if (!topMenus.TryGetValue(menuKey, out ToolStripMenuItem topItem))
                {
                    topItem = new ToolStripMenuItem(menuKey);
                    topMenus[menuKey] = topItem;
                    menuStrip.Items.Add(topItem);
                }

                if (isSeparator)
                {
                    topItem.DropDownItems.Add(new ToolStripSeparator());
                    continue;
                }

                ToolStripMenuItem menuItem = new ToolStripMenuItem(item.Text);
                if (item.Image != null)
                    menuItem.Image = item.Image;
                if (item.ShortCutKey != System.Windows.Forms.Keys.None)
                {
                    menuItem.ShortcutKeys = item.ShortCutKey;
                    menuItem.ShowShortcutKeys = true;
                }
                // Wire each handler to its ForEvent event; Click is the default.
                foreach (var (evt, handler) in item.Callbacks)
                {
                    if (null == handler) continue; // skip invalid

                    switch (evt)
                    {
                        case ControlEventType.Default:
                        case ControlEventType.Click:
                            menuItem.Click += (EventHandler)handler;
                            break;

                        case ControlEventType.DoubleClick:
                            menuItem.DoubleClick += (EventHandler)handler;
                            break;
                            // ignore unsupported
                    }
                }
                if (!string.IsNullOrWhiteSpace(item.ToolTipText))
                    menuItem.ToolTipText = item.ToolTipText;

                // Apply the WithEnabled value set before the item existed.
                menuItem.Enabled = item.IsEnabled;
                item.WindowsControl = menuItem;
                topItem.DropDownItems.Add(menuItem);
            }

            gPALMenuBar.WindowsControl = menuStrip;

            // MenuStrip docks to the top of the form (not the TableLayoutPanel)
            FormSettings.myForm.Controls.Add(menuStrip);
            FormSettings.myForm.MainMenuStrip = menuStrip;

            // A top-docked MenuStrip must sit above any top-docked toolbar. Docked siblings fill from the
            // edge in reverse child-index order, so pushing the menu to the highest index keeps it outermost.
            FormSettings.myForm.Controls.SetChildIndex(menuStrip, FormSettings.myForm.Controls.Count - 1);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms ToolStrip for a GPALToolbar, wires each button's callback,
        /// image and tooltip, and docks it to the top of the form (below the menu bar).
        /// </summary>
        /// <param name="gPALToolbar">GPAL toolbar definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddToolbar(GPALToolbar gPALToolbar)
        {
            FormSettings.myForm.ControlList.Add(gPALToolbar);

            ToolStrip toolStrip = new ToolStrip
            {
                Name = gPALToolbar.Name,
                Enabled = gPALToolbar.IsEnabled,
            };

            foreach (var (item, isSeparator) in gPALToolbar.toolbarEntries)
            {
                if (isSeparator)
                {
                    toolStrip.Items.Add(new ToolStripSeparator());
                    continue;
                }

                ToolStripButton button = new ToolStripButton(item.Text);
                if (item.Image != null)
                {
                    button.Image = item.Image;
                    button.DisplayStyle = string.IsNullOrWhiteSpace(item.Text)
                        ? ToolStripItemDisplayStyle.Image
                        : ToolStripItemDisplayStyle.ImageAndText;
                }
                else
                    button.DisplayStyle = ToolStripItemDisplayStyle.Text;

                // As with the menu bar, use the requested event.
                foreach (var (evt, handler) in item.Callbacks)
                {
                    if (null == handler) continue; // skip invalid

                    switch (evt)
                    {
                        case ControlEventType.Default:
                        case ControlEventType.Click:
                            button.Click += (EventHandler)handler;
                            break;

                        case ControlEventType.DoubleClick:
                            button.DoubleClick += (EventHandler)handler;
                            break;
                            // ignore unsupported
                    }
                }
                if (!string.IsNullOrWhiteSpace(item.ToolTipText))
                    button.ToolTipText = item.ToolTipText;

                button.Enabled = item.IsEnabled;
                item.WindowsControl = button;
                toolStrip.Items.Add(button);
            }

            gPALToolbar.WindowsControl = toolStrip;

            // ToolStrip docks to the top of the form (not the TableLayoutPanel), below any MenuStrip.
            FormSettings.myForm.Controls.Add(toolStrip);
            if (FormSettings.myForm.MainMenuStrip != null)
                FormSettings.myForm.Controls.SetChildIndex(FormSettings.myForm.MainMenuStrip, FormSettings.myForm.Controls.Count - 1);

            return this;
        }
        /// <summary>
        /// Adds a new tab page to the form's TabControl (creating the TabControl on first use), and switches the active TableLayoutPanel to the new tab's panel so subsequent controls are added to it.
        /// </summary>
        /// <param name="gPALTab">GPAL tab definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTab(GPALTab gPALTab)
        {
            if (null == FormSettings.TabControl)
            {
                FormSettings.TabControl = new TabControl
                {
                    Name = "tabControl",
                    AutoSize = false,
                    //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                    Dock = DockStyle.Fill,
                    Multiline = true,

                };

                // A disabled TabPage greys its contents but can still be selected, so disable a tab by refusing the
                // switch.
                FormSettings.TabControl.Selecting += (sender, args) =>
                {
                    // Nothing selected yet means pages are still being added; do not refuse.
                    if (null == FormSettings.TabControl.SelectedTab) return;

                    if (null != args.TabPage && false == args.TabPage.Enabled) args.Cancel = true;
                };

                FormSettings.TableLayoutPanel.Controls.Add(FormSettings.TabControl);
            }

            TabPage tabPage = new TabPage
            {
                Name = gPALTab.Name,
                Text = gPALTab.Text,
                AutoSize = false,
                //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Dock = DockStyle.Fill,
                Enabled = gPALTab.Enabled
            };

            TableLayoutPanel tmpTableLayoutPanel = new TableLayoutPanel
            {
                //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Dock = DockStyle.Fill,
                AutoSize = false,

                // As on the form's own panel: without this, an overfull tab hides its bottom controls with no
                // scrollbar.
                AutoScroll = true
            };

            gPALTab.WindowsControl = tabPage;

            FormSettings.myForm.ControlList.Add(gPALTab);

            foreach (var (evt, handler) in gPALTab.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Scroll:
                        tabPage.Scroll += (ScrollEventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        // The page's own Click is a click on its blank area, so it must be requested by name.
                        tabPage.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.SelectedIndexChanged:
                    case ControlEventType.Change:
                        // A tab's default event is becoming the visible tab. Only the TabControl raises that, naming
                        // the page, so each tab gets only its own.
                        FormSettings.TabControl.Selected += (sender, args) =>
                        {
                            if (args.TabPage == tabPage)
                                ((EventHandler)handler)(gPALTab, args);
                        };
                        break;
                        // ignore unsupported
                }
            }

            tabPage.Controls.Add(tmpTableLayoutPanel);
            FormSettings.TabControl.Controls.Add(tabPage);
            FormSettings.TableLayoutPanel = tmpTableLayoutPanel;

            if (!string.IsNullOrWhiteSpace(gPALTab.ToolTipText))
                ApplyToolTip(gPALTab, tabPage);

            return this;
        }
        /// <summary>
        /// Builds a GPALSplitter: a SplitContainer whose two panes each have a layout panel, filled with the controls
        /// before and after the pane marker.<br/>
        /// A pane can hold another splitter or a group box.
        /// </summary>
        /// <param name="gPALSplitter">Splitter definition to build.</param>

        private IAllowFormSettingsAndActions AddSplitter(GPALSplitter gPALSplitter)
        {
            FormSettings.myForm.ControlList.Add(gPALSplitter);

            SplitContainer sc = new SplitContainer
            {
                Name = gPALSplitter.Name,
                Enabled = gPALSplitter.IsEnabled,
                // a vertical bar gives left and right panes, a horizontal one top and bottom
                Orientation = true == gPALSplitter.IsHorizontalSplit ? Orientation.Vertical : Orientation.Horizontal,
                // Anchor instead of docking, like every other GPAL control: a docked SplitContainer in a
                // TableLayoutPanel cell loses its height. Its height is measured below once the panes are filled,
                // unless .WithHeight set it. Without its own height, its row takes the form's spare height, and
                // anchoring the bottom fills it, as a list view does.
                Anchor = 10 < gPALSplitter.Height
                    ? AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                    : AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Height = 10 < gPALSplitter.Height ? gPALSplitter.Height : 0,
                AutoScaleMode = AutoScaleMode.Inherit,
                // A set height means a fixed band rather than a container that takes the remaining space.
                Tag = 10 < gPALSplitter.Height ? SetHeightMarker : null
            };

            if (true == gPALSplitter.IsPercentage)
            {
                double pct = gPALSplitter.Percentage;

                // a proportion has to be reapplied as the form resizes, where a pixel distance stays put
                sc.SizeChanged += (s, e) =>
                {
                    int along = true == gPALSplitter.IsHorizontalSplit ? sc.Width : sc.Height;

                    if (along > sc.Panel1MinSize + sc.Panel2MinSize + sc.SplitterWidth)
                        sc.SplitterDistance = (int)(along * pct);
                };
            }
            else
                sc.SplitterDistance = gPALSplitter.Dimension;

            gPALSplitter.WindowsControl = sc;

            // The divider position is the default event when none is named.
            foreach (var (evt, handler) in gPALSplitter.Callbacks)
            {
                if (null == handler) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.SplitterMoved:
                        sc.SplitterMoved += (SplitterEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        sc.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        sc.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            TableLayoutPanel firstPane = MakeSplitPaneTlp();
            TableLayoutPanel secondPane = MakeSplitPaneTlp();

            sc.Panel1.Controls.Add(firstPane);
            sc.Panel2.Controls.Add(secondPane);
            FormSettings.TableLayoutPanel.Controls.Add(sc);

            FillContainer(gPALSplitter.FormControls, firstPane, secondPane, gPALSplitter.Name);


            // A SplitContainer cannot size to its contents, so measure it: side-by-side panes are as tall as the taller
            // one; stacked panes are both plus the bar. .WithHeight overrides this.
            if (10 >= gPALSplitter.Height)
            {
                // A pane's PreferredSize returns its current height; add up its controls' heights instead. Each row
                // is its control's height plus its top and bottom margins, so all three are counted.
                Func<TableLayoutPanel, int> contentHeight = pane =>
                {
                    int total = pane.Padding.Top + pane.Padding.Bottom;

                    foreach (Control child in pane.Controls)
                        total += child.Margin.Top + child.Height + child.Margin.Bottom;

                    return total;
                };


                sc.Height = true == gPALSplitter.IsHorizontalSplit
                    ? Math.Max(contentHeight(firstPane), contentHeight(secondPane))
                    : contentHeight(firstPane) + contentHeight(secondPane) + sc.SplitterWidth;
            }

            if (!string.IsNullOrWhiteSpace(gPALSplitter.ToolTipText))
                ApplyToolTip(gPALSplitter, sc);

            return this;
        }

        // what a GroupBox pads by on its own, kept here because the bottom is adjusted from it
        private const int GroupBoxPadding = 3;

        // how much further the bottom is padded so the last control's border clears the box's border
        private const int GroupBoxBottomClearance = 2;

        /// <summary>
        /// Realizes a GPALGroupBox: a titled box with its own layout panel holding everything that was added to it.
        /// </summary>
        /// <param name="gPALGroupBox">Group box definition to realize.</param>
        private IAllowFormSettingsAndActions AddGroupBox(GPALGroupBox gPALGroupBox)
        {
            FormSettings.myForm.ControlList.Add(gPALGroupBox);

            GroupBox gb = new GroupBox
            {
                Name = gPALGroupBox.Name,
                Text = gPALGroupBox.Text,
                Enabled = gPALGroupBox.IsEnabled,
                // Full width, only as tall as its contents. Docking to fill stops the box growing. CAVEAT: DockStyle is
                // not a flags enum, so Top | Bottom is Left.
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                // The inner panel measures short and the last control's bottom border falls under the box's border;
                // this extra padding brings it back.
                Padding = new Padding(GroupBoxPadding, GroupBoxPadding, GroupBoxPadding, GroupBoxPadding + GroupBoxBottomClearance),
            };

            // The inner panel must also size to its contents. A splitter pane fills instead, so this is not
            // MakeSplitPaneTlp.
            TableLayoutPanel inside = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                //AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
            };

            inside.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            gb.Controls.Add(inside);
            FormSettings.TableLayoutPanel.Controls.Add(gb);

            gPALGroupBox.WindowsControl = gb;

            FillContainer(gPALGroupBox.FormControls, inside, null, gPALGroupBox.Name);

            if (!string.IsNullOrWhiteSpace(gPALGroupBox.ToolTipText))
                ApplyToolTip(gPALGroupBox, gb);

            return this;
        }

        /// <summary>
        /// Adds a container's controls to its panel, switching to <paramref name="secondPane"/> at the pane marker.<br/>
        /// Each control is built by the same WithFormControl dispatch as a top-level control, so a container can hold
        /// anything a form can.
        /// </summary>
        /// <param name="formControls">What was added to the container, in order, marker included.</param>
        /// <param name="firstPane">Panel everything before the marker goes into.</param>
        /// <param name="secondPane">Panel everything after it goes into, null for a container with one pane.</param>
        /// <param name="name">Container name, for anything worth reporting.</param>
        private void FillContainer(List<object> formControls, TableLayoutPanel firstPane, TableLayoutPanel secondPane, string name)
        {
            TableLayoutPanel outerPanel = FormSettings.TableLayoutPanel;
            TabControl outerTabControl = FormSettings.TabControl;
            TableLayoutPanel current = firstPane;
            bool markerSeen = false;

            FormSettings.TabControl = null;

            foreach (object formControl in formControls)
            {
                if (formControl is GPALSpliter)
                {
                    if (null == secondPane)
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{name}] has no second pane, so the pane marker does nothing. Everything added stays in the one panel.");
                    else if (true == markerSeen)
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{name}] has more than one pane marker and a splitter has only two panes, so everything after the first marker stays in the second.");
                    else
                    {
                        current = secondPane;
                        markerSeen = true;
                    }

                    continue;
                }

                FormSettings.TableLayoutPanel = current;
                FormSettings.TabControl = null;
                WithFormControl((GPALControl)formControl);
            }

            FormSettings.TableLayoutPanel = outerPanel;
            FormSettings.TabControl = outerTabControl;
        }

        private TableLayoutPanel MakeSplitPaneTlp()
        {
            var tlp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ColumnCount = 1,
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return tlp;
        }

        /// <summary>
        /// Creates the underlying Windows Forms TableLayoutPanel for a GPALTableLayoutPanel and applies its tooltip.
        /// </summary>
        /// <param name="gPALTableLayoutPanel">GPAL table layout panel definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTableLayoutPanel(GPALTableLayoutPanel gPALTableLayoutPanel)
        {
            FormSettings.myForm.ControlList.Add(gPALTableLayoutPanel);
            TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
            {
                Name = gPALTableLayoutPanel.Name,
                GrowStyle = gPALTableLayoutPanel.GrowStyle,
                AutoScroll = false,
                ColumnCount = gPALTableLayoutPanel.ColumnCount,
                RowCount = gPALTableLayoutPanel.RowCount,
                Enabled = gPALTableLayoutPanel.IsEnabled,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Set WindowsControl, as every other Add does; otherwise the panel is dropped and later reads take the
            // not-yet-built path.
            gPALTableLayoutPanel.WindowsControl = tableLayoutPanel;

            FormSettings.TableLayoutPanel.Controls.Add(tableLayoutPanel);

            if (!string.IsNullOrWhiteSpace(gPALTableLayoutPanel.ToolTipText))
                ApplyToolTip(gPALTableLayoutPanel, tableLayoutPanel);

            return this;
        }
    }
}

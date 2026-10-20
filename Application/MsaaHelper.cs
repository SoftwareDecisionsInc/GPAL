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

using Accessibility;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Application
{
    /// <summary>
    /// Reads and selects controls through MSAA, which some frameworks publish to instead of UIAutomation.
    /// </summary>
    /// <remarks>
    /// The interface behind LegacyIAccessiblePattern, called through oleacc because the managed UIAutomation client
    /// lacks that pattern. Elements are matched to MSAA nodes by screen rectangle.
    /// </remarks>
    internal static class MsaaHelper
    {
        private const uint OBJID_CLIENT = 0xFFFFFFFC;
        private const int STATE_SYSTEM_SELECTED = 0x00000002;
        private const int SELFLAG_TAKEFOCUS = 0x00000001;
        private const int SELFLAG_TAKESELECTION = 0x00000002;
        private const int CHILDID_SELF = 0;

        // Limits on accessible tree depth and node count. A ribbon tab is four levels down; these only stop a provider
        // that lists its parent as a child from looping forever.
        private const int DepthLimit = 12;
        private const int NodeLimit = 4000;

        // Rectangles match within one pixel, for a provider that rounds where another truncates.
        private const double RectSlack = 2.0;

        private static Guid IID_IAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid interfaceId,
                                                             [MarshalAs(UnmanagedType.Interface)] out object accessible);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleChildren([MarshalAs(UnmanagedType.Interface)] IAccessible container,
                                                     int startingChild, int wanted,
                                                     [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct)] object[] children,
                                                     out int obtained);

        /// <summary>
        /// One control as MSAA addresses it: the owning object and a child number.
        /// </summary>
        /// <remarks>
        /// A control that is its own object has ChildId CHILDID_SELF.
        /// </remarks>
        private class Node
        {
            internal IAccessible Owner;
            internal object ChildId;
        }

        /// <summary>
        /// Whether MSAA says the control is selected.
        /// </summary>
        /// <param name="application">The Application being driven, for the log</param>
        /// <param name="element">The control to ask about</param>
        /// <returns>True or false as MSAA has it, null when MSAA cannot be asked about this control</returns>
        internal static bool? Selected(Application application, AutomationElement element)
        {
            bool? retVal = null;
            Node node = NodeFor(application, element);

            if (null != node)
            {
                int state = StateOf(node);

                retVal = STATE_SYSTEM_SELECTED == (state & STATE_SYSTEM_SELECTED);
            }

            return retVal;
        }

        /// <summary>
        /// Selects the control through MSAA, as clicking a tab does, without the mouse.
        /// </summary>
        /// <remarks>
        /// Takes focus as well as selection; a tab strip given only the selection can mark the tab without changing the
        /// page.
        /// </remarks>
        /// <param name="application">The Application being driven, for the log</param>
        /// <param name="element">The control to select</param>
        /// <returns>True when MSAA took the selection and the control now reads as selected</returns>
        internal static bool Select(Application application, AutomationElement element)
        {
            bool retVal = false;
            Node node = NodeFor(application, element);

            if (null != node)
            {
                try
                {
                    node.Owner.accSelect(SELFLAG_TAKEFOCUS | SELFLAG_TAKESELECTION, node.ChildId);

                    retVal = STATE_SYSTEM_SELECTED == (StateOf(node) & STATE_SYSTEM_SELECTED);
                }
                catch (Exception)
                {
                    // A provider refuses a selection by throwing. That is a normal answer; the caller goes on to the
                    // mouse, so report it like the other route messages.
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"MSAA would not select [{NameOf(node)}], so it was not selected that way.", application, GPALObjectType.Application);
                }
            }

            return retVal;
        }

        /// <summary>
        /// Presses the control through MSAA's default action.
        /// </summary>
        /// <remarks>
        /// For buttons UIAutomation publishes as a Pane with no InvokePattern. Needs no mouse, so it works on a hidden
        /// desktop.
        /// </remarks>
        /// <param name="application">The Application being driven, for the log</param>
        /// <param name="element">The control to press</param>
        /// <returns>True when MSAA performed the default action</returns>
        internal static bool Press(Application application, AutomationElement element)
        {
            bool retVal = false;
            Node node = NodeFor(application, element);

            if (null != node)
            {
                try
                {
                    node.Owner.accDoDefaultAction(node.ChildId);

                    retVal = true;
                }
                catch (Exception)
                {
                    // A control with no default action throws. That is a normal answer; the caller goes on to the
                    // mouse.
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"MSAA has no default action for [{NameOf(node)}], so it was not pressed that way.", application, GPALObjectType.Application);
                }
            }

            return retVal;
        }

        /// <summary>
        /// The MSAA node for a UIAutomation element, found by walking the accessible tree of the window the
        /// element lives in and matching on screen rectangle.
        /// </summary>
        /// <param name="application">The Application being driven, for the log</param>
        /// <param name="element">The control to find</param>
        /// <returns>The node, or null when the window, the tree or the rectangle does not give one</returns>
        private static Node NodeFor(Application application, AutomationElement element)
        {
            Node retVal = null;
            Rect wanted = Rect.Empty;
            IntPtr window = IntPtr.Zero;

            // A dialog closes when its button is pressed, and the button then throws on every query. That is the
            // expected result.
            try
            {
                wanted = null == element ? Rect.Empty : element.Current.BoundingRectangle;
                window = WindowOf(element);
            }
            catch (Exception)
            {
                wanted = Rect.Empty;
            }

            // A control with no rectangle cannot be matched, and one in no window has no tree to search.
            if (false == wanted.IsEmpty && IntPtr.Zero != window)
            {
                object root;

                if (0 == AccessibleObjectFromWindow(window, OBJID_CLIENT, ref IID_IAccessible, out root) && root is IAccessible accessible)
                {
                    int looked = 0;

                    try
                    {
                        retVal = Search(new Node { Owner = accessible, ChildId = CHILDID_SELF }, wanted, 0, ref looked);
                    }
                    catch (Exception)
                    {
                        // A window that closes while being walked takes its nodes with it.
                        retVal = null;
                    }
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"MSAA node at [{wanted}] in window [{window}] was [{(null == retVal ? "not found" : NameOf(retVal))}].", application, GPALObjectType.Application);

            return retVal;
        }

        /// <summary>
        /// Depth first search of an accessible node and everything under it for the one holding a rectangle.
        /// </summary>
        /// <param name="node">The node being looked at</param>
        /// <param name="wanted">The screen rectangle to match</param>
        /// <param name="depth">How far down the tree this node is</param>
        /// <param name="looked">Running count of nodes examined, against the ceiling</param>
        /// <returns>The matching node, or null</returns>
        private static Node Search(Node node, Rect wanted, int depth, ref int looked)
        {
            Node retVal = null;

            if (depth <= DepthLimit && ++looked <= NodeLimit)
            {
                if (true == Matches(node, wanted))
                    retVal = node;
                else
                {
                    foreach (Node child in ChildrenOf(node))
                    {
                        retVal = Search(child, wanted, depth + 1, ref looked);

                        if (null != retVal)
                            break;
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// Returns the children of an accessible node.
        /// </summary>
        /// <param name="node">The node whose children to read</param>
        /// <returns>The children; empty when it has none or will not report them</returns>
        private static System.Collections.Generic.List<Node> ChildrenOf(Node node)
        {
            var retVal = new System.Collections.Generic.List<Node>();

            // Only an object can have children. A numbered child is a leaf; asking its owner again loops.
            if (true == IsSelf(node.ChildId))
            {
                int count = 0;

                try
                {
                    count = node.Owner.accChildCount;
                }
                catch (Exception)
                {
                    // A provider that will not report its child count has none.
                    count = 0;
                }

                if (0 < count)
                {
                    object[] children = new object[count];
                    int obtained;

                    if (0 == AccessibleChildren(node.Owner, 0, count, children, out obtained))
                        for (int child = 0; child < obtained; child++)
                            if (children[child] is IAccessible ownObject)
                                retVal.Add(new Node { Owner = ownObject, ChildId = CHILDID_SELF });
                            else if (null != children[child])
                                retVal.Add(new Node { Owner = node.Owner, ChildId = children[child] });
                }
            }

            return retVal;
        }

        /// <summary>Whether a node sits at the rectangle being looked for.</summary>
        /// <param name="node">The node to measure</param>
        /// <param name="wanted">The screen rectangle to match</param>
        /// <returns>True when the two rectangles describe the same place on screen</returns>
        private static bool Matches(Node node, Rect wanted)
        {
            bool retVal = false;
            int left, top, width, height;

            try
            {
                node.Owner.accLocation(out left, out top, out width, out height, node.ChildId);

                retVal = RectSlack > Math.Abs(left - wanted.Left)
                      && RectSlack > Math.Abs(top - wanted.Top)
                      && RectSlack > Math.Abs(width - wanted.Width)
                      && RectSlack > Math.Abs(height - wanted.Height);
            }
            catch (Exception)
            {
                // MSAA reports "not implemented" by failing, which the interop throws. A node with no location is not
                // the one being searched for.
                retVal = false;
            }

            return retVal;
        }

        /// <summary>The MSAA state bits for a node, zero when it will not say.</summary>
        /// <param name="node">The node to read</param>
        /// <returns>The state bits, or zero</returns>
        private static int StateOf(Node node)
        {
            int retVal = 0;

            try
            {
                object state = node.Owner.get_accState(node.ChildId);

                if (state is int bits)
                    retVal = bits;
            }
            catch (Exception)
            {
                retVal = 0;
            }

            return retVal;
        }

        /// <summary>The name a node calls itself, for the log.</summary>
        /// <param name="node">The node to read</param>
        /// <returns>The name, or a placeholder when it has none</returns>
        private static string NameOf(Node node)
        {
            string retVal = "unnamed";

            try
            {
                string name = node.Owner.get_accName(node.ChildId);

                if (false == string.IsNullOrWhiteSpace(name))
                    retVal = name;
            }
            catch (Exception)
            {
                retVal = "unnamed";
            }

            return retVal;
        }

        /// <summary>Whether a child id means the node itself rather than one of its numbered children.</summary>
        /// <param name="childId">The child id to read</param>
        /// <returns>True when the id is the one meaning "myself"</returns>
        private static bool IsSelf(object childId)
        {
            return childId is int id && CHILDID_SELF == id;
        }

        /// <summary>
        /// Returns the window a control is in: its own handle, or the nearest ancestor's.
        /// </summary>
        /// <param name="element">The control to start from</param>
        /// <returns>The window handle, or zero when nothing above it has one</returns>
        private static IntPtr WindowOf(AutomationElement element)
        {
            IntPtr retVal = IntPtr.Zero;
            AutomationElement current = element;

            for (int level = 0; level <= DepthLimit && IntPtr.Zero == retVal && null != current; level++)
            {
                if (0 != current.Current.NativeWindowHandle)
                    retVal = new IntPtr(current.Current.NativeWindowHandle);
                else
                    current = TreeWalker.ControlViewWalker.GetParent(current);
            }

            return retVal;
        }
    }
}

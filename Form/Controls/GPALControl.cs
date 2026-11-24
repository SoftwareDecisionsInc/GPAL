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
    /// Base class for all GPAL form controls
    /// </summary>
    public class GPALControl : IAllowControlSettingsAndFontSettings, IAllowToolTipDelay, IAllowForEventControlSettings
    {
        /// <summary>
        /// The font currently being used
        /// </summary>
        /// <returns>The font currently in use</returns>
        public Font FontType { get; internal set; } = new Font("Georgia", 12);
        /// <summary>
        /// The alignment currently in use
        /// </summary>
        /// <returns>The alignment currently being used</returns>
        public ContentAlignment TextAlignment { get; internal set; } = ContentAlignment.MiddleLeft;
        /// <summary>
        /// Autosize setting currently in use
        /// </summary>
        /// <returns>Autosize setting currently in use</returns>
        public bool AutoSize { get; internal set; } = true;
        //// post-v1: control alignment
        ///// <summary>
        ///// The alignment of the control itself within its container.
        ///// </summary>
        ///// <returns>The control alignment currently in use.</returns>
        //public ContentAlignment ControlAlignment { get; internal set; } = ContentAlignment.MiddleLeft;
        /// <summary>
        /// The font style and size for the label display.
        /// </summary>
        /// <param name="font">Default is ("Georgia", 16)</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndFontSettings WithFont(Font font)
        {
            FontType = font;
            return this;
        }
        /// <summary>
        /// The alignment of the text inside the label.<br/>
        /// </summary>
        /// <param name="textAlignment">Alignment of the text. Default is MiddleLeft</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndFontSettings WithTextAlignment(ContentAlignment textAlignment)
        {
            TextAlignment = textAlignment;
            return this;
        }

        /// <summary>
        /// Whether or not the label contents autosize to fit the label.<br></br>
        /// </summary>
        /// <param name="autoSize">True [default] to autosize.<br/>False to control it yourself.</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndFontSettings WithAutoSize(bool autoSize)
        {
            AutoSize = autoSize;
            return this;
        }

        //// post-v1: control alignment
        ///// <summary>
        ///// Reserved for future use. Not yet implemented.
        ///// </summary>
        ///// <param name="alignment">The desired control alignment.</param>
        ///// <returns>Fluent interface to define more settings.</returns>
        //public IAllowControlSettings WithAlignment(ContentAlignment alignment)
        //{
        //    throw new NotImplementedException();
        //}
        /// <summary>
        /// The height of the control, in pixels. 0 [default] lets the control size itself.
        /// </summary>
        /// <returns>The height currently in use.</returns>
        public int Height { get; internal set; } = 0;
        /// <summary>
        /// The width of the control, in pixels. 0 [default] lets the control stretch to its container.
        /// </summary>
        /// <returns>The width currently in use.</returns>
        public int Width { get; internal set; } = 0;
        private string _text;
        /// <summary>
        /// For text-based controls, gets or sets the text.<br/>
        /// Setting stores the text and posts the update without waiting; getting returns the stored text.
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                if (WindowsControl is Control ctrl && true == Alive(ctrl))
                {
                    PostToUi(ctrl, () => ctrl.Text = value);
                }
            }
        }
        /// <summary>
        /// Control name, only used for Information and Exception channel messages.<br/>
        /// Set it with the .WithName fluent interface.
        /// </summary>
        public string Name { get; internal set; }
        /// <summary>
        /// for attaching value-added objects to carry along on form controls
        /// </summary>
        public object Tag { get; internal set; }
        /// <summary>
        /// Underlying Windows form control, Button, Checkbox, Textbox, etc.<br/>
        /// Create form controls through GPAL.
        /// </summary>
        public dynamic WindowsControl
        {
            get => _windowsControl;
            internal set
            {
                _windowsControl = value;

                // Do the work queued before the form was built.
                if (null != value)
                    Realized();
            }
        }

        private dynamic _windowsControl;

        /// <summary>
        /// Called once the form has built this control. A control that takes work before it exists overrides
        /// this and does that work here.
        /// </summary>
        internal virtual void Realized()
        {
            // Text controls write their own field into the control when built, so lines appended earlier already show.
            // Drop the queued copies.
            while (true == pendingLines.TryDequeue(out string _))
            {
            }
        }

        public bool IsReady => WindowsControl != null;

        // Line appending for all text controls; the thread marshalling and no-handle case are shared.
        private readonly ConcurrentQueue<string> pendingLines = new ConcurrentQueue<string>();
        private readonly object appendGate = new object();
        private bool waitingForHandle = false;

        internal void AppendLineCore(string text, int maxLines)
        {
            // Lines are queued here and written on the UI thread. This is called from the publishing thread, a worker
            // while the UI thread is blocked in Run.
            pendingLines.Enqueue(text);

            DrainLines(maxLines);
        }

        // TextBoxBase, the type GPALTextArea and GPALRichTextBox share
        private void DrainLines(int maxLines)
        {
            // A closing form still publishes, and posting to a disposed control makes Windows Forms recreate
            // its handle
            if (!(WindowsControl is TextBoxBase tb) || false == Alive(tb)) return;

            // No handle means created but not yet shown, since the disposed case is handled above. The lines
            // stay queued and HandleCreated drains them
            if (false == tb.IsHandleCreated)
            {
                lock (appendGate)
                {
                    if (false == waitingForHandle)
                    {
                        waitingForHandle = true;
                        tb.HandleCreated += (sender, args) => DrainLines(maxLines);
                    }
                }

                return;
            }

            MethodInvoker drain = () =>
            {
                // Posted while the handle existed and run after it went
                if (false == Alive(tb) || false == tb.IsHandleCreated) return;

                // Write the whole queue in one pass. Reading Lines or rewriting Text at the cap once per line freezes the
                // form for minutes.
                List<string> arrived = new List<string>();

                while (true == pendingLines.TryDequeue(out string line))
                    arrived.Add(line);

                if (0 == arrived.Count) return;

                // a placeholder on screen comes off first, so the lines are not added after it
                if (tb is PlaceholderTextBox showing)
                    showing.HidePlaceholder();

                string[] held = 0 < maxLines ? tb.Lines : new string[0];

                if (0 < maxLines && held.Length + arrived.Count > maxLines)
                {
                    // over the cap, so the tail of what is held plus the batch is written once
                    List<string> keeping = new List<string>(maxLines);

                    int roomForHeld = maxLines - Math.Min(arrived.Count, maxLines);

                    for (int each = Math.Max(0, held.Length - roomForHeld); each < held.Length; each++)
                        keeping.Add(held[each]);

                    for (int each = Math.Max(0, arrived.Count - maxLines); each < arrived.Count; each++)
                        keeping.Add(arrived[each]);

                    tb.Text = string.Join("\r\n", keeping);
                }
                else
                {
                    if (0 < tb.TextLength) tb.AppendText("\r\n");

                    tb.AppendText(string.Join("\r\n", arrived));
                }

                tb.SelectionStart = tb.TextLength;
                tb.ScrollToCaret();

                // AppendText raises no TextChanged, so the copy Text returns is brought up to date here
                _text = tb.Text;
            };
            // Posted rather than invoked. A workflow started from a button holds the UI thread, so an invoke
            // from a worker would block until it finished
            PostToUi(tb, drain);
        }

        internal void ClearLinesCore()
        {
            // Empty the queue too, or queued lines arrive after the clear.
            while (true == pendingLines.TryDequeue(out string dropped)) { }

            _text = string.Empty;

            if (!(WindowsControl is TextBoxBase tb) || false == Alive(tb)) return;

            if (false == tb.IsHandleCreated)
            {
                tb.Text = string.Empty;
                return;
            }

            MethodInvoker clear = () =>
            {
                if (false == Alive(tb) || false == tb.IsHandleCreated) return;

                tb.Clear();
            };

            PostToUi(tb, clear);
        }

        internal void ScrollToEndCore()
        {
            if (!(WindowsControl is TextBoxBase tb) || false == Alive(tb)) return;

            PostToUi(tb, () => tb.ScrollToCaret());
        }

        /// <summary>
        /// True when a control can still be written to.<br/>
        /// Marshalling to a control whose handle is gone makes WinForms try to create one and fail with "error creating
        /// window handle". Nothing to write to means the form is closed, not a failure.<br/>
        /// A handle is not required; a control not yet shown has none, and a workflow fills in saved
        /// values before <c>ShowDialog</c>.
        /// </summary>
        /// <param name="control">The Windows Forms control about to be written to</param>
        /// <returns>True when it can be written to; false when it is gone or closing</returns>
        internal static bool Alive(Control control)
        {
            // Only check that the control exists. A control on an unopened tab has no handle yet; its value is set now
            // and painted when shown. The handle is checked where the marshalling happens.
            return null != control
                && false == control.IsDisposed
                && false == control.Disposing;
        }

        public void Focus()
        {
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
                ctrl.Focus();
        }
        /// <summary>
        /// The control type of WindowsControl. Set when the control is instantiated with GPAL.
        /// </summary>
        public FormControlType ControlType { get; internal set; }
        internal bool ReadOnly { get; set; }
        internal bool IsChecked { get; set; }
        internal bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Whether the control accepts input, before and after the form is built.<br/>
        /// Implemented once for all controls. GPALBarItem overrides it, since a ToolStripItem is not a Control.
        /// </summary>
        /// <returns>True [default] enabled, false disabled</returns>
        public virtual bool Enabled
        {
            get
            {
                bool retVal = IsEnabled;

                if (WindowsControl is Control control && true == Alive(control))
                    retVal = OnUiRead(control, () => control.Enabled, IsEnabled);

                return retVal;
            }
            set
            {
                if (WindowsControl is Control control && true == Alive(control))
                    OnUi(control, () => control.Enabled = value);

                // Store the value either way, so a control disabled before the build starts disabled, and a read after
                // close returns the last value.
                IsEnabled = value;
            }
        }

        /// <summary>
        /// Fluent interface to set the initial state of the control.
        /// </summary>
        /// <param name="enabled">True [default]<br/>False</param>
        /// <returns>Fluent interface to define more settings</returns>
        public virtual IAllowControlSettings WithEnabled(bool enabled = true)
        {
            Enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets whether the user can change the control's value. Controls with nothing to edit store and ignore it.
        /// </summary>
        /// <param name="readOnly">True [default]<br/>False</param>
        /// <returns>Fluent interface to define more settings</returns>
        public virtual IAllowControlSettings WithReadOnly(bool readOnly = true)
        {
            IsReadOnly = readOnly;
            return this;
        }

        /// <summary>
        /// The value set by <see cref="WithReadOnly"/>, read when the control is built.
        /// </summary>
        internal bool IsReadOnly { get; set; }
        internal string ToolTipText { get; private set; }
        internal int ToolTipAutoPopDelayMs { get; private set; } = 5000;   // default 5 seconds
        internal System.Windows.Forms.Keys ShortCutKey { get; set; }
        internal List<(ControlEventType EventType, Delegate Handler)> Callbacks { get; } = new List<(ControlEventType EventType, Delegate Handler)>();

        /// <summary>
        /// Sets an eventhandler to attach to the Windows form control event.<br/>
        /// See the GPAL controls for which event that is.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALButton gPalButton3 = (GPALButton)GPAL.Button
        ///         .WithName("Exit")
        ///         .WithText("Exit")
        ///         .WithCallback&lt;EventHandler&gt;(Button3ClickHandler)
        ///         .ForEvent(ControlEventType.Click)   // Option: if not specified, adds handler to 'default' event single action for control
        ///         .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);
        /// </code>
        /// </example>
        /// <param name="handler">Your eventhandler specific for the control/event type</param>
        /// <returns>Fluent interface to define your control</returns>
        public IAllowForEventControlSettings WithCallback<TDelegate>(TDelegate handler)
            where TDelegate : Delegate
        {
            Callbacks.Add((ControlEventType.Default, WrapHandler(handler, RunOnUiThread) ?? (Delegate)handler));

            return this;
        }

        /// <summary>
        /// How long a handler can hold the UI thread before GPAL warns. Two seconds is past a visible freeze and far
        /// below any browser work.
        /// </summary>
        private const double UiHeldWarningSeconds = 2.0;

        /// <summary>
        /// Nesting depth of this control's callback. A change event raised by the callback writing to its own control
        /// arrives while it is still running; that is the loop to warn about.
        /// </summary>
        private int _callbackDepth;

        /// <summary>
        /// Number of this control's handlers waiting to run, to warn when the queue grows faster than it drains,
        /// usually a handler writing to its own control.
        /// </summary>
        private int _callbackPending;

        /// <summary>
        /// Waiting handlers allowed before GPAL warns: a few quick clicks are fine, a self-triggering handler is caught
        /// early.
        /// </summary>
        private const int CallbackBacklogWarning = 5;

        /// <summary>
        /// The last queued background handler for this control. Each new one chains after it, so they run one at a time
        /// in event order.
        /// </summary>
        private System.Threading.Tasks.Task _callbackChain = System.Threading.Tasks.Task.CompletedTask;

        /// <summary>
        /// Lock while the last handler is read and replaced, so two events cannot chain onto the same handler.
        /// </summary>
        private readonly object _callbackChainGate = new object();

        /// <summary>
        /// This control's name for messages; the type name until WithName is called.
        /// </summary>
        private string CallbackOwner => true == string.IsNullOrEmpty(Name) ? GetType().Name : Name;

        /// <summary>
        /// Marks this control's callback as running and warns if it already was: a handler writing to its own control
        /// re-raises the change event.
        /// </summary>
        private void EnterCallback()
        {
            if (1 < System.Threading.Interlocked.Increment(ref _callbackDepth))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The callback for [{CallbackOwner}] was entered while it was still running, which is what a handler writing to its own control does", this, GPALObjectType.Other);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private delegate bool EnumThreadWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsProc callback, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // True while a native dialog window (class #32770: file dialogs, MessageBox) is showing on the thread
        private static bool DialogOpenOnThread(uint threadId)
        {
            bool found = false;
            StringBuilder className = new StringBuilder(32);

            EnumThreadWindows(threadId, (hWnd, lParam) =>
            {
                className.Clear();
                GetClassName(hWnd, className, className.Capacity);
                found = "#32770" == className.ToString() && true == IsWindowVisible(hWnd);

                // stop at the first one
                return false == found;
            }, IntPtr.Zero);

            return found;
        }

        /// <summary>
        /// Runs a handler on the UI thread, warning about a handler triggered by its own write or one that freezes the
        /// window.
        /// </summary>
        /// <param name="call">The handler, with the sender and arguments it was raised with.</param>
        private void RunOnUiThread(Action call)
        {
            System.Diagnostics.Stopwatch held = System.Diagnostics.Stopwatch.StartNew();

            // A handler that opens a modal dialog (a file dialog, a MessageBox, ShowDialog) holds the window for as long
            // as the person takes, and has to run on this thread, so that time is not a frozen window. WinForms raises
            // EnterThreadModal for its own forms' ShowDialog. File dialogs and MessageBox are native Windows dialogs that
            // raise nothing, so a timer also looks for a dialog window (class #32770) open on this thread.
            bool showedDialog = false;
            EventHandler dialogOpened = (sender, args) => showedDialog = true;
            uint uiThread = GetCurrentThreadId();

            System.Windows.Forms.Application.EnterThreadModal += dialogOpened;

            using (System.Threading.Timer dialogWatch = new System.Threading.Timer(state =>
            {
                if (false == showedDialog && true == DialogOpenOnThread(uiThread))
                    showedDialog = true;
            }, null, 250, 250))
            {
                EnterCallback();

                try
                {
                    call();
                }
                finally
                {
                    // Decrement the depth even when the handler threw, or the next click is reported as a loop.
                    System.Threading.Interlocked.Decrement(ref _callbackDepth);

                    System.Windows.Forms.Application.EnterThreadModal -= dialogOpened;
                }
            }

            if (false == showedDialog && UiHeldWarningSeconds < held.Elapsed.TotalSeconds)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The handler for [{CallbackOwner}] held the window for [{held.Elapsed.TotalSeconds:0.0}] seconds. Attach it with WithCallbackOffUIThread and it runs on a thread of its own", this, GPALObjectType.Other);
        }

        /// <summary>
        /// Attaches a handler that runs off the UI thread, so the form keeps painting while it works.
        /// <br/><br/>
        /// A handler attached with <see cref="WithCallback{TDelegate}(TDelegate)"/> runs on the UI thread,
        /// which is right for anything quick. Work that drives a browser is not quick: the form freezes for
        /// the whole run and every control it updates on the way arrives at once when it finishes, so a
        /// progress bar goes from empty to full in one step and shows nothing in between.
        /// <br/><br/>
        /// GPAL controls are safe to set from the handler: every one of them marshals back to the UI thread on
        /// its own. Raw WinForms controls are not, so a handler that reaches past GPAL to one of those should
        /// stay on <see cref="WithCallback{TDelegate}(TDelegate)"/>.
        /// <br/><br/>
        /// Handlers attached this way run one at a time, in the order the events arrived. An event that fires
        /// on every keystroke or every click of a spinner therefore gets a handler per event and not a thread
        /// per event. A button is the exception: a click that arrives while its handler is still running or
        /// waiting to run is dropped, so pressing it twice runs its workflow once.
        /// <br/><br/>
        /// A <see cref="DragEventHandler"/> and a <see cref="KeyEventHandler"/> stay on the UI thread whatever
        /// this is called with, because the event reads args.Effect and args.Handled the moment the handler
        /// returns and a handler running later would set them after they were wanted. GPAL publishes a WARNING
        /// naming the control when it does that.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALButton runBtn = (GPALButton)GPAL.Button
        ///         .WithName("run")
        ///         .WithText("Run Workflows")
        ///         .WithCallbackOffUIThread&lt;EventHandler&gt;(OnRun)
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        /// <typeparam name="TDelegate">The handler type the control's event takes.</typeparam>
        /// <param name="handler">Your eventhandler specific for the control/event type</param>
        /// <returns>Fluent interface to define your control</returns>
        public IAllowForEventControlSettings WithCallbackOffUIThread<TDelegate>(TDelegate handler)
            where TDelegate : Delegate
        {
            // A drag handler sets args.Effect and a key handler args.Handled, read as soon as the handler returns. On
            // another thread they would be set too late.
            bool answersAsItReturns = handler is DragEventHandler || handler is KeyEventHandler;

            // a button drops a click that arrives while its handler is still running or waiting to run
            Action<Action> runner = this is GPALButton ? DropWhileBusy() : (Action<Action>)QueueInBackground;

            Delegate wrapped = true == answersAsItReturns ? null : WrapHandler(handler, runner);

            if (null == wrapped)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, true == answersAsItReturns
                    ? $"The event reads its answer from [{typeof(TDelegate).Name}] as soon as the handler returns, so the handler for [{CallbackOwner}] stays on the UI thread"
                    : $"[{typeof(TDelegate).Name}] is not a handler type GPAL knows, so the handler for [{CallbackOwner}] stays on the UI thread", this, GPALObjectType.Other);

                wrapped = WrapHandler(handler, RunOnUiThread) ?? (Delegate)handler;
            }

            Callbacks.Add((ControlEventType.Default, wrapped));

            return this;
        }

        /// <summary>
        /// Makes a runner for one background handler that queues an event only while that handler is neither
        /// running nor waiting, and drops the event otherwise.
        /// </summary>
        /// <returns>The runner, holding its own busy flag</returns>
        private Action<Action> DropWhileBusy()
        {
            int[] busy = new int[1];

            Action<Action> retVal = call =>
            {
                if (0 != System.Threading.Interlocked.CompareExchange(ref busy[0], 1, 0))
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"An event on [{CallbackOwner}] arrived while its handler was running or waiting to run, so it was dropped", this, GPALObjectType.Other);
                else
                    QueueInBackground(() =>
                    {
                        try
                        {
                            call();
                        }
                        finally
                        {
                            System.Threading.Interlocked.Exchange(ref busy[0], 0);
                        }
                    });
            };

            return retVal;
        }

        /// <summary>
        /// Runs one background handler from the control's queue and reports any failure.
        /// </summary>
        /// <param name="call">The handler, with the sender and arguments it was raised with.</param>
        private void RunInBackground(Action call)
        {
            EnterCallback();

            try
            {
                call();
            }
            catch (Exception ex)
            {
                // Nothing on this thread catches the exception, and a silent failure looks like a button that did
                // nothing.
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"A background handler for [{CallbackOwner}] ended in an exception", this, GPALObjectType.Other, ex);
            }
            finally
            {
                System.Threading.Interlocked.Decrement(ref _callbackDepth);
                System.Threading.Interlocked.Decrement(ref _callbackPending);
            }
        }

        /// <summary>
        /// Wraps a handler to run inside <paramref name="runner"/>, returning a delegate of the same type so the form
        /// can cast it to its event's type.<br/>
        /// Any handler type not listed returns null, and the caller attaches it unwrapped.
        /// </summary>
        /// <param name="handler">The handler the workflow attached.</param>
        /// <param name="runner">What to run it inside, either the UI thread runner or the background queue.</param>
        /// <returns>A delegate of the same type, or null for a type GPAL has no wrapper for</returns>
        private Delegate WrapHandler<TDelegate>(TDelegate handler, Action<Action> runner)
            where TDelegate : Delegate
        {
            Delegate retVal;

            switch (handler)
            {
                case EventHandler typed:
                    retVal = (EventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case KeyEventHandler typed:
                    retVal = (KeyEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case DragEventHandler typed:
                    retVal = (DragEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case ScrollEventHandler typed:
                    retVal = (ScrollEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case SplitterEventHandler typed:
                    retVal = (SplitterEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case ItemCheckedEventHandler typed:
                    retVal = (ItemCheckedEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case TreeViewEventHandler typed:
                    retVal = (TreeViewEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                case DataGridViewCellEventHandler typed:
                    retVal = (DataGridViewCellEventHandler)((sender, args) => runner(() => typed(sender, args)));
                    break;

                default:
                    retVal = null;
                    break;
            }

            return retVal;
        }

        /// <summary>
        /// Puts a handler on the end of this control's queue so it runs on a thread of its own, after whatever
        /// is already waiting. One runs at a time however fast the events arrive.
        /// </summary>
        /// <param name="call">The handler, already holding the sender and the arguments it was raised with.</param>
        private void QueueInBackground(Action call)
        {
            if (CallbackBacklogWarning == System.Threading.Interlocked.Increment(ref _callbackPending))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{CallbackBacklogWarning}] handlers for [{CallbackOwner}] are waiting their turn, so the events are arriving faster than the handler runs", this, GPALObjectType.Other);

            lock (_callbackChainGate)
                _callbackChain = _callbackChain.ContinueWith(previous => RunInBackground(call));
        }

        /// <summary>
        /// Specifies which event the most recently added <see cref="WithCallback{TDelegate}(TDelegate)"/>
        /// handler should be attached to, replacing its default ("Default") event type. Optional - if not
        /// called, the handler is attached to the control's default event.
        /// </summary>
        /// <param name="eventType">The event type to attach the most recently added callback to.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettings ForEvent(ControlEventType eventType)
        {
            // ForEvent applies to the callback added just before it: replace that "Default" entry with the event.
            if (Callbacks.Count > 0)
            {
                var last = Callbacks[Callbacks.Count - 1];
                if (last.EventType == ControlEventType.Default)
                {
                    Callbacks[Callbacks.Count - 1] = (eventType, last.Handler);
                    return this;
                }
            }

            // No Default entry before it: add the event with no handler
            Callbacks.Add((eventType, null)); // null handler = ignore later
            return this;
        }
        /// <summary>
        /// Sets the tooltip text shown when hovering over the control.
        /// </summary>
        /// <param name="toolTipText">The tooltip text to display.</param>
        /// <returns>Fluent interface to optionally define the tooltip auto-pop delay or further settings.</returns>
        public IAllowToolTipDelay WithToolTip(string toolTipText)
        {
            ToolTipText = toolTipText ?? "";
            return this;
        }

        /// <summary>
        /// Sets how long the tooltip set via <see cref="WithToolTip(string)"/> remains visible.
        /// </summary>
        /// <param name="delayInMs">The auto-pop delay in milliseconds. Enforced to a minimum of 1000ms.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettings WithToolTipDelay(int delayInMs)
        {
            ToolTipAutoPopDelayMs = Math.Max(1000, delayInMs); // enforce reasonable minimum
            return this;
        }

        /// <summary>
        /// Set the keyboard shortcut that will activate this controls eventhandler defined with .WithCallback
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALButton gPalButton3 = (GPALButton)GPAL.Button
        ///         .WithName("Exit")
        ///         .WithText("Exit")
        ///         .WithCallback&lt;EventHandler&gt;(Button3ClickHandler)
        ///         .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);
        /// </code>
        /// </example>
        /// <param name="shortCutKey">The shortcut key</param>
        /// <returns>Fluent interface to define your control</returns>
        public IAllowControlSettings WithShortcutKey(System.Windows.Forms.Keys shortCutKey)
        {
            ShortCutKey = shortCutKey;
            return this;
        }

        /// <summary>
        /// Runs work against a control on the UI thread, from any thread.
        /// <br/><br/>
        /// A form closing in between is not worth ending a workflow over.
        /// </summary>
        /// <param name="control">The control to work against.</param>
        /// <param name="work">What to do to it.</param>
        internal static void OnUi(Control control, MethodInvoker work)
        {
            if (false == Alive(control))
                return;

            try
            {
                // No handle means no thread to marshal to; do the work here. WinForms keeps the value until the control
                // is created.
                if (true == control.IsHandleCreated && true == control.InvokeRequired)
                    control.Invoke(work);
                else
                    work();
            }
            catch (ObjectDisposedException)
            {
                // the form went between the check and the work
            }
            catch (InvalidOperationException)
            {
                // The handle was destroyed too; Invoke reports it with this exception.
            }
        }

        /// <summary>
        /// Runs the work on the UI thread when there is a control to marshal through, otherwise here. A tree node or a
        /// toolstrip item not yet on a strip still needs setting.
        /// </summary>
        /// <param name="control">The control to marshal through, which may be null or already closed.</param>
        /// <param name="work">The work to do either way.</param>
        internal static void OnUiOrHere(Control control, MethodInvoker work)
        {
            if (true == Alive(control))
                OnUi(control, work);
            else
                work();
        }

        /// <summary>
        /// Hands the work to the UI thread without waiting for it to run. Use this where waiting would
        /// deadlock: a workflow raised from a button owns the UI thread for its whole run, so anything it
        /// publishes from a worker would wait on a thread that is waiting on the worker.
        /// </summary>
        /// <param name="control">The control to work against.</param>
        /// <param name="work">What to do to it, once the UI thread is free.</param>
        internal static void PostToUi(Control control, MethodInvoker work)
        {
            if (false == Alive(control))
                return;

            try
            {
                if (true == control.IsHandleCreated && true == control.InvokeRequired)
                    control.BeginInvoke(work);
                else
                    work();
            }
            catch (ObjectDisposedException)
            {
                // the form went between the check and the work
            }
            catch (InvalidOperationException)
            {
                // The handle was destroyed too; BeginInvoke reports it with this exception.
            }
        }

        /// <summary>
        /// Reads one value from a control on the UI thread, from any thread. If the form has closed, returns
        /// <paramref name="whenGone"/>, the last value set.
        /// </summary>
        /// <typeparam name="T">The type of the value being read.</typeparam>
        /// <param name="control">The control to read from.</param>
        /// <param name="read">What to read off it.</param>
        /// <param name="whenGone">What to return when the control is no longer there to ask.</param>
        /// <returns>The value read, or <paramref name="whenGone"/>.</returns>
        internal static T OnUiRead<T>(Control control, Func<T> read, T whenGone)
        {
            T retVal = whenGone;

            if (true == Alive(control))
                try
                {
                    retVal = true == control.IsHandleCreated && true == control.InvokeRequired
                        ? (T)control.Invoke(read)
                        : read();
                }
                catch (ObjectDisposedException)
                {
                    // the form went between the check and the read
                }
                catch (InvalidOperationException)
                {
                    // The handle was destroyed too; Invoke reports it with this exception.
                }

            return retVal;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread. If the calling thread already owns
        /// the UI, the action runs inline; otherwise it is marshalled via Invoke. Use this when
        /// you need to perform UI operations that GPAL does not expose natively.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        public void OnUiThread(Action action)
        {
            OnUiOrHere(WindowsControl as Control, () => action());
        }

        /// <summary>
        /// Sets the name for the control. Used in Information and Exception channel messages.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALButton gPalButton3 = (GPALButton)GPAL.Button
        ///         .WithName("Exit")
        ///         .WithText("Exit")
        ///         .WithCallback&lt;EventHandler&gt;(Button3ClickHandler)
        ///         .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);
        /// </code>
        /// </example>
        /// <param name="name">The name of your control</param>
        /// <returns>Fluent interface to define your control</returns>
        public IAllowControlSettings WithName(string name)
        {
            this.Name = name;
            return this;
        }
        /// <summary>
        /// Set the text of the control.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALButton gPalButton3 = (GPALButton)GPAL.Button
        ///         .WithName("Exit")
        ///         .WithText("Exit")
        ///         .WithTag(someObject)
        ///         .WithCallback&lt;EventHandler&gt;(Button3ClickHandler) // can then use tag to get extra values for operation upon click
        ///         .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);
        /// </code>
        /// </example>
        /// <param name="tag">Text to display in the control.</param>
        /// <returns>Fluent interface to define your control</returns>
        public IAllowControlSettings WithTag(object tag)
        {
            // Tag is on Control, so every built control has one.
            if (WindowsControl is Control control && true == Alive(control))
                OnUi(control, () => control.Tag = tag);

            this.Tag = tag;

            return this;
        }
        /// <summary>
        /// Sets the height of the control, in pixels.
        /// </summary>
        /// <param name="height">The desired height, in pixels.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettings WithHeight(int height)
        {
            this.Height = height;
            return this;
        }
        /// <summary>
        /// Sets the width of the control, in pixels.<br/>
        /// A control with no width stretches across its container, which is what a form full of full-width
        /// buttons looks like. Give one a width and it keeps that width and sits centered instead.
        /// </summary>
        /// <param name="width">The desired width, in pixels.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettings WithWidth(int width)
        {
            this.Width = width;
            return this;
        }
        /// <summary>
        /// Set the text of the control.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALButton gPalButton3 = (GPALButton)GPAL.Button
        ///         .WithName("Exit")
        ///         .WithText("Exit")
        ///         .WithCallback&lt;EventHandler&gt;(Button3ClickHandler)
        ///         .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);
        /// </code>
        /// </example>
        /// <param name="text">Text to display in the control.</param>
        /// <returns>Fluent interface to define your control</returns>
        public IAllowControlSettingsAndFontSettings WithText(string text)
        {
            if (null != WindowsControl)
                ((TextBox)WindowsControl).Text = text;      // this.Text will be updated via the TextChanged event;
            else
                this.Text = text;
            return this;
        }
        /// <summary>
        /// Return a typeof(GPALControl) so that controls do not have to be cast.
        /// </summary>
        /// <returns>A GPALForm control object, whatever control was created via <b>GPAL.</b></returns>
        /// <example>
        /// <code>
        /// static GPALTextArea gPALTextArea = GPAL.TextArea
        ///     .WithText("Type something here")
        ///     .WithCallback&lt;EventHandler&gt;(InputHandler)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.T)
        ///     .ToGPALObject();
        ///      
        /// static GPALLabel gPALLabel = GPAL.Label
        ///     .WithText("Form Tester")
        ///     .ToGPALObject();
        ///     
        /// static GPALCheckbox gPALCheckbox1 = GPAL.Checkbox
        ///     .WithChecked(true)
        ///     .WithText("Wait on document.ready")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        public dynamic ToGPALObject()
        {
            return this;
        }
    }
}

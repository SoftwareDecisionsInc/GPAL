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
    /// Unused placeholder class.
    /// </summary>
    class GPALControls
    {
    }
    #region <GPAL Controls>
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
        /// <b>NOTE:</b> Use .WithName fluent interface to set the control name.
        /// </summary>
        public string Name { get; internal set; }
        /// <summary>
        /// for attaching value-added objects to carry along on form controls
        /// </summary>
        public object Tag { get; internal set; }
        /// <summary>
        /// Underlying Windows form control, Button, Checkbox, Textbox, etc.<br/>
        /// <b>NOTE:</b> Use GPAL to instantiate form controls.
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

                // Write the whole queue in one pass. Reading Lines or rewriting Text at the cap once per line froze the
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
        /// <b>NOTE:</b> a handle is not required; a control not yet shown has none, and a workflow fills in saved
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
            // replace the last "Default" entry with the explicit event
            // (this is the only "magic" — keeps chain order natural)
            if (Callbacks.Count > 0)
            {
                var last = Callbacks[Callbacks.Count - 1];
                if (last.EventType == ControlEventType.Default)
                {
                    Callbacks[Callbacks.Count - 1] = (eventType, last.Handler);
                    return this;
                }
            }

            // if no prior Default > just add as explicit (rare misuse case)
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
    /// <summary>
    /// A GPAL button instantiated with GPAL.Button for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALButton.WindowsControl.Clicked event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
    ///     .Default
    ///     .WithName("button1")
    ///     .WithText("Exit")
    ///     .WithCallback&lt;EventHandler&gt;(ExitButtonClickHandler)
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);  // Alt-E
    ///
    /// static void Main(string[] args)
    /// {
    ///     GPALForm myForm = (GPALForm)GPAL.Form
    ///         .WithTitle("Simple Form Test")
    ///         .WithFormControl(gPalButton1)
    ///         .ShowDialog();
    /// }
    ///     
    /// private static void ExitButtonClickHandler(object sender, EventArgs e)
    /// {
    ///     Control control = (Control)sender;
    ///     
    ///     while (null != control.Parent)
    ///         control = control.Parent;
    ///     control.Hide();
    /// }
    /// </code>
    /// </example>
    public class GPALButton : GPALControl, IAllowControlSettingsAndDefault
    {
        /// <summary>
        /// Is this button the default Accept button for the form?<br/>
        /// <b>NOTE:</b> Do NOT define more than one default. Only the last one added to the form is the default.
        /// </summary>
        /// <returns>Your designation for the button, true or false.</returns>
        internal bool IsDefault { get; set; }
        internal GPALButton()
        {
            ControlType = FormControlType.Button;
            IsDefault = false;
        }
        /// <summary>
        /// Fluent interface to designate this button as the default button.<br/>
        /// <b>NOTE:</b> Do NOT define more than one default. Only the last one added to the form is the default.
        /// </summary>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndDefault Default
        {
            get
            {
                IsDefault = true;
                return this;
            }
        }
    }
    /// <summary>
    /// A GPAL checkbox instantiateds with GPAL,Checkbox for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALCheckbox.WindowsControl.CheckChanged event.<br/>
    /// Default is not checked
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALCheckbox gPALCheckbox1 = (GPALCheckbox)GPAL.Checkbox
    ///     .WithChecked(true)
    ///     .WithText("Overwrite File")
    ///     .WithCallback&lt;EventHandler&gt;(CheckboxHandler); // on check changed event handler
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPALCheckbox1)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALCheckbox : GPALControl, IAllowControlSettingsAndChecked
    {
        internal GPALCheckbox()
        {
            ControlType = FormControlType.Checkbox;
        }
        /// <summary>
        /// Sets whether the checkbox is checked.
        /// </summary>
        /// <param name="isChecked">True<br/>False [default]</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndChecked WithChecked(bool isChecked)
        {
            this.Checked = isChecked;
            return this;
        }
        /// <summary>
        /// Runtime access to un/check the checkbox. Setting this writes through to the live control
        /// (if realized), marshalling to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        /// <returns>True [checked]<br/>False [default - unchecked]</returns>
        public bool Checked
        {
            get
            {
                bool retVal = IsChecked;

                if (WindowsControl is CheckBox cb && true == Alive(cb))
                    retVal = OnUiRead(cb, () => cb.Checked, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is CheckBox cb && true == Alive(cb))
                {
                    OnUi(cb, () => cb.Checked = value);
                }
                else
                {
                    IsChecked = value;
                }
            }
        }
    }
    /// <summary>
    /// A grid control instantiated with GPAL.DataGridView for use on GPAL forms, displaying the contents
    /// of an <see cref="IGPALGrid{T}"/> as rows and columns.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALDataGridView gPalDataGridView = (GPALDataGridView)GPAL.DataGridView
    ///     .WithColumns("Name", "Status")
    ///     .WithInput(myGrid)
    ///     .ToGPALObject();
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalDataGridView)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALDataGridView : GPALControl, IAllowDataGridViewControlSettings
    {
        private IGPALGrid<string> _grid;
        private readonly List<string> _columnNames = new List<string>();
        private bool _autoGenerateColumns = false;  // default false – only generate if explicitly asked or no columns set

        internal GPALDataGridView()
        {
            ControlType = FormControlType.DataGridView;
        }

        /// <summary>
        /// Sets the column header names for the grid. If not specified and
        /// <see cref="WithAutoGenerateColumns(bool)"/> is true, columns are auto-named "Column 1", "Column 2", etc.
        /// </summary>
        /// <param name="columnNames">The column header names, in order.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithColumns(params string[] columnNames)
        {
            if (columnNames != null)
            {
                _columnNames.AddRange(columnNames);
            }
            return this;
        }

        /// <summary>
        /// Sets whether the grid's cells are read-only.
        /// </summary>
        /// <param name="readOnly"><c>true</c> to make cells read-only; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public new IAllowDataGridViewControlSettings WithReadOnly(bool readOnly = true)
        {
            base.ReadOnly = readOnly;
            return this;
        }

        /// <summary>
        /// Sets whether columns should be auto-generated (named "Column 1", "Column 2", etc.) when no
        /// column names were provided via <see cref="WithColumns(string[])"/>.
        /// </summary>
        /// <param name="autoGenerate"><c>true</c> to auto-generate columns; otherwise <c>false</c> [default].</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithAutoGenerateColumns(bool autoGenerate)
        {
            _autoGenerateColumns = autoGenerate;
            return this;
        }

        /// <summary>
        /// Sets the data grid to display and immediately syncs it to the underlying Windows control via
        /// <see cref="SyncGridToControl"/>.
        /// </summary>
        /// <param name="grid">The grid data to display.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(IGPALGrid<string> grid)
        {
            this.CurrentGrid = grid;
            return this;
        }

        /// <summary>
        /// Rebuilds the underlying <see cref="DataGridView"/>'s columns and rows from <see cref="CurrentGrid"/>.
        /// Columns come from <see cref="ColumnNames"/> if set, otherwise auto-generated as "Column N" if
        /// <see cref="AutoGenerateColumns"/> is true. Rows are populated from each row of the grid, and
        /// columns/rows are auto-resized to fit. If the underlying control requires a thread-safe invoke,
        /// this re-invokes itself on the UI thread. No-op if the underlying control is not a
        /// <see cref="DataGridView"/> or no grid has been set.
        /// </summary>


        // Column widths in column order. Empty leaves the control's defaults.
        internal readonly List<int> _columnWidths = new List<int>();

        /// <summary>
        /// Sets column widths in pixels, in column order.
        /// </summary>
        /// <remarks>
        /// Fewer widths than columns leaves the rest as the grid sizes them. A width of zero or less is ignored. With a
        /// width for every column, the last one fills to the edge.
        /// </remarks>
        /// <param name="widths">The widths, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithColumnWidth(params int[] widths)
        {
            _columnWidths.Clear();

            if (null != widths)
                _columnWidths.AddRange(widths);

            ApplyColumnWidths();

            return this;
        }


        // Which columns wrap, in column order. Empty means none, the grid's default.
        internal readonly List<bool> _columnWrap = new List<bool>();

        /// <summary>
        /// Sets which columns wrap their text, in column order.<br/>
        /// No arguments wraps every column; fewer entries than columns leaves the rest unwrapped.
        /// </summary>
        /// <example>
        /// <code>
        ///     .WithWrap(false, true, false)   // the middle column wraps
        ///     .WithWrap()                     // all of them wrap
        /// </code>
        /// </example>
        /// <param name="wrap">True for each column that wraps, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithWrap(params bool[] wrap)
        {
            _columnWrap.Clear();
            wrapAsked = true;

            if (null != wrap)
                _columnWrap.AddRange(wrap);

            ApplyWrap();

            return this;
        }

        /// <summary>
        /// Applies the wrap setting and lets rows grow to fit. Applied before and after the form is built, as the
        /// widths are.
        /// </summary>
        internal void ApplyWrap()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv))
                return;

            if (true == dgv.InvokeRequired)
            {
                OnUi(dgv, ApplyWrap);
                return;
            }

            // No WithWrap call: nothing wraps. A call with no entries: everything wraps.
            bool everyColumn = 0 == _columnWrap.Count && true == wrapAsked;

            for (int column = 0; column < dgv.Columns.Count; column++)
            {
                bool wraps = true == everyColumn
                          || (column < _columnWrap.Count && true == _columnWrap[column]);

                dgv.Columns[column].DefaultCellStyle.WrapMode = true == wraps
                                                              ? DataGridViewTriState.True
                                                              : DataGridViewTriState.False;
            }

            dgv.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);
        }

        // Whether WithWrap was called; no entries means every column wraps.
        private bool wrapAsked = false;

        // what share of the width each column takes. Empty leaves the columns as the control makes them
        internal readonly List<double> _columnShares = new List<double>();

        /// <summary>
        /// Sets each column's share of the width, in column order.
        /// </summary>
        /// <remarks>
        /// Shares need not add up to anything and reflow as the form resizes. Integer arguments pick the pixel
        /// overload.
        /// </remarks>
        /// <example>
        /// <code>
        ///     .WithColumnWidth(0.5, 0.2, 0.3)   // shares
        ///     .WithColumnWidth(200, 80, 120)    // pixels
        /// </code>
        /// </example>
        /// <param name="shares">The shares, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithColumnWidth(params double[] shares)
        {
            _columnShares.Clear();

            if (null != shares)
                _columnShares.AddRange(shares);

            ApplyColumnWidths();

            return this;
        }

        // Apply before the form is built and again after; an existing control will not be rebuilt.
        internal void ApplyColumnWidths()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv))
                return;

            // Apply shares first; they set the whole control's sizing mode. A later pixel width holds until the next
            // resize.
            if (0 < _columnShares.Count)
            {
                dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

                for (int column = 0; column < _columnShares.Count && column < dgv.Columns.Count; column++)
                    dgv.Columns[column].FillWeight = (float)(0 < _columnShares[column] ? _columnShares[column] : 1);
            }

            if (0 == _columnWidths.Count)
                return;

            if (true == dgv.InvokeRequired)
            {
                OnUi(dgv, ApplyColumnWidths);
                return;
            }

            for (int column = 0; column < _columnWidths.Count && column < dgv.Columns.Count; column++)
                if (0 < _columnWidths[column])
                    dgv.Columns[column].Width = _columnWidths[column];

            FillLastColumn();
        }

        // True when every column has a pixel width; only then is the last column stretched to the edge. Partial widths
        // leave the rest to the control. Shares already reach the edge through Fill.
        internal bool LastColumnFills => 0 < _columnWidths.Count && _columnWidths.Count == (WindowsControl as DataGridView)?.Columns.Count && 0 == _columnShares.Count;

        // Setting a column width raises the resize event this runs from.
        private bool filling = false;

        /// <summary>
        /// Widens the last column to the control's edge.<br/>
        /// Never narrows: the requested width is the minimum, so a too-narrow control gets a scrollbar. The width is
        /// unknown until the handle exists, so this runs on every resize.
        /// </summary>
        internal void FillLastColumn()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv) || false == LastColumnFills)
                return;

            if (true == dgv.InvokeRequired)
            {
                OnUi(dgv, FillLastColumn);
                return;
            }

            int last = dgv.Columns.Count - 1;

            if (0 > last || true == filling)
                return;

            filling = true;

            try
            {
                // A Fill column ignores Width, so filling only applies while the grid sizes by pixels.
                dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

                if (last < _columnWidths.Count && 0 < _columnWidths[last])
                    dgv.Columns[last].Width = _columnWidths[last];

                int used = 0;

                for (int before = 0; before < last; before++)
                    used += dgv.Columns[before].Width;

                // The grid's scrollbars are inside ClientSize, unlike a list view's. Subtract the vertical one, or a
                // scrolling grid's last column is too wide and adds a horizontal scrollbar.
                int verticalBar = 0;

                foreach (Control child in dgv.Controls)
                    if (child is VScrollBar bar && true == bar.Visible)
                        verticalBar = bar.Width;

                // The row header takes width but is not a column.
                int left = dgv.ClientSize.Width - used - (true == dgv.RowHeadersVisible ? dgv.RowHeadersWidth : 0) - verticalBar - GPALListView.FillMargin;

                if (left > dgv.Columns[last].Width)
                    dgv.Columns[last].Width = left;
            }
            finally
            {
                filling = false;
            }
        }

        /// <summary>
        /// Sets the rows from a DataTable; its column names become the grid's column names.
        /// </summary>
        /// <param name="dataTable">The table to show</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(DataTable dataTable)
        {
            IGPALGrid<string> rows = DatabaseHelper.DataTableToGrid(dataTable, out List<DatabaseSettings.DatabaseColumn> columns);

            // Set the column names first; a column with no name shows as its number.
            if (0 == _columnNames.Count)
                foreach (DatabaseSettings.DatabaseColumn column in columns)
                    _columnNames.Add(column.ColumnName);

            return WithInput(rows);
        }

        /// <summary>
        /// Sets the rows by running the database's query once, here.
        /// </summary>
        /// <remarks>
        /// The query runs once, when this is called; no connection stays open.
        /// </remarks>
        /// <example>
        /// <code>
        ///     GPAL.DataGridView
        ///         .WithInput(GPAL.Database
        ///             .WithServerName(@"HORNET2\DEADMIKE")
        ///             .WithDatabaseName("GPAL")
        ///             .WithDatabaseType(DatabaseType.SQLServer)
        ///             .WithTableName("Orders")
        ///             .ToGPALObject())
        ///         .WithName("orders")
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        /// <param name="database">The database whose query fills the grid</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(IGPALDatabase database)
        {
            DataTable table = null;

            if (null != database)
                database.SaveTo(out table);

            return WithInput(table);
        }

        /// <summary>
        /// Sets the rows from a file, split by its extension or settings, so a tab-delimited file and a spreadsheet
        /// both load.
        /// </summary>
        /// <param name="file">The file to read</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(GPALFile file)
        {
            IGPALGrid<string> rows = GPAL.Grid.ToGPALObject();

            if (null != file)
            {
                FileHelper.GetGrid(((IGPALFileInternal)file).FileSettings, out List<IGPALGrid<string>> tokens);

                // A file with several grids is shown one grid after another, in file order.
                if (null != tokens)
                    foreach (IGPALGrid<string> token in tokens)
                        foreach (List<string> row in token)
                            rows.AddRow(new List<string>(row));
            }

            return WithInput(rows);
        }

        // The range GetGrid reads, counted from one. An unset end means to the edge of the control.
        private int _startRow = 0;
        private int _startColumn = 0;
        private int _endRow = 0;
        private int _endColumn = 0;

        /// <summary>
        /// The first row a range covers, counting from one.
        /// </summary>
        /// <param name="row">The row to start at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithStartRow(int row)
        {
            _startRow = row;
            return this;
        }

        /// <summary>
        /// The first column a range covers, counting from one.
        /// </summary>
        /// <param name="column">The column to start at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithStartColumn(int column)
        {
            _startColumn = column;
            return this;
        }

        /// <summary>
        /// The last row a range covers, counting from one.
        /// </summary>
        /// <param name="row">The row to stop at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithEndRow(int row)
        {
            _endRow = row;
            return this;
        }

        /// <summary>
        /// The last column a range covers, counting from one.
        /// </summary>
        /// <param name="column">The column to stop at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithEndColumn(int column)
        {
            _endColumn = column;
            return this;
        }

        /// <summary>
        /// Reads what the grid is showing, including headers and any user edits or sorting.
        /// </summary>
        /// <remarks>
        /// Reads the live control, including user edits and sorting; before the form is built it returns the grid it
        /// was given. Start and end count from one.
        /// </remarks>
        /// <param name="grid">Receives what was read</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings GetGrid(out IGPALGrid<string> grid)
        {
            IGPALGrid<string> reading = GPAL.Grid.ToGPALObject();

            if (WindowsControl is DataGridView dgv && true == Alive(dgv))
            {
                if (true == dgv.InvokeRequired)
                    OnUi(dgv, () => ReadInto(dgv, reading));
                else
                    ReadInto(dgv, reading);
            }
            else if (null != _grid)
                foreach (List<string> row in _grid)
                    reading.AddRow(new List<string>(row));

            grid = reading;

            return this;
        }

        /// <summary>
        /// Copies the range out of the live control, one row per row and one cell per column.
        /// </summary>
        /// <param name="dgv">The control being read</param>
        /// <param name="reading">The grid being filled</param>
        private void ReadInto(DataGridView dgv, IGPALGrid<string> reading)
        {
            int firstRow = 1 > _startRow ? 1 : _startRow;
            int lastRow = 1 > _endRow ? dgv.Rows.Count : (_endRow > dgv.Rows.Count ? dgv.Rows.Count : _endRow);
            int firstColumn = 1 > _startColumn ? 1 : _startColumn;
            int lastColumn = 1 > _endColumn ? dgv.Columns.Count : (_endColumn > dgv.Columns.Count ? dgv.Columns.Count : _endColumn);

            // Column names first, so a saved file has headers; only columns in the range.
            if (0 < dgv.Columns.Count)
            {
                List<string> headers = new List<string>();

                for (int column = firstColumn; column <= lastColumn; column++)
                    headers.Add(dgv.Columns[column - 1].HeaderText ?? string.Empty);

                reading.AddRow(headers);
            }

            for (int row = firstRow; row <= lastRow; row++)
            {
                if (true == dgv.Rows[row - 1].IsNewRow)
                    continue;

                List<string> cells = new List<string>();

                for (int column = firstColumn; column <= lastColumn; column++)
                    cells.Add(dgv.Rows[row - 1].Cells[column - 1].Value?.ToString() ?? string.Empty);

                reading.AddRow(cells);
            }
        }

        internal void SyncGridToControl()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv) || _grid == null)
            {
                return;
            }

            if (dgv.InvokeRequired)
            {
                OnUi(dgv, SyncGridToControl);
                return;
            }

            dgv.Rows.Clear();
            dgv.Columns.Clear();

            // Determine column count from first non-null row (defensive)
            int columnCount = 0;
            foreach (List<string> row in _grid)
            {
                if (row != null)
                {
                    columnCount = row.Count;
                    break;
                }
            }

            // Columns
            if (_columnNames.Count > 0)
            {
                int colIndex = 0;
                foreach (string header in _columnNames)
                {
                    if (colIndex >= columnCount) break;
                    dgv.Columns.Add(header, header);
                    colIndex++;
                }
            }
            else if (_autoGenerateColumns && columnCount > 0)
            {
                for (int i = 0; i < columnCount; i++)
                {
                    string header = $"Column {i + 1}";
                    dgv.Columns.Add(header, header);
                }
            }

            // Rows
            foreach (List<string> rowData in _grid)
            {
                if (rowData == null) continue;

                var dgvRow = new DataGridViewRow();
                var cells = new DataGridViewCell[rowData.Count];

                int colIndex = 0;
                foreach (string cellValue in rowData)
                {
                    cells[colIndex] = new DataGridViewTextBoxCell
                    {
                        Value = cellValue ?? ""
                    };
                    colIndex++;
                }

                dgvRow.Cells.AddRange(cells);
                dgv.Rows.Add(dgvRow);
            }

            dgv.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            dgv.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);

            // Refilling the rows resized every column, so reapply the workflow's widths, as the list view does in
            // RefitSizedColumns.
            ApplyColumnWidths();
            ApplyWrap();
        }

        /// <summary>
        /// The grid data currently displayed, as set via <see cref="WithInput(IGPALGrid{string})"/>.
        /// Setting this refreshes the underlying control via <see cref="SyncGridToControl"/>.
        /// </summary>
        public IGPALGrid<string> CurrentGrid
        {
            get => _grid;
            set
            {
                _grid = value;
                SyncGridToControl();
            }
        }
        /// <summary>
        /// The column header names set via <see cref="WithColumns(string[])"/>.
        /// </summary>
        public IReadOnlyList<string> ColumnNames => _columnNames.AsReadOnly();
        /// <summary>
        /// Whether columns are auto-generated when no column names were provided, as set via
        /// <see cref="WithAutoGenerateColumns(bool)"/>.
        /// </summary>
        public bool AutoGenerateColumns => _autoGenerateColumns;
    }

    /// <summary>
    /// A GPAL single line input instantiated with GPAL.Input for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALInput.WindowsControl.TextChanged event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALInput gPalInput = (GPALInput)GPAL.Input
    ///     .WithText(@"c:\drivers")
    ///     .WithCallback&lt;EventHandler&gt;(InputHandler); // on text changed event handler
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalInput)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALInput : GPALControl, IAllowControlSettingsPlaceholderAndPassword
    {
        internal bool InputIsPassword { get; set; } = false;
        internal string PlaceHolder { get; set; }
        internal GPALInput()
        {
            ControlType = FormControlType.Input;
        }
        /// <summary>
        /// The input a password or other sensitive information.<br/>
        /// The input will be masked with a bullet character.
        /// NOTE: CAVEAT: hovering over this in dev studio will set the input to password and will not trigger breakpoint in here
        /// </summary>
        /// <returns>Fluent interface to define more settings</returns>
        /// <summary>
        /// Runtime access to show or hide what a password input holds, for a "show password" button.
        /// </summary>
        /// <returns>True - the text is readable<br/>False [default for a password input] - the text is bulleted</returns>
        public bool ShowPassword
        {
            get
            {
                return false == InputIsPassword;
            }
            set
            {
                InputIsPassword = false == value;

                if (WindowsControl is PlaceholderTextBox ptbInput && true == Alive(ptbInput))
                {
                    char passwordChar = true == value ? '\0' : '●';  // bullet

                    OnUi(ptbInput, () => ptbInput.PasswordChar = passwordChar);
                }
            }
        }

        public IAllowControlSettingsPlaceholderAndPassword IsPassword
        {
            get
            {
                InputIsPassword = true;
                if (true == String.IsNullOrEmpty(PlaceHolder))
                    PlaceHolder = "Enter Password";
                return this;
            }
        }
        public IAllowControlSettingsPlaceholderAndPassword WithPlaceholder(string PlaceholderText)
        {
            PlaceHolder = PlaceholderText;
            if (WindowsControl is PlaceholderTextBox tb && true == Alive(tb))
                tb.PlaceholderText = PlaceholderText;
            return this;
        }

        public override IAllowControlSettings WithReadOnly(bool readOnly = true)
        {
            IsReadOnly = readOnly;
            if (WindowsControl is PlaceholderTextBox tb && true == Alive(tb))
                tb.ReadOnly = readOnly;
            return this;
        }

    }
    /// <summary>
    /// A GPAL lable instantiated with GPAL.Label for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALLabel.WindowsControl.Clicked event.<br/>
    /// Labels autosize to fit the form.
    /// </summary>
    /// <example>
    /// <code>
    /// Font font = new Font("Times New Roman", 12.0f);
    /// 
    /// static GPALLabel gPalLabel = (GPALLabel)GPAL.Label
    ///     .WithText("Form Tester")
    ///     .WithFont(font)
    ///     .WithTextAlignment(ContentAlignment.TopCenter);
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalLabel)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    // TOFO: font type, font size
    public class GPALLabel : GPALControl, IAllowControlSettingsAndFontSettings
    {
        internal GPALLabel()
        {
            ControlType = FormControlType.Label;
        }
    }
    /// <summary>
    /// Empty space of a set height between two controls on a GPAL form, instantiated with GPAL.SpacerFor.
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Form
    ///     .WithFormControl(moreBotsLink)
    ///     .WithFormControl(GPAL.SpacerFor(12))
    ///     .WithFormControl(okButton)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALSpacer : GPALControl
    {
        internal GPALSpacer()
        {
            ControlType = FormControlType.Spacer;
        }
    }
    /// <summary>
    /// A GPAL link instantiated with GPAL.LinkLabel or GPAL.LinkLabelFor for use on GPAL forms.<br/>
    /// Clicking it opens its address in the default browser: the one given with WithUrl, or its own text when that
    /// is an address. A callback given with WithCallback runs on the click as well.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALLinkLabel moreBots = (GPALLinkLabel)GPAL.LinkLabel
    ///     .WithUrl("https://gpal.sditx.com/docs/botw")
    ///     .WithText("More Bots of the Week");
    ///
    /// static GPALLinkLabel docs = GPAL.LinkLabelFor("https://gpal.sditx.com/");
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("About")
    ///     .WithFormControl(moreBots)
    ///     .WithFormControl(docs)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALLinkLabel : GPALControl, IAllowControlSettingsAndUrl
    {
        internal GPALLinkLabel()
        {
            ControlType = FormControlType.LinkLabel;
        }

        /// <summary>The address the link opens, or null to open its own text when that is an address.</summary>
        public string Url { get; internal set; }

        /// <summary>
        /// Sets the address the link opens in the default browser. Set it before WithText, since WithText
        /// moves on to the font settings.
        /// </summary>
        /// <param name="url">The address to open</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndUrl WithUrl([URL] string url)
        {
            Url = url;
            return this;
        }

        /// <summary>
        /// Opens the link's address in the default browser, the way clicking it does. Nothing happens when there
        /// is no address and the text is not one.
        /// </summary>
        internal void Open()
        {
            string target = false == string.IsNullOrWhiteSpace(Url) ? Url : Text;

            if (true == target?.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || true == target?.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                GPAL.Launcher.WithExecutable(target).Launch();
        }
    }
    /// <summary>
    /// A GPAL radiobutton instantiated with GPAL.RadioButton for use GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALRadioButton.WindowsControl.CheckChanged event.<br/>
    /// Default is not selected
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALRadioButton gPalRadioButton1 = (GPALRadioButton)GPAL.RadioButton
    ///     .WithChecked(true)
    ///     .WithName("Radiobutton1")
    ///     .WithText("Chrome")
    ///     .WithCallback&lt;EventHandler&gt;(RadioButtonHandler); // on check changed event handler
    ///     
    /// static GPALRadioButton gPalRadioButton2 = (GPALRadioButton)GPAL.RadioButton
    ///     .WithName("Radiobutton2")
    ///     .WithText("Edge")
    ///     .WithCallback&lt;EventHandler&gt;(RadioButtonHandler); // on check changed event handler
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalRadioButton1)
    ///     .WithFormControl(gPalRadioButton2)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    // TODO: group box
    public class GPALRadioButton : GPALControl, IAllowControlSettingsAndChecked
    {
        internal GPALRadioButton()
        {
            ControlType = FormControlType.RadioButton;
        }
        /// <summary>
        /// Sets whether the radiobutton is checked.
        /// </summary>
        /// <param name="isChecked">True<br/>False [default]</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndChecked WithChecked(bool isChecked)
        {
            this.Checked = isChecked;
            return this;
        }
        /// <summary>
        /// Runtime access to un/check the radiobutton. Setting this writes through to the live control
        /// (if realized), marshalling to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        /// <returns>True [checked]<br/>False [default] - unchecked</returns>
        public bool Checked
        {
            get
            {
                bool retVal = IsChecked;

                if (WindowsControl is RadioButton rb && true == Alive(rb))
                    retVal = OnUiRead(rb, () => rb.Checked, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is RadioButton rb && true == Alive(rb))
                {
                    OnUi(rb, () => rb.Checked = value);
                }
                else
                {
                    IsChecked = value;
                }
            }
        }
    }
    /// <summary>
    /// A GPAL tab instantiated with GPAL.Tab for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALTab.WindowsControl.Clicked event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALTab gPalTab1 = (GPALTab)GPAL.Tab
    ///     .WithName("Tab1")
    ///     .WithText("Tab 1")
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.D1); // Alt-1
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithTab(gPalTab1)  // items after this are placed on this tab
    ///     .WithFormControl(gPalRadioButton1)
    ///     .WithFormControl(gPalRadioButton2)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALTab : GPALControl, IAllowControlSettings
    {
        internal GPALTab()
        {
            ControlType = FormControlType.Tab;
        }
        public override bool Enabled
        {
            get
            {
                // A GPALTab's control is its TabPage; the TabControl would answer for every tab.
                bool retVal = IsEnabled;

                if (WindowsControl is TabPage page && true == Alive(page))
                    retVal = OnUiRead(page, () => page.Enabled, retVal);

                return retVal;
            }
            set
            {
                IsEnabled = value;

                if (!(WindowsControl is TabPage page) || false == Alive(page)) return;

                OnUi(page, () => page.Enabled = value);
            }
        }
        /// <summary>
        /// Activate (switch to) this tab at runtime, bringing its controls into view.
        /// </summary>
        public void Activate()
        {
            // Marshalled like the other setters; this is called from a workflow thread.
            if (WindowsControl is TabPage tabPage && tabPage.Parent is TabControl tabControl)
                OnUi(tabControl, () => tabControl.SelectedTab = tabPage);
        }
    }
    /// <summary>
    /// Marks where one pane of a <see cref="GPALSplitter"/> ends and the other begins; add it with GPAL.SplitRight or
    /// GPAL.SplitBottom.<br/>
    /// It is not a control and cannot be added to a form.
    /// </summary>
    public class GPALSpliter
    {
        internal GPALSpliter() { }
    }

    /// <summary>
    /// Two panes with a bar between them, instantiated with GPAL.Splitter and laid out like any other control.<br/>
    /// The controls of both panes are added in order, with GPAL.SplitRight or GPAL.SplitBottom marking where the
    /// first pane ends. Everything before the marker is the left or top pane, everything after it the right or
    /// bottom. With no marker at all, everything is in the first pane.<br/>
    /// A splitter is built completely before it is added, so it can hold another splitter, a group box or a plain
    /// control, nested exactly as written.
    /// <example>
    /// <code>
    /// GPAL.Splitter.HSplit(0.55)
    ///     .WithFormControl(GPAL.LabelFor("Automation Engine:"))
    ///     .WithFormControl(comboBoxAutomationEngine)
    ///     .WithFormControl(GPAL.SplitRight)
    ///     .WithFormControl(GPAL.LabelFor("Retry Count:"))
    ///     .WithFormControl(gPALNumeric);
    /// </code>
    /// </example>
    /// </summary>
    public class GPALSplitter : GPALControl, IAllowSplitterControlSettings
    {
        /// <summary>True for a vertical bar with left and right panes, false for a horizontal bar with top and bottom.</summary>
        internal bool IsHorizontalSplit { get; private set; } = true;
        /// <summary>Size of the first pane in pixels, used when <see cref="Percentage"/> is zero.</summary>
        internal int Dimension { get; private set; }
        /// <summary>Size of the first pane as 0.0 to 1.0 of the splitter, which keeps its proportion as the form resizes.</summary>
        internal double Percentage { get; private set; }
        internal bool IsPercentage => 0 < Percentage;
        /// <summary>Everything added, in order, with the pane marker still in place so the panes can be told apart.</summary>
        internal List<object> FormControls { get; } = new List<object>();

        internal GPALSplitter() { }

        /// <summary>
        /// How tall the splitter is. Without one it is measured from what the panes hold.
        /// </summary>
        /// <param name="height">Height in pixels</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public new IAllowSplitterControlSettings WithHeight([Pixels] int height)
        {
            Height = height;
            return this;
        }
        /// <summary>
        /// A vertical bar with a left and a right pane, the left one this many pixels wide.
        /// </summary>
        /// <param name="leftWidth">Width of the left pane in pixels</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings HSplit([Pixels] int leftWidth)
        {
            IsHorizontalSplit = true;
            Dimension = leftWidth;
            return this;
        }
        /// <summary>
        /// A vertical bar with a left and a right pane, the left one this share of the width, so it keeps its
        /// proportion as the form resizes.
        /// </summary>
        /// <param name="leftPercent">Share of the width for the left pane, 0.0 to 1.0</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings HSplit(double leftPercent)
        {
            IsHorizontalSplit = true;
            Percentage = leftPercent;
            return this;
        }
        /// <summary>
        /// A horizontal bar with a top and a bottom pane, the top one this many pixels tall.
        /// </summary>
        /// <param name="topHeight">Height of the top pane in pixels</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings VSplit([Pixels] int topHeight)
        {
            IsHorizontalSplit = false;
            Dimension = topHeight;
            return this;
        }
        /// <summary>
        /// A horizontal bar with a top and a bottom pane, the top one this share of the height, so it keeps its
        /// proportion as the form resizes.
        /// </summary>
        /// <param name="topPercent">Share of the height for the top pane, 0.0 to 1.0</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings VSplit(double topPercent)
        {
            IsHorizontalSplit = false;
            Percentage = topPercent;
            return this;
        }
        /// <summary>
        /// Add a control to the splitter. Everything before GPAL.SplitRight or GPAL.SplitBottom belongs to the
        /// first pane, everything after it to the second.
        /// </summary>
        /// <param name="gPALFormControl">A control, another container, or the pane marker</param>
        /// <returns>Fluent interface to add more controls</returns>
        public IAllowSplitterControlSettings WithFormControl(object gPALFormControl)
        {
            FormControls.Add(gPALFormControl);
            return this;
        }
        /// <summary>
        /// Returns this splitter so it can be handed to a form or to another container.
        /// </summary>
        /// <returns>This splitter</returns>
        public new GPALSplitter ToGPALObject()
        {
            return this;
        }
    }

    /// <summary>
    /// A titled box around a set of controls, instantiated with GPAL.GroupBox and laid out like any other control.
    /// <example>
    /// <code>
    /// GPAL.GroupBox.WithText("Options")
    ///     .WithFormControl(GPAL.LabelFor("Retry Count:"))
    ///     .WithFormControl(gPALNumeric);
    /// </code>
    /// </example>
    /// </summary>
    public class GPALGroupBox : GPALControl, IAllowGroupBoxControlSettings
    {
        /// <summary>Everything inside the box, in order.</summary>
        internal List<object> FormControls { get; } = new List<object>();

        internal GPALGroupBox() { }

        /// <summary>
        /// The caption along the top of the box.
        /// </summary>
        /// <param name="text">Caption to display</param>
        /// <returns>Fluent interface to add the controls inside</returns>
        public new IAllowGroupBoxControlSettings WithText(string text)
        {
            Text = text;
            return this;
        }
        /// <summary>
        /// Add a control inside the box. A container can go in here too, so a group box can hold a splitter.
        /// </summary>
        /// <param name="gPALFormControl">A control or another container</param>
        /// <returns>Fluent interface to add more controls</returns>
        public IAllowGroupBoxControlSettings WithFormControl(object gPALFormControl)
        {
            FormControls.Add(gPALFormControl);
            return this;
        }
        /// <summary>
        /// Returns this group box so it can be handed to a form or to another container.
        /// </summary>
        /// <returns>This group box</returns>
        public new GPALGroupBox ToGPALObject()
        {
            return this;
        }
    }

    /// <summary>
    /// A table layout panel instantiated with GPAL.TableLayoutPanel for use on GPAL forms, used to
    /// arrange other controls in a configurable grid of rows and columns.
    /// </summary>
    public class GPALTableLayoutPanel : GPALControl, IAllowControlSettingsAndTableLayoutPanelSettings
    {
        internal GPALTableLayoutPanel()
        {
            ControlType = FormControlType.TableLayoutPanel;
        }

        // Values used before the form is built, as IsEnabled backs Enabled; the three properties below use them until
        // there is a WindowsControl. Defaults are the WinForms ones.
        private TableLayoutPanelGrowStyle growStyle = TableLayoutPanelGrowStyle.AddRows;
        private int columnCount = 0;
        private int rowCount = 0;
        /// <summary>
        /// The direction in which the panel automatically adds new rows or columns when it runs out of
        /// space, mirroring the underlying <see cref="TableLayoutPanel.GrowStyle"/>.
        /// </summary>
        /// <returns>The grow style currently in use.</returns>
        public TableLayoutPanelGrowStyle GrowStyle
        {
            get
            {
                TableLayoutPanelGrowStyle retVal = growStyle;

                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    retVal = OnUiRead(panel, () => panel.GrowStyle, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    OnUi(panel, () => panel.GrowStyle = value);

                growStyle = value;   // kept either way, so the value survives the control not being there yet
            }
        }
        /// <summary>
        /// The number of columns in the panel, mirroring the underlying <see cref="TableLayoutPanel.ColumnCount"/>.
        /// </summary>
        /// <returns>The column count currently in use.</returns>
        public int ColumnCount
        {
            get
            {
                int retVal = columnCount;

                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    retVal = OnUiRead(panel, () => panel.ColumnCount, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    OnUi(panel, () => panel.ColumnCount = value);

                columnCount = value;   // kept either way, so the value survives the control not being there yet
            }
        }

        /// <summary>
        /// The number of rows in the panel, mirroring the underlying <see cref="TableLayoutPanel.RowCount"/>.
        /// </summary>
        /// <returns>The row count currently in use.</returns>
        public int RowCount
        {
            get
            {
                int retVal = rowCount;

                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    retVal = OnUiRead(panel, () => panel.RowCount, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    OnUi(panel, () => panel.RowCount = value);

                rowCount = value;   // kept either way, so the value survives the control not being there yet
            }
        }


        /// <summary>
        /// Sets the number of columns in the panel.
        /// </summary>
        /// <param name="columnCount">The desired column count.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettingsAndTableLayoutPanelSettings WithColumnCount(int columnCount)
        {
            ColumnCount = columnCount;
            return this;
        }

        /// <summary>
        /// Fluent interface to set the initial state of the control
        /// </summary>
        /// <param name="enabled">True [default]<br/>False</param>
        /// <returns>Fluent interface to define more settings</returns>
        public new IAllowControlSettingsAndTableLayoutPanelSettings WithEnabled(bool enabled = true)
        {
            this.Enabled = enabled;
            return this;
        }
        /// <summary>
        /// Sets the direction in which the panel automatically adds new rows or columns when it runs out
        /// of space.
        /// </summary>
        /// <param name="tableLayoutPanelGrowStyle">The desired grow style.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettingsAndTableLayoutPanelSettings WithGrowStyle(TableLayoutPanelGrowStyle tableLayoutPanelGrowStyle)
        {
            this.GrowStyle = tableLayoutPanelGrowStyle;
            return this;
        }

        /// <summary>
        /// Sets the number of rows in the panel.
        /// </summary>
        /// <param name="rowCount">The desired row count.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettingsAndTableLayoutPanelSettings WithRowCount(int rowCount)
        {
            RowCount = rowCount;
            return this;
        }
    }
    /// <summary>
    /// A GPAL multiline textare instantiated with GPAL.TextArea for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALTextarea.WindowsControl.TextChanged event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALTextArea gPalTextArea = (GPALTextArea)GPAL.TextArea
    ///     .WithText("Type something here")
    ///     .WithCallback&lt;EventHandler&gt;(TextAreaHandler) // on text changed event handler
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.T);  // Alt-T
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalTextArea)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALTextArea : GPALControl, IAllowControlSettingsAndPlaceholder
    {
        internal string PlaceHolder { get; set; }

        // Lines wait here for the control's owning thread. See AppendLine.

        internal GPALTextArea()
        {
            ControlType = FormControlType.TextArea;
        }

        public IAllowControlSettingsAndPlaceholder WithPlaceholder(string PlaceholderText)
        {
            PlaceHolder = PlaceholderText;
            if (WindowsControl is PlaceholderTextBox tb && true == Alive(tb))
                tb.PlaceholderText = PlaceholderText;
            return this;
        }

        public override IAllowControlSettings WithReadOnly(bool readOnly = true)
        {
            IsReadOnly = readOnly;
            if (WindowsControl is PlaceholderTextBox tb && true == Alive(tb))
                tb.ReadOnly = readOnly;
            return this;
        }

        /// <summary>
        /// Appends <paramref name="text"/> as a new line, thread-safe. When <paramref name="maxLines"/> is
        /// greater than zero and the control already has that many lines, the oldest line is dropped first.
        /// </summary>
        /// <param name="text">The line to append.</param>
        /// <param name="maxLines">Rolling cap; 0 means unlimited.</param>
        public void AppendLine(string text, int maxLines = 0) => AppendLineCore(text, maxLines);

        /// <summary>
        /// Empties the control and anything queued for it, thread-safe.
        /// </summary>
        public void Clear() => ClearLinesCore();

        /// <summary>
        /// Scrolls the text area to the last line, thread-safe.
        /// </summary>
        public void ScrollToEnd() => ScrollToEndCore();
    }
    /// <summary>
    /// A read-only display control for rendering simple Markdown - headings (#, ##, ###), **bold**,
    /// *italic*, __underline__, ~~strikethrough~~, `inline code`, {colorname}text{} color spans,
    /// --- horizontal rules, and bullet (-, *) or numbered (1.) lists with nesting via leading spaces.
    /// Intended for displaying formatted AI responses or other structured text.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALRichTextBox gPalOutput = (GPALRichTextBox)GPAL.RichTextBox
    ///     .WithName("AIOutput")
    ///     .ToGPALObject();
    ///
    /// gPalOutput.Text = "# Summary\n\n**Key points:**\n- *First* point\n- __Second__ point\n- {red}Warning{}\n  - Nested detail";
    /// </code>
    /// </example>
    public class GPALRichTextBox : GPALControl, IAllowRichTextBoxControlSettings
    {
        private string _text = string.Empty;

        internal GPALRichTextBox()
        {
            ControlType = FormControlType.RichTextBox;
            // A rich text box shows rendered markdown, so it is read-only by default.
            IsReadOnly = true;
        }

        /// <summary>
        /// The Markdown source text. Setting this re-renders headings, **bold** spans, and bullet/numbered
        /// lists (with nesting via leading spaces) into the live control (if realized), marshalling to the
        /// UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public new string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                if (WindowsControl is RichTextBox rtb && true == Alive(rtb))
                {
                    PostToUi(rtb, () => RenderMarkdown(rtb, _text));
                }
            }
        }

        /// <summary>
        /// Appends <paramref name="text"/> as a new line, thread-safe.<br/>
        /// The line is appended verbatim rather than rendered as Markdown, so a line beginning with # or * is
        /// not treated as a heading or a bullet. <see cref="Text"/> is updated to match.
        /// </summary>
        /// <param name="text">The line to append.</param>
        /// <param name="maxLines">Rolling cap; 0 means unlimited.</param>
        public void AppendLine(string text, int maxLines = 0)
        {
            _text = 0 == _text.Length ? text : _text + Environment.NewLine + text;

            AppendLineCore(text, maxLines);
        }

        /// <summary>
        /// Empties the control and anything queued for it, thread-safe.
        /// </summary>
        public void Clear()
        {
            _text = string.Empty;

            ClearLinesCore();
        }

        /// <summary>
        /// Scrolls to the last line, thread-safe.
        /// </summary>
        public void ScrollToEnd() => ScrollToEndCore();

        /// <summary>
        /// Sets the initial Markdown source text.
        /// </summary>
        /// <param name="text">Markdown text to render.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public new IAllowRichTextBoxControlSettings WithText(string text)
        {
            this.Text = text;
            return this;
        }


        public override IAllowControlSettings WithReadOnly(bool readOnly = true)
        {
            IsReadOnly = readOnly;
            if (WindowsControl is RichTextBox rtb && true == Alive(rtb))
                rtb.ReadOnly = readOnly;
            return this;
        }

        // matches **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, {colorname}text{} — bold before italic to avoid ** ambiguity
        private static readonly Regex _inlinePattern = new Regex(
            @"\*\*(?<b>.+?)\*\*|\*(?<i>.+?)\*|__(?<u>.+?)__|~~(?<s>.+?)~~|`(?<code>.+?)`|{(?<c>[^}]+)}(?<ct>.+?)\{\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Renders Markdown source text into a <see cref="RichTextBox"/>:
        /// lines starting with #, ##, or ### become progressively larger bold headings;
        /// a line containing only --- becomes a gray horizontal rule;
        /// inline **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, and {colorname}text{} spans are supported;
        /// lines starting with "- ", "* ", or "1. " (optionally indented in steps of two spaces)
        /// become bulleted/numbered paragraphs with indentation proportional to nesting depth.
        /// </summary>
        // Fonts used by the renderer, one per family, size and style; SelectionFont copies from it. Each font holds a
        // GDI handle, and re-rendering on every append would run out of handles with new fonts per render.
        private static readonly Dictionary<string, Font> renderFonts = new Dictionary<string, Font>();
        private static readonly object renderFontGate = new object();

        /// <summary>
        /// The font for one styled run of rendered Markdown, made once and kept.
        /// </summary>
        /// <param name="family">The font family name</param>
        /// <param name="size">The size in points</param>
        /// <param name="style">Bold, italic and the rest</param>
        /// <returns>The font, which belongs to the cache and must not be disposed by the caller</returns>
        private static Font RenderFont(string family, float size, FontStyle style)
        {
            string key = $"{family}|{size}|{(int)style}";
            Font retVal;

            lock (renderFontGate)
            {
                if (false == renderFonts.TryGetValue(key, out retVal))
                {
                    retVal = new Font(family, size, style);
                    renderFonts[key] = retVal;
                }
            }

            return retVal;
        }

        internal static void RenderMarkdown(RichTextBox rtb, string markdown)
        {
            rtb.Clear();

            Font baseFont = rtb.Font;
            Color baseColor = rtb.ForeColor;
            string codeFamily = "Courier New";
            string[] lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine;

                int headingLevel = 0;
                while (headingLevel < line.Length && line[headingLevel] == '#')
                    headingLevel++;
                if (headingLevel > 0 && headingLevel <= 3 && headingLevel < line.Length && line[headingLevel] == ' ')
                    line = line.Substring(headingLevel + 1);
                else
                    headingLevel = 0;

                int leadingSpaces = 0;
                while (leadingSpaces < line.Length && line[leadingSpaces] == ' ')
                    leadingSpaces++;
                int indentLevel = leadingSpaces / 2;
                string trimmed = line.Substring(leadingSpaces);

                // horizontal rule
                if (trimmed == "---")
                {
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.SelectionLength = 0;
                    rtb.SelectionFont = RenderFont(baseFont.FontFamily.Name, baseFont.Size, FontStyle.Regular);
                    rtb.SelectionColor = Color.Gray;
                    rtb.AppendText(new string('-', 48) + "\n");
                    continue;
                }

                bool isBulletItem = false;
                bool isListItem = false;
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    isBulletItem = true;
                    isListItem = true;
                    trimmed = trimmed.Substring(2);
                }
                else
                {
                    Match numberedMatch = Regex.Match(trimmed, @"^(\d+\.\s+)(.*)$");
                    if (numberedMatch.Success)
                    {
                        isListItem = true;
                        trimmed = numberedMatch.Groups[1].Value + numberedMatch.Groups[2].Value;
                    }
                }

                int lineStart = rtb.TextLength;

                FontStyle headingBase = headingLevel > 0 ? FontStyle.Bold : FontStyle.Regular;
                float headingSize = headingLevel > 0 ? baseFont.Size + (4 - headingLevel) * 2 : baseFont.Size;

                foreach ((string segmentText, FontStyle style, Color? color, bool isCode) in SplitInline(trimmed, headingBase))
                {
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.SelectionLength = 0;
                    rtb.SelectionFont = isCode
                        ? RenderFont(codeFamily, baseFont.Size, FontStyle.Regular)
                        : RenderFont(baseFont.FontFamily.Name, headingSize, style);
                    rtb.SelectionColor = isCode ? Color.SteelBlue : (color ?? baseColor);
                    rtb.AppendText(segmentText);
                }
                rtb.AppendText("\n");

                if (isListItem)
                {
                    rtb.SelectionStart = lineStart;
                    rtb.SelectionLength = rtb.TextLength - lineStart;
                    rtb.SelectionBullet = isBulletItem;
                    rtb.SelectionIndent = 20 * (indentLevel + 1);
                }
            }

            rtb.SelectionStart = 0;
            rtb.SelectionLength = 0;
        }

        /// <summary>
        /// Splits a line into inline spans supporting **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, and {colorname}text{}.
        /// <paramref name="inheritBase"/> is OR'd into every span's style so heading bold propagates into children.
        /// Color names are resolved via <see cref="Color.FromName"/>; unrecognised names fall back to null (default color).
        /// Code spans use monospace rendering regardless of style.
        /// </summary>
        private static List<(string Text, FontStyle Style, Color? Color, bool IsCode)> SplitInline(string line, FontStyle inheritBase = FontStyle.Regular)
        {
            var result = new List<(string Text, FontStyle Style, Color? Color, bool IsCode)>();
            int pos = 0;

            foreach (Match m in _inlinePattern.Matches(line))
            {
                if (m.Index > pos)
                    result.Add((line.Substring(pos, m.Index - pos), inheritBase, null, false));

                if (m.Groups["b"].Success)
                    result.Add((m.Groups["b"].Value, inheritBase | FontStyle.Bold, null, false));
                else if (m.Groups["i"].Success)
                    result.Add((m.Groups["i"].Value, inheritBase | FontStyle.Italic, null, false));
                else if (m.Groups["u"].Success)
                    result.Add((m.Groups["u"].Value, inheritBase | FontStyle.Underline, null, false));
                else if (m.Groups["s"].Success)
                    result.Add((m.Groups["s"].Value, inheritBase | FontStyle.Strikeout, null, false));
                else if (m.Groups["code"].Success)
                    result.Add((m.Groups["code"].Value, FontStyle.Regular, null, true));
                else if (m.Groups["c"].Success)
                {
                    Color named = Color.FromName(m.Groups["c"].Value);
                    Color? resolved = named.IsKnownColor || named.A > 0 ? (Color?)named : null;
                    // recurse so bold/italic/underline inside a color span still render correctly
                    foreach (var inner in SplitInline(m.Groups["ct"].Value, inheritBase))
                        result.Add((inner.Item1, inner.Item2, inner.Item3 ?? resolved, inner.Item4));
                }

                pos = m.Index + m.Length;
            }

            if (pos < line.Length)
                result.Add((line.Substring(pos), inheritBase, null, false));
            if (result.Count == 0)
                result.Add((string.Empty, inheritBase, null, false));

            return result;
        }
    }
    /// <summary>
    /// A dropdown list (ComboBox) for selecting one option from a predefined set of choices.
    /// Selection changes trigger WhenSelectedDo callback(s).
    /// By default uses DropDownList style (no free text entry).
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALComboBox browserSelector = (GPALComboBox)GPAL.ComboBox
    ///     .WithName("BrowserChoice")
    ///     .WithItems("Chrome", "Edge", "Firefox")
    ///     .WithSelectedIndex(0)
    ///     .WhenSelectedDo(selected => 
    ///         Debug.WriteLine($"Browser changed to: {selected}"))
    ///     .WithToolTip("Select the target browser for automation")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALComboBox : GPALControl, IAllowComboBoxControlSettings
    {
        internal readonly List<string> items = new List<string>();
        internal int selectedIndex = -1;
        internal string selectedItem = null;

        // Items that stay in the list but cannot be chosen. A greyed option is clearer than one that silently
        // disappears.
        internal readonly HashSet<string> disabledItems = new HashSet<string>();

        // what to fall back to when a disabled item is picked, so the box never sits on something invalid
        internal int lastEnabledIndex = -1;

        internal GPALComboBox() 
        {
            ControlType = FormControlType.ComboBox;
        }

        /// <summary>
        /// Appends the given items to the dropdown list, in order.
        /// </summary>
        /// <param name="itemArray">The items to add.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithItems(params string[] itemArray)
        {
            if (null != itemArray)
            {
                items.AddRange(itemArray);
                ShowItems(itemArray);
            }
            return this;
        }

        // Keep the stored and on-screen lists in step. Before the form is shown only the stored list exists.
        private void ShowItems(IEnumerable<string> itemsToShow)
        {
            if (!(WindowsControl is ComboBox cb) || false == Alive(cb)) return;

            object[] adding = itemsToShow.Cast<object>().ToArray();

            OnUi(cb, () => cb.Items.AddRange(adding));
        }

        /// <summary>
        /// Greys an item and makes it unselectable, keeping it in the list. For an option that does not apply given
        /// other choices.
        /// </summary>
        /// <param name="item">The item text to disable.</param>
        public void DisableItem(string item)
        {
            if (true == string.IsNullOrEmpty(item)) return;

            disabledItems.Add(item);
            Redraw();
        }

        /// <summary>
        /// Makes a disabled item choosable again.
        /// </summary>
        /// <param name="item">The item text to enable.</param>
        public void EnableItem(string item)
        {
            if (true == string.IsNullOrEmpty(item)) return;

            disabledItems.Remove(item);
            Redraw();
        }

        /// <summary>
        /// Whether the item is currently choosable.
        /// </summary>
        /// <param name="item">The item text to ask about.</param>
        /// <returns>True when the item can be chosen.</returns>
        public bool IsItemEnabled(string item)
        {
            return false == disabledItems.Contains(item);
        }

        private void Redraw()
        {
            if (!(WindowsControl is ComboBox cb) || false == Alive(cb)) return;

            OnUi(cb, () => cb.Invalidate());
        }

        /// <summary>
        /// Empties the dropdown, so its contents can be replaced with a list that suits what has been chosen
        /// elsewhere on the form. Clears what is on screen as well as what the form was built with.
        /// </summary>
        public void ClearItems()
        {
            items.Clear();
            disabledItems.Clear();
            selectedIndex = -1;
            selectedItem = null;
            lastEnabledIndex = -1;

            if (!(WindowsControl is ComboBox cb) || false == Alive(cb)) return;

            OnUi(cb, () => cb.Items.Clear());
        }

        /// <summary>
        /// Appends the given items to the dropdown list, in order.
        /// </summary>
        /// <param name="itemCollection">The items to add.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithItems(IEnumerable<string> itemCollection)
        {
            if (null != itemCollection)
            {
                items.AddRange(itemCollection);
            }
            return this;
        }

        /// <summary>
        /// Appends the items from a non-generic array (e.g. the result of <see cref="Enum.GetValues"/>)
        /// to the dropdown list, converting each element to its string representation.
        /// </summary>
        /// <param name="itemArray">The array whose elements will be added.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithItems(Array itemArray)
        {
            if (null != itemArray)
            {
                foreach (object item in itemArray)
                    items.Add(item?.ToString() ?? string.Empty);
            }
            return this;
        }

        /// <summary>
        /// Sets which item is initially selected, by index. Ignored if <paramref name="index"/> is out of
        /// range of the items added via <see cref="WithItems(string[])"/>.
        /// </summary>
        /// <param name="index">The 0-based index of the item to select.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithSelectedIndex([Index] int index)
        {
            selectedIndex = index;
            selectedItem = null;
            return this;
        }

        /// <summary>
        /// Sets which item is initially selected, by its display text. If the item is not found
        /// in the list the selection is left at the default (-1 / no selection).
        /// </summary>
        /// <param name="name">The exact display text of the item to pre-select.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithSelectedItem(string name)
        {
            selectedItem = name;
            selectedIndex = -1;
            return this;
        }

        /// <summary>
        /// The text of the currently selected item.
        /// </summary>
        /// <returns>The selected item's text, or <c>null</c> if no item is selected.</returns>
        public string SelectedValue
        {
            get
            {
                int idx = SelectedIndex;
                if ((0 > idx) || (idx >= items.Count))
                    return null;
                return items[idx];
            }
        }

        /// <summary>
        /// The 0-based index of the currently selected item, or -1 if none is selected. Setting this
        /// writes through to the live control (if realized), marshalling to the UI thread via
        /// <see cref="Control.Invoke(Delegate)"/> if required. Ignored if set out of range of the items
        /// added via <see cref="WithItems(string[])"/>.
        /// </summary>

        public int SelectedIndex
        {
            // Read on the UI thread when called from another thread, as the setters write on it.
            get
            {
                int retVal = selectedIndex;

                if (WindowsControl is ComboBox cb && true == Alive(cb))
                    retVal = OnUiRead(cb, () => cb.SelectedIndex, retVal);

                return retVal;
            }
            set
            {
                if ((0 > value) || (value >= items.Count)) return;

                selectedIndex = value;
                if (WindowsControl is ComboBox cb && true == Alive(cb))
                {
                    OnUi(cb, () => cb.SelectedIndex = value);
                }
            }
        }
    }

    /// <summary>
    /// A progress indicator control that shows completion percentage or indeterminate activity.
    /// Supports continuous bar, marquee animation and basic value tracking.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALProgressBar taskProgress = GPAL.ProgressBar
    ///     .WithName("DownloadProgress")
    ///     .WithMaximum(100)
    ///     .WithValue(42)
    ///     .WithStyle(ProgressBarStyle.Continuous)
    ///     .WithToolTip("Shows current task progress")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
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

    /// <summary>
    /// A GPAL slider instantiated with GPAL.Slider, for picking a number from a range by dragging.<br/>
    /// Callback EventHandler is invoked on the underlying TrackBar's ValueChanged event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALSlider volume = (GPALSlider)GPAL.Slider
    ///     .WithMinimum(0)
    ///     .WithMaximum(100)
    ///     .WithValue(50)
    ///     .WithTickFrequency(10)
    ///     .WithName("volume")
    ///     .ToGPALObject();
    ///
    /// // and later, from anywhere
    /// volume.Value = 75;
    /// </code>
    /// </example>
    public class GPALSlider : GPALControl, IAllowSliderControlSettings
    {
        private int _value = 0;
        private int _minimum = 0;
        private int _maximum = 100;
        private int _tickFrequency = 10;
        private Orientation _orientation = Orientation.Horizontal;

        internal GPALSlider()
        {
            ControlType = FormControlType.Slider;
        }

        /// <summary>
        /// Where the slider sits, clamped to the range it was given.
        /// </summary>
        /// <returns>The current value</returns>
        public int Value
        {
            get => OnUiRead(WindowsControl as TrackBar, () => ((TrackBar)WindowsControl).Value, _value);

            set
            {
                _value = value < _minimum ? _minimum : value > _maximum ? _maximum : value;

                SyncToControl();
            }
        }

        /// <summary>The lowest value the slider can be dragged to.</summary>
        public int Minimum => _minimum;

        /// <summary>The highest value the slider can be dragged to.</summary>
        public int Maximum => _maximum;

        /// <summary>How far apart the tick marks are.</summary>
        public int TickFrequency => _tickFrequency;

        /// <summary>Horizontal or vertical.</summary>
        public Orientation Orientation => _orientation;

        /// <summary>
        /// The lowest value the slider can be dragged to.
        /// </summary>
        /// <param name="min">The minimum. A value below it is raised to meet it</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithMinimum(int min)
        {
            _minimum = min;

            if (_value < min)
                _value = min;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// The highest value the slider can be dragged to.
        /// </summary>
        /// <param name="max">The maximum. A value above it is lowered to meet it</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithMaximum(int max)
        {
            _maximum = max;

            if (_value > max)
                _value = max;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// Where the slider starts.
        /// </summary>
        /// <param name="value">The value, clamped to the range</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithValue(int value)
        {
            Value = value;
            return this;
        }

        /// <summary>
        /// How far apart the tick marks are.
        /// </summary>
        /// <param name="tickFrequency">Ticks every this many units</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithTickFrequency(int tickFrequency)
        {
            _tickFrequency = tickFrequency;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// Horizontal or vertical.
        /// </summary>
        /// <param name="orientation">Horizontal by default</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithOrientation(Orientation orientation)
        {
            _orientation = orientation;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// Set the range before the value, so the control's old range does not reject it.
        /// </summary>
        private void SyncToControl()
        {
            if (WindowsControl is TrackBar bar && true == Alive(bar))
                OnUi(bar, () =>
                {
                    bar.Minimum = _minimum;
                    bar.Maximum = _maximum;
                    bar.TickFrequency = 1 > _tickFrequency ? 1 : _tickFrequency;
                    bar.Orientation = _orientation;
                    bar.Value = _value;
                });
        }
    }

    public class GPALProgressBar : GPALControl, IAllowProgressBarControlSettings
    {
        private int _value = 0;
        private int _minimum = 0;
        private int _maximum = 100;
        private ProgressBarStyle _style = ProgressBarStyle.Blocks;

        internal GPALProgressBar()
        {
            ControlType = FormControlType.ProgressBar;
        }

        /// <summary>
        /// Sets the initial progress value, clamped to <see cref="Minimum"/>/<see cref="Maximum"/>, and
        /// syncs it to the underlying control.
        /// </summary>
        /// <param name="value">The desired progress value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithValue(int value)
        {
            this.Value = value;
            return this;
        }

        /// <summary>
        /// Sets the minimum progress value. If <see cref="Value"/> is below this, it is raised to match.
        /// </summary>
        /// <param name="min">The desired minimum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithMinimum(int min)
        {
            _minimum = min;
            if (_value < min) _value = min;
            SyncValueToControl();
            return this;
        }

        /// <summary>
        /// Sets the maximum progress value. If <see cref="Value"/> exceeds this, it is lowered to match.
        /// </summary>
        /// <param name="max">The desired maximum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithMaximum(int max)
        {
            _maximum = max;
            if (_value > max) _value = max;
            SyncValueToControl();
            return this;
        }

        /// <summary>
        /// Sets the visual style of the progress bar (e.g. continuous, blocks, marquee).
        /// </summary>
        /// <param name="style">The desired progress bar style.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithStyle(ProgressBarStyle style)
        {
            this.Style = style;
            return this;
        }

        /// <summary>
        /// Runtime access to advance the progress value by <paramref name="step"/>, clamped to
        /// <see cref="Minimum"/>/<see cref="Maximum"/>.
        /// </summary>
        /// <param name="step">The amount to add to the current value. Defaults to 1.</param>
        public void Increment(int step = 1)
        {
            this.Value += step;
        }

        /// <summary>
        /// Applies <see cref="Value"/> to the underlying <see cref="ProgressBar"/>, marshalling to the UI
        /// thread via <see cref="Control.Invoke(Delegate)"/> if required. No-op if the underlying control
        /// is not a <see cref="ProgressBar"/>.
        /// </summary>
        private void SyncValueToControl()
        {
            if (WindowsControl is ProgressBar pb && true == Alive(pb))
            {
                OnUi(pb, () => Apply(pb));
            }
        }

        /// <summary>
        /// Sets the range before the value, so the control's old range does not reject it. An out-of-range Value
        /// throws, which a bar whose maximum grows would hit.
        /// </summary>
        /// <param name="pb">The control to update.</param>
        private void Apply(ProgressBar pb)
        {
            // Set Maximum first when increasing and Minimum first when decreasing; the control rejects a minimum above
            // its maximum.
            if (pb.Maximum < _maximum)
            {
                pb.Maximum = _maximum;
                pb.Minimum = _minimum;
            }
            else
            {
                pb.Minimum = _minimum;
                pb.Maximum = _maximum;
            }

            pb.Value = _value;
        }

        /// <summary>
        /// Applies <see cref="Style"/> to the underlying <see cref="ProgressBar"/>, marshalling to the UI
        /// thread via <see cref="Control.Invoke(Delegate)"/> if required. No-op if the underlying control
        /// is not a <see cref="ProgressBar"/>.
        /// </summary>
        private void SyncStyleToControl()
        {
            if (WindowsControl is ProgressBar pb && true == Alive(pb))
            {
                OnUi(pb, () => pb.Style = _style);
            }
        }

        /// <summary>
        /// The current progress value, clamped to <see cref="Minimum"/>/<see cref="Maximum"/>. Setting this
        /// writes through to the live control (if realized), marshalling to the UI thread via
        /// <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(_minimum, Math.Min(_maximum, value));
                SyncValueToControl();
            }
        }
        /// <summary>
        /// The minimum progress value, as set via <see cref="WithMinimum(int)"/>.
        /// </summary>
        public int Minimum => _minimum;
        /// <summary>
        /// The maximum progress value, as set via <see cref="WithMaximum(int)"/>.
        /// </summary>
        public int Maximum => _maximum;
        /// <summary>
        /// The current visual style. Setting this writes through to the live control (if realized),
        /// marshalling to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public ProgressBarStyle Style
        {
            get => _style;
            set
            {
                _style = value;
                SyncStyleToControl();
            }
        }
    }

    /// <summary>
    /// A status bar (usually docked at the bottom of the form) that displays real-time messages, 
    /// progress indicators, or simple key-value status pairs.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALStatusStrip statusBar = GPAL.StatusStrip
    ///     .WithName("MainStatus")
    ///     .WithStatusText("Ready - Idle")
    ///     .WithToolTip("Current application status")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALStatusStrip : GPALControl, IAllowStatusStripControlSettings
    {
        private string _text = "";

        internal GPALStatusStrip()
        {
            ControlType = FormControlType.StatusStrip;
        }

        /// <summary>
        /// The status text. Blank or whitespace falls back to "Ready". Setting this writes through to
        /// the first <see cref="ToolStripStatusLabel"/> of the live control (if realized), marshalling
        /// to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public new string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                SyncTextToControl();
            }
        }

        /// <summary>
        /// Sets the initial status text. Blank or whitespace falls back to "Ready".
        /// </summary>
        /// <param name="text">The status text to display.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public new IAllowStatusStripControlSettings WithText(string text)
        {
            this.Text = text;
            return this;
        }

        /// <summary>
        /// Applies <see cref="_text"/> to the first <see cref="ToolStripStatusLabel"/> in the underlying
        /// <see cref="StatusStrip"/>'s items, marshalling to the UI thread via
        /// <see cref="Control.Invoke(Delegate)"/> if required. No-op if the underlying control is not a
        /// <see cref="StatusStrip"/> with at least one item, or that item is not a <see cref="ToolStripStatusLabel"/>.
        /// </summary>
        private void SyncTextToControl()
        {
            if (WindowsControl is StatusStrip strip && true == Alive(strip) && strip.Items.Count > 0)
            {
                ToolStripStatusLabel label = strip.Items[0] as ToolStripStatusLabel;
                if (label != null)
                {
                    PostToUi(strip, () => label.Text = _text);
                }
            }
        }
    }
    /// <summary>
    /// A single item shared by GPAL menu bars and toolbars. Because it derives from
    /// <see cref="GPALControl"/> it speaks the same one-parameter fluent vocabulary as every other
    /// control (WithCallback, WithShortcutKey, WithName, WithToolTip), and adds an optional image.
    /// The callback added via <see cref="GPALControl.WithCallback{TDelegate}(TDelegate)"/> is wired to
    /// the item's Click event; the shortcut set via <see cref="GPALControl.WithShortcutKey"/> is shown
    /// on menu items (toolbars have no per-button shortcut).
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALBarItem saveItem = (GPALBarItem)GPAL.BarItem
    ///     .WithText("Save")
    ///     .WithImage(saveIcon)
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)
    ///     .WithCallback&lt;EventHandler&gt;(SaveHandler)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALBarItem : GPALControl, IAllowBarItemSettings
    {
        internal System.Drawing.Image Image { get; set; }

        internal GPALBarItem()
        {
            ControlType = FormControlType.BarItem;
        }

        /// <summary>
        /// Greys the item and makes it unclickable, keeping the menu layout.
        /// </summary>
        /// <param name="trueFalse">False to grey the item out.</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public new IAllowBarItemSettings WithEnabled(bool trueFalse = true)
        {
            Enabled = trueFalse;
            return this;
        }

        /// <summary>
        /// Whether the item can be clicked. Readable and settable while the form is up, so a menu can follow
        /// what the workflow is doing.
        /// </summary>
        public override bool Enabled
        {
            get
            {
                return WindowsControl is ToolStripItem strip ? strip.Enabled : IsEnabled;
            }
            set
            {
                IsEnabled = value;

                if (!(WindowsControl is ToolStripItem strip)) return;

                // A ToolStripItem has no InvokeRequired; ask its owning strip.
                Control owner = strip.Owner;

                OnUiOrHere(owner, () => strip.Enabled = value);
            }
        }

        /// <summary>
        /// Sets the item's display text.
        /// </summary>
        /// <param name="text">The text to display on the menu item or toolbar button.</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public new IAllowBarItemSettings WithText(string text)
        {
            this.Text = text;
            return this;
        }

        /// <summary>
        /// Sets the item's icon from an in-memory image.
        /// </summary>
        /// <param name="image">The image to display (menu items show it left of the text; toolbar buttons show it on the button).</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public IAllowBarItemSettings WithImage(System.Drawing.Image image)
        {
            this.Image = image;
            return this;
        }

        /// <summary>
        /// Sets the item's icon by loading it from the file referenced by a <see cref="GPALFile"/>.
        /// </summary>
        /// <param name="file">The GPAL file whose first filename points to an image on disk.</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public IAllowBarItemSettings WithImage(GPALFile file)
        {
            try
            {
                string path = file?.Filenames?.FirstOrDefault() ?? (string)file;
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    this.Image = System.Drawing.Image.FromFile(path);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Image file not found for bar item [{path}]", this, GPALObjectType.Other);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to load bar item image [{file?.Filename}]", this, GPALObjectType.Other, ex);
            }
            return this;
        }
    }
    /// <summary>
    /// A menu bar docked to the top of a GPAL form. Open a top-level menu with
    /// <see cref="WithMenu(string)"/>, then attach <see cref="GPALBarItem"/>s to it with
    /// <see cref="WithItem(GPALBarItem)"/> (and optional <see cref="WithSeparator"/> rules). Items added
    /// after a WithMenu call belong to that menu until the next WithMenu.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALMenuBar gPALMenuBar = (GPALMenuBar)GPAL.MenuBar
    ///     .WithMenu("File")
    ///     .WithItem(saveItem)
    ///     .WithSeparator()
    ///     .WithItem(exitItem)
    ///     .WithMenu("Help")
    ///     .WithItem(aboutItem)
    ///     .WithName("MainMenu")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALMenuBar : GPALControl, IAllowMenuBarControlSettings
    {
        private string _currentMenu;
        internal readonly List<(string TopMenu, GPALBarItem Item, bool IsSeparator)> menuEntries
            = new List<(string, GPALBarItem, bool)>();

        internal GPALMenuBar()
        {
            ControlType = FormControlType.MenuBar;
        }

        /// <summary>
        /// Opens (or reselects) a top-level menu. Subsequent <see cref="WithItem(GPALBarItem)"/> and
        /// <see cref="WithSeparator"/> calls attach to this menu until the next WithMenu.
        /// </summary>
        /// <param name="topMenu">The top-level menu caption (e.g. "File", "Help").</param>
        /// <returns>Fluent interface to add items to this menu.</returns>
        public IAllowMenuBarControlSettings WithMenu(string topMenu)
        {
            _currentMenu = topMenu;
            return this;
        }

        /// <summary>
        /// Adds an item to the currently open top-level menu.
        /// </summary>
        /// <param name="item">The bar item to add.</param>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowMenuBarControlSettings WithItem(GPALBarItem item)
        {
            menuEntries.Add((_currentMenu, item, false));
            return this;
        }

        /// <summary>
        /// Adds a separator line to the currently open top-level menu.
        /// </summary>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowMenuBarControlSettings WithSeparator()
        {
            menuEntries.Add((_currentMenu, null, true));
            return this;
        }
    }
    /// <summary>
    /// A toolbar docked below the menu bar, containing <see cref="GPALBarItem"/> buttons and optional
    /// separators.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALToolbar gPALToolbar = (GPALToolbar)GPAL.Toolbar
    ///     .WithItem(newItem)
    ///     .WithItem(openItem)
    ///     .WithSeparator()
    ///     .WithItem(runItem)
    ///     .WithName("MainToolbar")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALToolbar : GPALControl, IAllowToolbarControlSettings
    {
        internal readonly List<(GPALBarItem Item, bool IsSeparator)> toolbarEntries
            = new List<(GPALBarItem, bool)>();

        internal GPALToolbar()
        {
            ControlType = FormControlType.Toolbar;
        }

        /// <summary>
        /// Adds a button to the toolbar.
        /// </summary>
        /// <param name="item">The bar item to add as a toolbar button.</param>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowToolbarControlSettings WithItem(GPALBarItem item)
        {
            toolbarEntries.Add((item, false));
            return this;
        }

        /// <summary>
        /// Adds a vertical separator between toolbar buttons.
        /// </summary>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowToolbarControlSettings WithSeparator()
        {
            toolbarEntries.Add((null, true));
            return this;
        }
    }
    /// <summary>
    /// A list view control capable of displaying data in Details, List, LargeIcon, SmallIcon, or Tile views.
    /// Supports columns, item selection, double-click activation and multi-select.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALListView fileResults = GPAL.ListView
    ///     .WithName("SavedFiles")
    ///     .WithColumns("Filename", "Size (KB)", "Saved At")
    ///     .WithView(ListViewMode.Details)
    ///     .WhenSelectedDo(selectedFile => Debug.WriteLine($"Selected: {selectedFile}"))
    ///     .WithToolTip("List of downloaded or processed files")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALListView : GPALControl, IAllowListViewControlSettings
    {
        private readonly List<string> columnHeaders = new List<string>();
        private readonly List<double> columnWeights = new List<double>();
        private ListViewMode viewMode = ListViewMode.Details;
        internal bool checkBoxes = false;
        private bool fullRowSelect = true;
        private bool multiSelect = false;
        private bool sortable = false;
        private bool tickAll = false;
        private bool tickingAll = false;

        internal GPALListView()
        {
            ControlType = FormControlType.ListView;
        }

        /// <summary>
        /// Shows a checkbox on every row. ControlEventType.ItemChecked reports changes.
        /// </summary>
        /// <param name="enable">True to show checkboxes.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithCheckBoxes(bool enable = true)
        {
            CheckBoxes = enable;
            return this;
        }

        /// <summary>
        /// Puts a checkbox on the first column header. Clicking it ticks every row that is not disabled, or clears every
        /// row, and it shows ticked whenever every row that is not disabled is ticked. Turns on row checkboxes too.
        /// </summary>
        /// <param name="enable">True to show the header checkbox.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithTickAll(bool enable = true)
        {
            tickAll = enable;

            if (true == enable)
                checkBoxes = true;

            return this;
        }

        /// <summary>
        /// Whether the first column header carries a tick-all checkbox, as set via <see cref="WithTickAll(bool)"/>.
        /// </summary>
        public bool TickAll => tickAll;

        // Ticks every row that is not disabled, or clears every row, then shows the result on the header box.
        internal void TickAllRows(GPALListViewControl lv)
        {
            bool tick = false == lv.HeaderChecked;

            tickingAll = true;

            foreach (ListViewItem item in lv.Items)
                item.Checked = true == tick && SystemColors.GrayText != item.ForeColor;

            tickingAll = false;

            SyncHeaderBox();
        }

        // The header box is ticked when there is a row that is not disabled and every one of them is ticked. Runs on
        // the UI thread; while the header is ticking every row, it waits for the end.
        internal void SyncHeaderBox()
        {
            if (WindowsControl is GPALListViewControl lv && true == tickAll && false == tickingAll)
            {
                List<ListViewItem> open = lv.Items.Cast<ListViewItem>().Where(item => SystemColors.GrayText != item.ForeColor).ToList();

                lv.HeaderChecked = 0 < open.Count && open.All(item => item.Checked);
            }
        }

        /// <summary>
        /// Whether rows carry a checkbox. Settable while the form is up.
        /// </summary>
        public bool CheckBoxes
        {
            get
            {
                return WindowsControl is ListView lv ? lv.CheckBoxes : checkBoxes;
            }
            set
            {
                checkBoxes = value;

                if (!(WindowsControl is ListView lv) || false == Alive(lv)) return;

                OnUi(lv, () => lv.CheckBoxes = value);
            }
        }

        /// <summary>
        /// Sets the column headers for the list view (used in <see cref="ListViewMode.Details"/> mode).
        /// </summary>
        /// <param name="headers">The column header names, in order.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithColumns(params string[] headers)
        {
            if (headers != null)
            {
                columnHeaders.AddRange(headers);
            }
            return this;
        }

        /// <summary>
        /// Sets how items are displayed (Details, List, LargeIcon, SmallIcon, or Tile). Defaults to <see cref="ListViewMode.Details"/>.
        /// </summary>
        /// <param name="view">The desired view mode.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithView(ListViewMode view)
        {
            viewMode = view;
            return this;
        }

        /// <summary>
        /// Sets whether selecting an item highlights the entire row. Defaults to <c>true</c>.
        /// </summary>
        /// <param name="enable"><c>true</c> to highlight the full row when selected; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithFullRowSelect(bool enable)
        {
            fullRowSelect = enable;
            return this;
        }

        /// <summary>
        /// Sets whether multiple items can be selected at once. Defaults to <c>false</c>.
        /// </summary>
        /// <param name="enable"><c>true</c> to allow multi-select; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithMultiSelect(bool enable)
        {
            multiSelect = enable;
            return this;
        }

        /// <summary>
        /// Enables click-to-sort on all columns. Clicking a column header sorts ascending;
        /// clicking again reverses to descending. Compares numeric, date, then string.
        /// </summary>
        public IAllowListViewControlSettings WithSortable(bool sortable = true)
        {
            this.sortable = sortable;
            return this;
        }

        /// <summary>
        /// Adds a proportional width weight for the next column (in declaration order).
        /// Weights do not need to sum to 1 -- they are normalized automatically.
        /// When any weight is set the ListView resizes columns proportionally on resize.
        /// </summary>
        public IAllowListViewControlSettings WithColumnWeight(double weight)
        {
            columnWeights.Add(weight);
            return this;
        }

        /// <summary>
        /// The column header names set via <see cref="WithColumns(string[])"/>.
        /// </summary>
        public IReadOnlyList<string> Columns => columnHeaders.AsReadOnly();

        /// <summary>
        /// Proportional column weights set via <see cref="WithColumnWeight(double)"/>.
        /// </summary>
        public IReadOnlyList<double> ColumnWeights => columnWeights.AsReadOnly();

        /// <summary>Whether column-click sorting is enabled.</summary>
        public bool Sortable => sortable;
        /// <summary>
        /// The view mode set via <see cref="WithView(ListViewMode)"/>, mapped to the underlying WinForms View.
        /// </summary>
        public View ViewMode
        {
            get
            {
                switch (viewMode)
                {
                    case ListViewMode.List:      return View.List;
                    case ListViewMode.LargeIcon: return View.LargeIcon;
                    case ListViewMode.SmallIcon: return View.SmallIcon;
                    case ListViewMode.Tile:      return View.Tile;
                    default:                     return View.Details;
                }
            }
        }
        /// <summary>
        /// Whether full-row selection is enabled, as set via <see cref="WithFullRowSelect(bool)"/>.
        /// </summary>
        public bool FullRowSelect => fullRowSelect;
        /// <summary>
        /// Whether multi-select is enabled, as set via <see cref="WithMultiSelect(bool)"/>.
        /// </summary>
        public bool MultiSelect => multiSelect;

        public int SelectedIndex
        {
            get
            {
                int retVal = -1;

                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () => 0 < lv.SelectedItems.Count ? lv.SelectedItems[0].Index : -1, retVal);

                return retVal;
            }
        }

        public void ClearItems()
        {
            if (!(WindowsControl is ListView lv) || false == Alive(lv)) return;
            OnUi(lv, () =>
            {
                lv.Items.Clear();
                SyncHeaderBox();
            });
        }

        /// <summary>
        /// Greys a row and makes it unselectable, keeping it in the list. By position, since text can repeat.
        /// </summary>
        /// <param name="index">The 0-based row to disable.</param>
        public void DisableItem([Index] int index)
        {
            SetItemEnabled(index, false);
        }

        /// <summary>
        /// Makes a disabled row selectable again.
        /// </summary>
        /// <param name="index">The 0-based row to enable.</param>
        public void EnableItem([Index] int index)
        {
            SetItemEnabled(index, true);
        }

        /// <summary>
        /// Whether the row at this position can be selected.
        /// </summary>
        /// <param name="index">The 0-based row to ask about.</param>
        /// <returns>True when the row can be selected, and when there is no such row.</returns>
        public bool IsItemEnabled([Index] int index)
        {
            bool retVal = true;

            if (!(WindowsControl is ListView lv) || false == Alive(lv)) return retVal;

            MethodInvoker read = () =>
            {
                if (0 <= index && index < lv.Items.Count)
                    retVal = SystemColors.GrayText != lv.Items[index].ForeColor;
            };

            OnUi(lv, read);

            return retVal;
        }

        // The row's color is its enabled state, so look and behavior always match.
        private void SetItemEnabled(int index, bool enabled)
        {
            if (!(WindowsControl is ListView lv) || false == Alive(lv)) return;

            MethodInvoker paint = () =>
            {
                if (0 > index || index >= lv.Items.Count) return;

                lv.Items[index].ForeColor = true == enabled ? SystemColors.WindowText : SystemColors.GrayText;

                if (false == enabled) lv.Items[index].Selected = false;

                SyncHeaderBox();
            };

            OnUi(lv, paint);
        }

        /// <summary>
        /// Ticks the checkbox on a row. By position, since text can repeat. ControlEventType.ItemChecked reports it
        /// like a click would.
        /// </summary>
        /// <param name="index">The 0-based row to tick.</param>
        public void CheckItem([Index] int index)
        {
            SetItemChecked(index, true);
        }

        /// <summary>
        /// Clears the checkbox on a row.
        /// </summary>
        /// <param name="index">The 0-based row to clear.</param>
        public void UncheckItem([Index] int index)
        {
            SetItemChecked(index, false);
        }

        // Sets the box on the UI thread; a row that does not exist is left alone.
        private void SetItemChecked(int index, bool isChecked)
        {
            if (WindowsControl is ListView lv && true == Alive(lv))
                OnUi(lv, () =>
                {
                    if (0 <= index && index < lv.Items.Count)
                        lv.Items[index].Checked = isChecked;
                });
        }

        public void AddItem(string title, params string[] subItems)
        {
            // Rows added before the list exists are kept until it does.
            if (!(WindowsControl is ListView lv) || false == Alive(lv))
            {
                pendingRows.Enqueue(new Tuple<string, string[]>(title, subItems ?? new string[0]));
                return;
            }

            var item = new ListViewItem(title);
            foreach (var s in subItems) item.SubItems.Add(s);
            OnUi(lv, () =>
            {
                lv.Items.Add(item);
                SyncHeaderBox();
            });
        }

        // what was added before the list existed, in the order it was added
        private readonly System.Collections.Concurrent.ConcurrentQueue<Tuple<string, string[]>> pendingRows =
            new System.Collections.Concurrent.ConcurrentQueue<Tuple<string, string[]>>();

        /// <summary>
        /// The form has built the list, so anything added before now goes in.
        /// </summary>
        internal override void Realized()
        {
            while (true == pendingRows.TryDequeue(out Tuple<string, string[]> held))
                AddItem(held.Item1, held.Item2);

            // A list on an unopened tab has no handle, so earlier fits were refused. Rows can be measured once the tab
            // is first shown.
            if (WindowsControl is ListView lv)
            {
                lv.HandleCreated += (sender, args) => LayOutColumns();
                lv.VisibleChanged += (sender, args) => LayOutColumns();
                lv.Resize += (sender, args) => LayOutColumns();
            }
        }


        /// <summary>
        /// Sets each column's share of the width, in column order.
        /// </summary>
        /// <remarks>
        /// Shares need not add up to anything and reflow as the form resizes. Integer arguments pick the pixel
        /// overload.
        /// </remarks>
        /// <example>
        /// <code>
        ///     .WithColumnWidth(0.2, 0.1, 0.7)   // shares
        ///     .WithColumnWidth(100, 60, -2)     // pixels, -2 fills the rest on the last column
        /// </code>
        /// </example>
        /// <param name="shares">The shares, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithColumnWidth(params double[] shares)
        {
            columnWeights.Clear();

            if (null != shares)
                columnWeights.AddRange(shares);

            return this;
        }

        // how wide each column is, in column order. Empty leaves every column as the control makes it
        internal readonly List<int> _columnWidths = new List<int>();

        /// <summary>
        /// Sets column widths in pixels, in column order; fewer widths than columns leaves the rest alone. -1 sizes a
        /// column to its widest content and -2 to the wider of that and its header, recalculated as rows are added or
        /// cleared.
        /// </summary>
        /// <param name="widths">The widths, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithColumnWidth(params int[] widths)
        {
            _columnWidths.Clear();

            if (null != widths)
                _columnWidths.AddRange(widths);

            ApplyColumnWidths();

            return this;
        }

        // Whether the workflow set any widths. With shares, the form reflows on resize and this does nothing.
        internal bool LastColumnFills => 0 == columnWeights.Count;

        // Margin after the last column, so narrowing the form never adds a horizontal scrollbar.
        internal const int FillMargin = 2;

        // narrow enough to be a column, wide enough to be clicked on
        private const int NarrowestColumn = 24;

        // Setting a column's width raises the resize event this runs from.
        private bool layingOut = false;

        /// <summary>
        /// Sets all column widths at once.<br/>
        /// An unset column gets an equal share of what the set widths leave.
        /// </summary>
        /// <remarks>
        /// Runs on handle creation, show and resize, not when rows are added.
        /// </remarks>
        internal void LayOutColumns()
        {
            if (!(WindowsControl is ListView lv) || false == Alive(lv) || 0 < columnWeights.Count)
                return;

            if (true == lv.InvokeRequired)
            {
                OnUi(lv, LayOutColumns);
                return;
            }

            if (false == lv.IsHandleCreated || 0 == lv.Columns.Count || true == layingOut)
                return;

            layingOut = true;

            try
            {
                int available = Math.Max(NarrowestColumn * lv.Columns.Count, lv.ClientSize.Width - FillMargin);
                int[] widths = new int[lv.Columns.Count];
                int named = 0;
                int unnamed = 0;

                // A set width is positive. A sentinel, zero or unset column shares the remainder.
                for (int column = 0; column < widths.Length; column++)
                    if (column < _columnWidths.Count && 0 < _columnWidths[column])
                    {
                        widths[column] = _columnWidths[column];
                        named += widths[column];
                    }
                    else
                        unnamed++;

                if (0 < unnamed)
                {
                    int each = Math.Max(NarrowestColumn, (available - named) / unnamed);

                    for (int column = 0; column < widths.Length; column++)
                        if (0 == widths[column])
                            widths[column] = each;
                }

                // Too wide: scale all columns down together.
                int total = 0;

                foreach (int width in widths)
                    total += width;

                if (available < total && 0 < total)
                {
                    for (int column = 0; column < widths.Length; column++)
                        widths[column] = Math.Max(NarrowestColumn, widths[column] * available / total);

                    total = 0;

                    foreach (int width in widths)
                        total += width;
                }

                // the last column ends the row at the edge
                widths[widths.Length - 1] += available - total;

                if (NarrowestColumn > widths[widths.Length - 1])
                    widths[widths.Length - 1] = NarrowestColumn;

                for (int column = 0; column < widths.Length; column++)
                    lv.Columns[column].Width = widths[column];
            }
            finally
            {
                layingOut = false;
            }
        }

        // Apply before the form is built and again after; an existing control will not be rebuilt.
        internal void ApplyColumnWidths()
        {
            LayOutColumns();
        }

        /// <summary>
        /// The text of the first selected item, or <c>null</c> if nothing is selected.
        /// </summary>
        public string SelectedText
        {
            get
            {
                string retVal = null;

                // Read on the UI thread with a guard, like SelectedIndex. Directly from a workflow thread it could
                // throw or be half-updated.
                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () => 0 < lv.SelectedItems.Count ? lv.SelectedItems[0].Text : null, retVal);

                return retVal;
            }
        }

        /// <summary>
        /// Which rows are checked, one bool per row, in the current row order: added order unless .WithSortable and a
        /// header click reordered them.<br/>
        /// A workflow can walk its own data by the same index. Empty when the list has no rows or is not on a form.
        /// </summary>
        /// <returns>One bool per row, true where the box is checked</returns>
        public IReadOnlyList<bool> CheckedItems
        {
            get
            {
                List<bool> retVal = new List<bool>();

                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () =>
                    {
                        List<bool> ticked = new List<bool>();

                        foreach (ListViewItem item in lv.Items)
                            ticked.Add(item.Checked);

                        return ticked;
                    }, retVal);

                return retVal.AsReadOnly();
            }
        }

        /// <summary>
        /// Which rows are selected, one bool per row, in the current row order.<br/>
        /// Selected is not checked; see <see cref="CheckedItems"/>. Use after .WithMultiSelect, since SelectedIndex
        /// only returns the first.
        /// </summary>
        /// <returns>One bool per row, true where the row is selected</returns>
        public IReadOnlyList<bool> SelectedItems
        {
            get
            {
                List<bool> retVal = new List<bool>();

                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () =>
                    {
                        List<bool> chosen = new List<bool>();

                        foreach (ListViewItem item in lv.Items)
                            chosen.Add(item.Selected);

                        return chosen;
                    }, retVal);

                return retVal.AsReadOnly();
            }
        }
    }
    /// <summary>
    /// A numeric up/down spinner control instantiated with GPAL.NumericUpDown for use on GPAL forms.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALNumericUpDown gPalQuantity = (GPALNumericUpDown)GPAL.NumericUpDown
    ///     .WithMinimum(0)
    ///     .WithMaximum(10)
    ///     .WithIncrement(1)
    ///     .WithValue(1)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALNumericUpDown : GPALControl, IAllowNumericUpDownControlSettings
    {
        private decimal _value = 0m;
        private decimal _minimum = 0m;
        private decimal _maximum = 100m;
        private decimal _increment = 1m;
        private int _decimalPlaces = 0;

        internal GPALNumericUpDown()
        {
            ControlType = FormControlType.NumericUpDown;
        }

        /// <summary>
        /// Sets the initial value.
        /// </summary>
        /// <param name="value">The desired value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithValue(decimal value)
        {
            _value = value;
            return this;
        }

        /// <summary>
        /// Sets the minimum allowed value. Defaults to 0.
        /// </summary>
        /// <param name="min">The desired minimum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithMinimum(decimal min)
        {
            _minimum = min;
            return this;
        }

        /// <summary>
        /// Sets the maximum allowed value. Defaults to 100.
        /// </summary>
        /// <param name="max">The desired maximum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithMaximum(decimal max)
        {
            _maximum = max;
            return this;
        }

        /// <summary>
        /// Sets the amount the value changes when the up/down spinner is clicked. Defaults to 1.
        /// </summary>
        /// <param name="inc">The desired increment.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithIncrement(decimal inc)
        {
            _increment = inc;
            return this;
        }

        /// <summary>
        /// Sets how many decimal places are displayed. Defaults to 0.
        /// </summary>
        /// <param name="places">The number of decimal places to display.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithDecimalPlaces(int places)
        {
            _decimalPlaces = places;
            return this;
        }

        /// <summary>
        /// Runtime access to the value. Reads the live control once the form is up, so a value the user
        /// spun to is the value a workflow gets, and falls back to <see cref="WithValue(decimal)"/> before
        /// then. Setting writes through to the live control, marshalling to the UI thread when required.
        /// </summary>
        public decimal Value
        {
            get
            {
                decimal retVal = _value;

                if (WindowsControl is NumericUpDown nud && true == Alive(nud))
                    retVal = OnUiRead(nud, () => nud.Value, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is NumericUpDown nud && true == Alive(nud))
                {
                    OnUi(nud, () => nud.Value = value);
                }
                else
                {
                    _value = value;
                }
            }
        }
        /// <summary>
        /// The minimum allowed value, as set via <see cref="WithMinimum(decimal)"/>.
        /// </summary>
        public decimal Minimum => _minimum;
        /// <summary>
        /// The maximum allowed value, as set via <see cref="WithMaximum(decimal)"/>.
        /// </summary>
        public decimal Maximum => _maximum;
        /// <summary>
        /// The spinner increment, as set via <see cref="WithIncrement(decimal)"/>.
        /// </summary>
        public decimal Increment => _increment;
        /// <summary>
        /// The number of decimal places displayed, as set via <see cref="WithDecimalPlaces(int)"/>.
        /// </summary>
        public int DecimalPlaces => _decimalPlaces;
    }
    /// <summary>
    /// A date/time picker control instantiated with GPAL.DateTimePicker for use on GPAL forms.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALDateTimePicker gPalDate = (GPALDateTimePicker)GPAL.DateTimePicker
    ///     .WithFormat(DateTimePickerFormat.Short)
    ///     .WithValue(DateTime.Today)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALDateTimePicker : GPALControl, IAllowDateTimePickerControlSettings
    {
        private DateTime _value = DateTime.Now;
        private DateTimePickerFormat _format = DateTimePickerFormat.Long;
        private string _customFormat = "";

        internal GPALDateTimePicker()
        {
            ControlType = FormControlType.DateTimePicker;
        }

        /// <summary>
        /// Sets the initial date/time value. Defaults to the current date/time.
        /// </summary>
        /// <param name="value">The desired date/time.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDateTimePickerControlSettings WithValue(DateTime value)
        {
            _value = value;
            return this;
        }

        /// <summary>
        /// Sets the display format. Defaults to <see cref="DateTimePickerFormat.Long"/>. Use
        /// <see cref="DateTimePickerFormat.Custom"/> with <see cref="WithCustomFormat(string)"/> to
        /// specify a custom format string.
        /// </summary>
        /// <param name="format">The desired display format.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDateTimePickerControlSettings WithFormat(DateTimePickerFormat format)
        {
            _format = format;
            return this;
        }

        /// <summary>
        /// Sets a custom display format string, used when <see cref="Format"/> is <see cref="DateTimePickerFormat.Custom"/>.
        /// </summary>
        /// <param name="formatString">The custom .NET date/time format string.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDateTimePickerControlSettings WithCustomFormat(string formatString)
        {
            _customFormat = formatString;
            return this;
        }

        /// <summary>
        /// Runtime access to the date/time. Reads the live control once the form is up, so the date the user
        /// picked is the date a workflow gets, and falls back to <see cref="WithValue(DateTime)"/> before
        /// then. Setting writes through to the live control, marshalling to the UI thread when required.
        /// </summary>
        public DateTime Value
        {
            get
            {
                DateTime retVal = _value;

                if (WindowsControl is DateTimePicker dtp && true == Alive(dtp))
                    retVal = OnUiRead(dtp, () => dtp.Value, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is DateTimePicker dtp && true == Alive(dtp))
                {
                    OnUi(dtp, () => dtp.Value = value);
                }
                else
                {
                    _value = value;
                }
            }
        }
        /// <summary>
        /// The display format, as set via <see cref="WithFormat(DateTimePickerFormat)"/>.
        /// </summary>
        public DateTimePickerFormat Format => _format;
        /// <summary>
        /// The custom display format string, as set via <see cref="WithCustomFormat(string)"/>.
        /// </summary>
        public string CustomFormat => _customFormat;
    }
    /// <summary>
    /// A tree view control instantiated with GPAL.TreeView for use on GPAL forms.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALTreeView gPalTree = (GPALTreeView)GPAL.TreeView
    ///     .WithCheckBoxes(true)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALTreeView : GPALControl, IAllowTreeViewControlSettings
    {
        private bool _checkBoxes = false;
        private bool _showLines = true;
        private bool _showPlusMinus = true;

        internal GPALTreeView()
        {
            ControlType = FormControlType.TreeView;
        }

        /// <summary>
        /// Sets whether checkboxes are displayed next to each tree node.
        /// </summary>
        /// <param name="enable"><c>true</c> to show checkboxes; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowTreeViewControlSettings WithCheckBoxes(bool enable)
        {
            _checkBoxes = enable;
            return this;
        }

        /// <summary>
        /// Sets whether lines are drawn between sibling nodes and their parent.
        /// </summary>
        /// <param name="show"><c>true</c> to show lines; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowTreeViewControlSettings WithShowLines(bool show)
        {
            _showLines = show;
            return this;
        }

        /// <summary>
        /// Sets whether plus/minus expand buttons are displayed next to nodes that have children.
        /// </summary>
        /// <param name="show"><c>true</c> to show the plus/minus buttons; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowTreeViewControlSettings WithShowPlusMinus(bool show)
        {
            _showPlusMinus = show;
            return this;
        }

        /// <summary>
        /// Whether checkboxes are displayed next to each tree node, as set via <see cref="WithCheckBoxes(bool)"/>.
        /// </summary>
        public bool CheckBoxes => _checkBoxes;
        /// <summary>
        /// Whether lines are drawn between sibling nodes and their parent, as set via <see cref="WithShowLines(bool)"/>.
        /// </summary>
        public bool ShowLines => _showLines;
        /// <summary>
        /// Whether plus/minus expand buttons are displayed, as set via <see cref="WithShowPlusMinus(bool)"/>.
        /// </summary>
        public bool ShowPlusMinus => _showPlusMinus;

        /// <summary>
        /// Removes all nodes from the tree, thread-safe.
        /// </summary>
        public void ClearNodes()
        {
            // Also drop nodes added before the tree was built, so a clear means the same before and after.
            if (!(WindowsControl is TreeView tv) || false == Alive(tv))
            {
                lock (pendingNodes)
                    pendingNodes.Clear();
            }
            else
                OnUi(tv, () => tv.Nodes.Clear());
        }

        /// <summary>
        /// The node currently selected, or null when nothing is. Settable, so a workflow can put the user
        /// where it wants them.
        /// </summary>
        public GPALTreeNode SelectedNode
        {
            get
            {
                if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return null;

                TreeNode node = null;

                OnUi(tv, () => node = tv.SelectedNode);

                return null != node ? new GPALTreeNode(node) : null;
            }
            set
            {
                if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return;

                MethodInvoker select = () => tv.SelectedNode = value?.Node;

                OnUi(tv, select);
            }
        }

        /// <summary>
        /// Stops the tree redrawing until <see cref="EndUpdate"/>, so a large tree does not fill in row by row.
        /// </summary>
        public void BeginUpdate()
        {
            if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return;

            OnUi(tv, () => tv.BeginUpdate());
        }

        /// <summary>
        /// Lets the tree redraw again after <see cref="BeginUpdate"/>.
        /// </summary>
        public void EndUpdate()
        {
            if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return;

            OnUi(tv, () => tv.EndUpdate());
        }

        /// <summary>
        /// Adds a top-level node and returns a <see cref="GPALTreeNode"/> for attaching children.
        /// Thread-safe.
        /// </summary>
        /// <param name="text">The node label.</param>
        public GPALTreeNode AddNode(string text)
        {
            TreeNode node = null;

            // A node added before the tree is built is created now and kept. The workflow gets a real node (AddChild,
            // Text, Tag, Enabled and Expand work), and the tree takes it with its children once built.
            if (!(WindowsControl is TreeView tv) || false == Alive(tv))
            {
                node = new TreeNode(text);

                lock (pendingNodes)
                    pendingNodes.Add(node);
            }
            else
                OnUi(tv, () => node = tv.Nodes.Add(text));

            return node != null ? new GPALTreeNode(node) : null;
        }

        // the top-level nodes added before the form built the tree, in the order they were added
        private readonly List<TreeNode> pendingNodes = new List<TreeNode>();

        /// <summary>
        /// The form has built the tree, so the nodes added before now go in.
        /// </summary>
        internal override void Realized()
        {
            if (WindowsControl is TreeView tv)
                lock (pendingNodes)
                {
                    foreach (TreeNode node in pendingNodes)
                        tv.Nodes.Add(node);

                    pendingNodes.Clear();
                }
        }
    }

    /// <summary>
    /// A handle to a tree node returned by <see cref="GPALTreeView.AddNode(string)"/>.
    /// Use it to add children and control expansion.
    /// </summary>
    public class GPALTreeNode
    {
        private readonly TreeNode _node;

        internal GPALTreeNode(TreeNode node) { _node = node; }

        // the node this handle stands for, so the tree can hand one back and be given one
        internal TreeNode Node => _node;

        /// <summary>
        /// The node's label.
        /// </summary>
        public string Text
        {
            get
            {
                TreeView tv = _node.TreeView;
                string retVal = null;

                GPALControl.OnUiOrHere(tv, () => retVal = _node.Text);

                return retVal;
            }
            set
            {
                TreeView tv = _node.TreeView;

                GPALControl.OnUiOrHere(tv, () => _node.Text = value);
            }
        }

        /// <summary>
        /// Anything the workflow wants to carry on the node: the selector it stands for, the record it came
        /// from, an id to look up later. Untouched by GPAL.
        /// </summary>
        public object Tag
        {
            get { return _node.Tag; }
            set { _node.Tag = value; }
        }

        /// <summary>
        /// Makes this the selected node, thread-safe.
        /// </summary>
        public void Select()
        {
            TreeView tv = _node.TreeView;

            if (null == tv) return;

            GPALControl.OnUiOrHere(tv, () => tv.SelectedNode = _node);
        }

        /// <summary>
        /// Hides this node's children, thread-safe.
        /// </summary>
        public void Collapse()
        {
            TreeView tv = _node.TreeView;

            GPALControl.OnUiOrHere(tv, () => _node.Collapse());
        }

        /// <summary>
        /// Adds a child node under this node, thread-safe.
        /// </summary>
        /// <param name="text">The child node label.</param>
        /// <returns>The new child node, for further nesting.</returns>
        public GPALTreeNode AddChild(string text)
        {
            TreeNode child = null;
            TreeView tv = _node.TreeView;
            GPALControl.OnUiOrHere(tv, () => child = _node.Nodes.Add(text));
            return child != null ? new GPALTreeNode(child) : null;
        }


        /// <summary>
        /// Whether this node can be selected. A disabled node stays on the tree, greyed. Its children are unchanged;
        /// disable them too for the whole branch.
        /// </summary>
        public bool Enabled
        {
            get
            {
                return SystemColors.GrayText != _node.ForeColor;
            }
            set
            {
                TreeView tv = _node.TreeView;

                // The color is the state; TreeNode has no Enabled property.
                MethodInvoker paint = () => _node.ForeColor = true == value ? SystemColors.WindowText : SystemColors.GrayText;

                GPALControl.OnUiOrHere(tv, paint);
            }
        }

        /// <summary>
        /// Expands this node to show its children, thread-safe.
        /// </summary>
        public void Expand()
        {
            TreeView tv = _node.TreeView;
            GPALControl.OnUiOrHere(tv, () => _node.Expand());
        }
    }

    /// <summary>
    /// Event arguments passed to a <see cref="GPALFileSelector"/> callback when the file/folder
    /// selection dialog is closed, whether by the user picking a path or cancelling.
    /// </summary>
    public class FileSelectorEventArgs : EventArgs
    {
        /// <summary>
        /// <c>true</c> if the user dismissed the dialog without selecting a path.
        /// </summary>
        public bool Cancelled { get; }
        /// <summary>
        /// The first selected path, or an empty string if none was selected.
        /// </summary>
        public string SelectedPath { get; }
        /// <summary>
        /// All selected paths. Contains more than one entry only when the dialog allowed multi-select.
        /// </summary>
        public IReadOnlyList<string> SelectedPaths { get; }

        /// <summary>
        /// Creates the event arguments for a completed file selector dialog.
        /// </summary>
        /// <param name="cancelled"><c>true</c> if the dialog was cancelled.</param>
        /// <param name="selectedPath">The first selected path. Stored as an empty string if <c>null</c>.</param>
        /// <param name="selectedPaths">All selected paths. Stored as an empty list if <c>null</c> or empty.</param>
        public FileSelectorEventArgs(bool cancelled, string selectedPath, string[] selectedPaths)
        {
            Cancelled = cancelled;
            SelectedPath = selectedPath ?? "";
            if (selectedPaths == null || selectedPaths.Length == 0)
            {
                SelectedPaths = Array.AsReadOnly(Array.Empty<string>());
            }
            else
            {
                SelectedPaths = Array.AsReadOnly(selectedPaths);
            }
        }
    }

    /// <summary>
    /// A file/folder selector control instantiated with GPAL.FileSelector for use on GPAL forms.
    /// Displays a button which, when clicked, opens an <see cref="OpenFileDialog"/> or
    /// <see cref="FolderBrowserDialog"/> (depending on <see cref="WithSelectFolder(bool)"/>) and
    /// raises its callbacks with a <see cref="FileSelectorEventArgs"/> once the dialog closes.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALFileSelector gPalFileSelector = (GPALFileSelector)GPAL.FileSelector
    ///     .WithTitle("Select Input File")
    ///     .WithFilter("CSV files (*.csv)|*.csv")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALFileSelector : GPALControl, IAllowFileSelectorControlSettings
    {
        internal string _title = "Select File/Folder";
        internal string _initialDirectory = "";
        internal string _filter = "All files (*.*)|*.*";
        internal bool _selectFolder = false;       // true = FolderBrowserDialog, false = OpenFileDialog
        internal bool _multiSelect = false;
        internal bool _compact = false;
        internal string _placeholder = "";
        internal string _selectedPath = "";
        internal readonly List<string> _selectedPaths = new List<string>();

        internal GPALFileSelector()
        {
            ControlType = FormControlType.FileSelector;
        }

        /// <summary>
        /// Sets the title bar text of the selection dialog.
        /// </summary>
        /// <param name="title">The desired title. If null/whitespace, defaults to "Select File/Folder".</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithTitle(string title)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "Select File/Folder" : title;
            return this;
        }

        /// <summary>
        /// Sets the directory the selection dialog initially opens to.
        /// </summary>
        /// <param name="path">The initial directory path.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithInitialDirectory([Directory] string path)
        {
            _initialDirectory = path;
            return this;
        }

        /// <summary>
        /// Sets the file type filter used by the <see cref="OpenFileDialog"/> when
        /// <see cref="WithSelectFolder(bool)"/> is <c>false</c>.
        /// </summary>
        /// <param name="filter">The dialog filter string. If null/whitespace, defaults to "All files (*.*)|*.*".</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithFilter(string filter)
        {
            _filter = string.IsNullOrWhiteSpace(filter) ? "All files (*.*)|*.*" : filter;
            return this;
        }

        /// <summary>
        /// Sets whether the control opens a <see cref="FolderBrowserDialog"/> (folder selection) or an
        /// <see cref="OpenFileDialog"/> (file selection).
        /// </summary>
        /// <param name="folderMode"><c>true</c> to select folders; <c>false</c> [default] to select files.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithSelectFolder(bool folderMode)
        {
            _selectFolder = folderMode;
            return this;
        }

        /// <summary>
        /// Sets whether the file dialog allows selecting multiple files. Has no effect when
        /// <see cref="WithSelectFolder(bool)"/> is <c>true</c>.
        /// </summary>
        /// <param name="allowMulti"><c>true</c> to allow multiple file selection.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithMultiSelect(bool allowMulti)
        {
            _multiSelect = allowMulti;
            return this;
        }
        /// <summary>
        /// When <c>true</c>, renders as an editable text field with a small "..." browse button on the right,
        /// allowing the user to type a path or URL directly as well as browse for a file or folder.
        /// When <c>false</c> [default], renders as a "Browse..." button on the left with a read-only path display.
        /// </summary>
        /// <param name="compact"><c>true</c> for inline text + "..." layout.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithCompact(bool compact)
        {
            _compact = compact;
            return this;
        }
        /// <summary>
        /// Sets placeholder hint text shown in the text field when it is empty. Only applies in compact mode.
        /// </summary>
        public IAllowFileSelectorControlSettings WithPlaceholder(string placeholder)
        {
            _placeholder = placeholder ?? "";
            return this;
        }

        /// <summary>
        /// Sets the path the selector starts on, as though it had already been chosen.<br/>
        /// <see cref="SelectedPath"/> reads it back, so a form can open on a sensible default instead of an
        /// empty box. <see cref="SetPath"/> does the same thing after the control is built.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALFileSelector chromeProfile = (GPALFileSelector)GPAL.FileSelector
        ///         .WithSelectFolder(true)
        ///         .WithSelectedPath(@"C:\profiles\chrome\signedin")
        ///         .WithName("chromeProfile")
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        /// <param name="path">The path to start with</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithSelectedPath([FilePath] string path)
        {
            _selectedPath = path ?? "";

            _selectedPaths.Clear();

            if (false == string.IsNullOrEmpty(_selectedPath))
                _selectedPaths.Add(_selectedPath);

            return this;
        }

        public string SelectedPath => _selectedPath;
        /// <summary>
        /// All paths selected in the most recent dialog.
        /// </summary>
        public IReadOnlyList<string> SelectedPaths => _selectedPaths.AsReadOnly();

        /// <summary>
        /// Records the paths chosen in the selection dialog and refreshes the displayed text via
        /// <see cref="SyncDisplay"/>. Called after the dialog is closed.
        /// </summary>
        /// <param name="paths">The selected paths, or <c>null</c>/empty if none were selected.</param>
        internal void UpdateSelection(string[] paths)
        {
            _selectedPaths.Clear();
            if (paths != null && paths.Length > 0)
            {
                _selectedPaths.AddRange(paths);
                _selectedPath = paths[0];
                SyncDisplay();
            }
            else
            {
                _selectedPath = "";
            }
        }
        /// <summary>
        /// Updates the selection via <see cref="UpdateSelection(string[])"/> and then invokes any
        /// registered <see cref="ControlEventType.Change"/>, <see cref="ControlEventType.Click"/>, or
        /// <see cref="ControlEventType.Default"/> callbacks with a <see cref="FileSelectorEventArgs"/>
        /// describing the dialog result. Called after the selection dialog is closed.
        /// </summary>
        /// <param name="cancelled"><c>true</c> if the dialog was cancelled.</param>
        /// <param name="paths">The selected paths, or <c>null</c>/empty if none were selected.</param>
        internal void NotifySelection(bool cancelled, string[] paths)
        {
            UpdateSelection(paths);

            var args = new FileSelectorEventArgs(cancelled, SelectedPath, paths);

            foreach (var (evt, handler) in Callbacks)
            {
                ControlEventType target = evt == ControlEventType.Default ? ControlEventType.Change : evt;

                if (target == ControlEventType.Change ||
                    target == ControlEventType.Click ||
                    target == ControlEventType.Default)
                {
                    ((EventHandler)handler)?.Invoke(this, args);
                }
            }
        }
        public void Clear()
        {
            _selectedPath = "";
            _selectedPaths.Clear();
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
            {
                PostToUi(ctrl, () => ctrl.Text = "");
            }
        }

        public void SetPath([FilePath] string path)
        {
            _selectedPath = path ?? "";
            _selectedPaths.Clear();
            if (!string.IsNullOrEmpty(_selectedPath))
                _selectedPaths.Add(_selectedPath);
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
            {
                PostToUi(ctrl, () => ctrl.Text = _selectedPath);
            }
        }

        private void SyncDisplay()
        {
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
            {
                string display = _selectedPaths.Count > 0
                    ? string.Join("; ", _selectedPaths)
                    : "";

                PostToUi(ctrl, () => ctrl.Text = display);
            }
        }
    }

    #endregion <GPAL Controls>
}


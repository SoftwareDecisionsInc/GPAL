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
using GenerallyPositive;

namespace GenerallyPositive
{
    class WindowHelper
    {
		// TODO: iimplement in GPAL, different methods to close windows
		/// <summary>
		/// Close multiple windows.
		/// </summary>
		/// <param name="selectors">Selectors list of windows that needed to be closed.</param>
		/// <returns><c>true</c> if all windows in list are closed; otherwise returns <c>false</c>.</returns>
		private bool CloseMultipleWindows(Selector selectors)
		{
			logger?.Log(LogLevel.Info, "Enter CloseMultipleWindows");
			try
			{
				if (uiAutomation.AdvancedWaitForElement(selectors))
				{
					int attempt = 1;
					bool areOpen = false;
					do
					{
						logger?.Log(LogLevel.Info, "Scanning for and closing {0} windows, attempt: {1}", selectors.Count, attempt);
						foreach (string selector in selectors)
						{
							this.CloseWindow(selector);
						}
						areOpen = uiAutomation.AdvancedWaitForElement(selectors, 500);
					} while (areOpen && attempt++ < 3);

					if (areOpen)
					{
						logger?.Log(LogLevel.Warn, "Failed to completely close all {0} windows", selectors.Count);
						return false;
					}
				}
				logger?.Log(LogLevel.Debug, "{0} windows are closed", selectors.Count);
				return true;
			}
			catch (Exception ex)
			{
				logger?.Log(LogLevel.Info, $"Exception trying to close multiple windows [{ex.Message}]");
				return false;
			}
		}

		/// <summary>
		/// Close a PowerChart window
		/// </summary>
		/// <param name="title">Title of window</param>
		/// <param name="selector">Window's selector</param>
		/// <param name="closeAction">Close action of the target window</param>
		/// <returns></returns>
		private bool CloseWindow(string selector, params string[] additionalCloseButtons)
		{
			logger?.Log(LogLevel.Info, "Enter CloseWindow");
			try
			{
				if (additionalCloseButtons.Count() > 0)
				{
					CloseButtons.AddRange(additionalCloseButtons);
				}

				if (uiAutomation.WaitForElement(selector, maxWait: 500))
				{
					List<Func<bool>> closeMethods = new List<Func<bool>> {
						()=>this.ImageClose(selector,CloseButtons),()=> this.BoundClose(selector),
						()=> this.HardClose(selector), ()=> this.HardClose(selector,useHardwareEvent:true)
					};
					int attempt = 1;
					bool isClose = false;
					do
					{
						logger?.Log(LogLevel.Info, "Closing window, attempt {0}", attempt);
						foreach (Func<bool> method in closeMethods)
						{
							if (method())
							{
								isClose = uiAutomation.WaitForElementDisappear(selector, 2000);
								break;
							}
						}
					} while (!isClose && attempt++ < 3);
					return isClose;
				}

				logger?.Log(LogLevel.Debug, "Window is close");
				return true;
			}
			catch (Exception ex)
			{
				// ignore
				logger?.Log(LogLevel.Error, $"Error CloseWindow {ex.Message}");
				return false;   // what's the point of a return for this helper? mbv
			}
			finally
			{
				CloseButtons.RemoveAll(btn => additionalCloseButtons.Contains(btn));
			}
		}

		/// <summary>
		/// Hard closing window by using UIAutomation close.
		/// </summary>
		/// <param name="selector">Window's selector, used for identifying closing window.</param>
		/// <param name="useHardwareEvent">A flag to set UIAutomation to use hardware event when closing.</param>
		/// <returns><c>true</c> if window is closed; otherwise returns <c>false</c>.</returns>
		private bool HardClose(string selector, bool useHardwareEvent = false)
		{
			try
			{
				string methodName = useHardwareEvent ? "HardClose with hardware event" : "HardClose";
				logger?.Log(LogLevel.Debug, "{0}: closing window", methodName);
				if (uiAutomation.WaitForElement(selector))
				{
					Action closeAction = delegate {
						uiAutomation.SetForegroundWindow(selector);
						uiAutomation.CloseElement(selector, useHardwareEvents: useHardwareEvent);
					};

					if (!Extend.StopWatchLoop(closeAction, () => uiAutomation.WaitForElementDisappear(selector, maxWait: 1000), 15000))
					{
						return false;
					}
				}
				logger?.Log(LogLevel.Debug, "{0}: Window is closed", methodName);
				return true;
			}
			catch (Exception ex)
			{
				// ignore
				logger?.Log(LogLevel.Error, $"Error HardClose {ex.Message}");
				return false;   // what's the point of a return for this helper? mbv
			}
		}

		/// <summary>
		/// Closing window by using window's bound to navigate to close button location.
		/// </summary>
		/// <param name="selector">Window's selector, used for identifying closing window.</param>
		/// <returns><c>true</c> if window is closed; otherwise returns <c>false</c>.</returns>
		private bool BoundClose(string selector)
		{
			try
			{
				if (uiAutomation.WaitForElement(selector))
				{
					AutomationElement windowElement = uiAutomation.SelectorToElement(selector);
					if (windowElement == null || !windowElement.Exists())
					{
						logger?.Log(LogLevel.Error, "BoundClose: window element of selector is not exist");
						return false;
					}
					Rectangle windowBound = windowElement.Bounds();
					if (windowBound.IsEmpty)
					{
						logger?.Log(LogLevel.Error, "BoundClose: window bounds are empty");
						return false;
					}

					Action closeAction = delegate {
						uiAutomation.SetForegroundWindow(selector);
						InputSimulator.MouseClick(windowBound.Right - 20, windowBound.Top + 10);
					};

					if (!Extend.StopWatchLoop(closeAction, () => uiAutomation.WaitForElementDisappear(selector, maxWait: 1000), 15000))
					{
						return false;
					}
				}

				logger?.Log(LogLevel.Debug, "BoundClose: Window is closed");
				return true;
			}
			catch (Exception ex)
			{
				// ignore
				logger?.Log(LogLevel.Error, $"Error BoundClose {ex.Message}");
				return false;   // what's the point of a return for this helper? mbv
			}
		}

		/// <summary>
		/// Closing window by using the close button image to locate the actual close button in target window and click it.
		/// </summary>
		/// <param name="selector">Window's selector, used for identifying closing window.</param>
		/// <param name="closeImages">List of close button images that is used for finding the close button in target window.</param>
		/// <returns><c>true</c> if window is closed; otherwise returns <c>false</c>.</returns>
		private bool ImageClose(string selector, List<string> closeImages)
		{
			try
			{
				logger?.Log(LogLevel.Debug, "ImageClose: closing window");
				if (uiAutomation.WaitForElement(selector))
				{
					AutomationElement windowElement = uiAutomation.SelectorToElement(selector);
					if (windowElement == null || !windowElement.Exists())
					{
						logger?.Log(LogLevel.Error, "ImageClose: window element of selector is not exist");
						return false;
					}
					Rectangle windowBound = windowElement.Bounds();
					if (windowBound.IsEmpty)
					{
						logger?.Log(LogLevel.Error, "ImageClose: window bounds are empty");
						return false;
					}

					Action closeAction = delegate {
						uiAutomation.SetForegroundWindow(selector);
						imageHelper.ClickAnyWhenPresentInRegion(closeImages, windowBound, maxWait: 1000);
					};

					if (!Extend.StopWatchLoop(closeAction, () => uiAutomation.WaitForElementDisappear(selector, maxWait: 1000), 15000))
					{
						return false;
					}
				}
				logger?.Log(LogLevel.Debug, "ImageClose: window is close");
				return true;
			}
			catch (Exception ex)
			{
				// ignore
				logger?.Log(LogLevel.Error, $"Error ImageClose {ex.Message}");
				return false;   // what's the point of a return for this helper? mbv
			}
		}

	}
}


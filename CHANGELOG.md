# Changelog

Every release of GPAL and GPAL.Image, newest first. GPAL.Image is released only when its own code changes.

## 2.0 (planned)

Changes that would break a working program wait for 2.0. Everything below still works through 1.x.

- Close(bool) is removed; call Close()
- BrowserHelper.KillAllRunningProcesses(bool, IBrowser) is removed; call KillAllRunningProcesses(IBrowser)
- WithServiceAccountKey(GoogleServiceAccountKey) is removed and GoogleServiceAccountKey is internal; pass the key file

## 1.1.4

### GPAL

#### Changed

- GPAL is licensed under the Apache License 2.0, and so is every earlier version

## 1.1.3

### GPAL

#### Changed

- InFrame and InShadowDom wait for their frame or shadow host as long as an action waits for its selector

#### Fixed

- A headless Puppeteer click on an item in an open menu clicks it; the menu no longer closes first
- Entering a same-origin iframe on Puppeteer no longer logs a CDP error

## 1.1.2

### GPAL

#### Changed

- A selector search logs one line with what it found and matched; after a WaitFor, with how long the wait took
- A wait on document.ready logs one line with the status and time, and a warning when the wait runs out
- StartWorkflow logs "Workflow started"

#### Fixed

- The GPAL version and start time are logged when a program turns on console output with WithPublishToConsole
- A splitter is tall enough for every control in its panes; the last one was cut off
- InMainDom, InFrame and InShadowDom publish a failed switch instead of throwing it; a browser that is gone still ends the workflow
- A Selenium browser whose session is lost, or whose windows are all closed, ends the workflow at the next action

## 1.1.1

### GPAL

#### Fixed

- FullScreen works on the Puppeteer engines
- Headless Chrome and Edge maximize and go full screen at the screen's size, not 800x600
- WithWindowSize resizes a browser that is already open
- Restore brings a full screen window back
- A Google service account key file is read correctly

#### Deprecated

- WithServiceAccountKey(GoogleServiceAccountKey); pass the key file or its JSON

## 1.1.0

### GPAL

#### Changed

- Close() ends the browser and its web driver; Release() leaves a browser running
- A selector string is read as CSS or XPath, never both

#### Deprecated

- Close(bool) and KillAllRunningProcesses(bool, IBrowser)

#### Removed

- WithCDPDebugPipe; use AutomationEngine.PuppeteerPipe

#### Added

- .NET 10 build beside the .NET Framework 4.8 build
- Archives open like folders; GPALFile.SaveTo writes zip, gz, tar and 7z
- GPAL.Notify: Slack, Teams, Discord, Google Chat, Telegram, ntfy or any webhook
- Clipboard in and out on browsers and applications
- Applications wait, match and search as browsers do

#### Fixed

- Hidden desktops, tabs and windows on every engine
- Many converter fixes: any format converts to any other without losing data
- Smaller fixes to REST, database parameters and file reading

### GPAL.Image

#### Added

- .NET 10 build beside the .NET Framework 4.8 build

## 1.0.2

### GPAL

#### Changed

- OttoMagic uploads recognize more file types
- Greyed list view rows cannot be ticked
- A button attached with WithCallbackOffUIThread ignores clicks while its handler is running

#### Added

- One-time passwords on GPAL.Credentials: WithOtpSecret, WithOtpQRCode, GetOtpCode and SaveOtpSecretsTo
- GPAL.RegisterQRCodeReader
- List views: WithTickAll, CheckItem and UncheckItem

#### Fixed

- Programs built from the package on .NET Framework get System.ValueTuple.dll
- Placeholders show in inputs, text areas and file selectors, and never become the value
- Password inputs show their placeholder as text

### GPAL.Image

#### Added

- Reads the QR codes WithOtpQRCode takes, phone photos of a screen included

## 1.0.1

### GPAL

#### Changed

- Downloads go straight to the download folder by default; WithPromptForDownload(true) turns the Save As prompt back on
- Network idle on Selenium with Chrome and Edge is counted from the browser, with nothing run in the page
- Network idle waits check every half second
- The stealth Google referrer goes with every GoTo and Get until ClearReferrer or WithUseReferrer clears or replaces it

#### Added

- GPAL.VK_ESCAPE

#### Fixed

- PuppeteerPipe and PuppeteerPipeHW work on Chrome and Edge
- Network idle on Puppeteer
- Drag and drop starts at the selector offset on every engine
- Smaller fixes to downloads, form filling, window sizing and logging

### GPAL.Image

- Released alongside GPAL 1.0.1; no changes

## 1.0.0

### GPAL

- First release

### GPAL.Image

- First release

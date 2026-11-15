# Changelog

Every release of GPAL and GPAL.Image, newest first. The two are released together at the same version.

## 2.0 (planned)

Changes that would break a working program wait for 2.0. Everything below still works through 1.x.

- Close(bool) is removed; call Close()
- BrowserHelper.KillAllRunningProcesses(bool, IBrowser) is removed; call KillAllRunningProcesses(IBrowser)

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

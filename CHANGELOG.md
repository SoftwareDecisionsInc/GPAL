# Changelog

Every release of GPAL and GPAL.Image, newest first. The two are released together at the same version.

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

Torii for macOS
===============

Easiest: double-click "Install Torii.command". It puts Torii in your Applications
folder, clears the download quarantine flag, and opens it. No password needed for
a normal account, nothing else on your Mac is touched.

If macOS refuses to open the .command file ("can't be opened because Apple cannot
check it for malicious software"), either:

  - right-click it and choose Open, then Open again in the dialog, or
  - go to System Settings > Privacy & Security and press "Open Anyway".

Or skip the script and do the same two steps by hand:

  1. Drag Torii.app into your Applications folder.
  2. Open Terminal and run:

       xattr -dr com.apple.quarantine /Applications/Torii.app

     Then open Torii normally.

Why the fuss: Torii isn't signed with a paid Apple developer certificate, and
macOS flags every downloaded app as quarantined. For unsigned apps that shows up
as "Torii is damaged and can't be opened" the first time. Clearing the flag is
safe; you downloaded it yourself.

One-line install / update from Terminal (same thing, no download step):

  curl -fsSL https://lazer.shikkesora.com/install-mac.sh | bash

  For the Nova channel:

  curl -fsSL https://lazer.shikkesora.com/install-mac.sh | bash -s -- nova

Updates: Torii checks for new versions itself and offers to update with one
click. Running the line above again also updates in place.

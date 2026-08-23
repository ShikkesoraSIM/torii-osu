Torii for macOS
===============

Quickest way in (recommended): open Terminal and paste this line.

  curl -fsSL https://lazer.shikkesora.com/install-mac.sh | bash

  For the Nova channel:

  curl -fsSL https://lazer.shikkesora.com/install-mac.sh | bash -s -- nova

It downloads the right build for your Mac, puts Torii in Applications, makes it
launchable and opens it. No password needed for a normal account, nothing else
on your Mac is touched. Running the line again later updates in place, and Torii
also offers updates by itself.

Using this zip instead:

  Double-clicking "Install Torii.command" does the same job, but recent macOS
  versions block downloaded scripts ("Apple could not verify ... is free of
  malware", with only a Move to Trash button). Two ways around it:

  - run it from Terminal, which isn't blocked:

      bash ~/Downloads/"Install Torii.command"

    (adjust the path if you unzipped somewhere else), or

  - after the block, go to System Settings > Privacy & Security, scroll to
    Security, press "Open Anyway" next to the message about the script, and
    double-click it again.

Or do it by hand:

  1. Drag Torii.app into your Applications folder.
  2. In Terminal:

       xattr -dr com.apple.quarantine /Applications/Torii.app
       codesign --force --deep --sign - /Applications/Torii.app

  3. Open Torii normally.

Why all this: Torii isn't signed with a paid Apple developer certificate. macOS
marks every downloaded file as quarantined, and an unsigned quarantined app
shows up as "damaged and can't be opened". On Apple Silicon the system also
refuses to run any program without a signature at all, so the install gives
Torii a local one. Clearing the quarantine flag is safe: you downloaded it
yourself.

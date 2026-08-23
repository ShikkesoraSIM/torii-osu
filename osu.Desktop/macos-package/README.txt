Torii for macOS
===============

1. Double-click "Install Torii.command".
   macOS will refuse and only offer "Move to Trash". That's expected: Torii isn't
   signed with a paid Apple certificate. Press Cancel (or Done).

2. Open System Settings > Privacy & Security, scroll all the way down to the
   Security section. There's a line about "Install Torii.command" with an
   "Open Anyway" button. Press it.

3. Double-click "Install Torii.command" again. The same dialog comes back, but now
   it has an "Open Anyway" option. Pick that.

4. A Terminal window installs Torii into Applications and opens it. Done.
   From then on Torii opens normally, shows up in Spotlight and Launchpad, and
   offers updates by itself.

Prefer Terminal? This one line does the same thing, no dialogs at all:

  curl -fsSL https://lazer.shikkesora.com/install-mac.sh | bash

  (add  -s -- nova  at the end for the Nova channel)

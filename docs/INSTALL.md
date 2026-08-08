# Installing Bandroom

## First time

1. Go to **https://github.com/kingsupreme89/Bandroom-v1/releases/latest**
2. Download **`BandroomSetup.exe`** (only that file — ignore the `.nupkg`/`RELEASES` files, those are for the app's own auto-updater, not for you).
3. Run it. It installs Bandroom and puts one shortcut on your Desktop / Start Menu called **Bandroom**.
4. From now on, **only ever open that one shortcut.** You never need Setup again.

## Getting updates

You don't manually download anything for updates. Bandroom checks for a new version every time it opens. If one's available, an **"↑ Update"** button lights up in the header — click it, done.

## The one mistake to avoid

**Don't keep old copies of `BandroomSetup.exe` around, and don't run one a second time.** Every time you download it, your browser saves a new copy (`BandroomSetup.exe`, `BandroomSetup(1).exe`, `BandroomSetup(2).exe`...) tied to whatever version was current *that day*. If you accidentally double-click an old one later, it will silently **downgrade** your install back to that old version — no warning, and it'll look like features are missing or broken.

If this happens to you: the app now detects it automatically and shows a red **"↑ Fix Version"** button in the header instead of the normal update button — click it to get back to latest. If you ever see that button, it means an old installer got run by mistake.

**Simple rule: delete `BandroomSetup.exe` from your Downloads folder right after you run it.** You won't need it again until you're setting up on a brand new PC.
